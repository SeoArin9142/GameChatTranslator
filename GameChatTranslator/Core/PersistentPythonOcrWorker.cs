using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GameTranslator
{
    [SupportedOSPlatform("windows")]
    public sealed class PersistentPythonOcrWorker : IDisposable
    {
        public const int DefaultInitializationTimeoutMs = 30000;

        private const int MaxCapturedStandardErrorLines = 64;

        private readonly string pythonExecutablePath;
        private readonly string runnerScriptPath;
        private readonly SemaphoreSlim requestLock = new SemaphoreSlim(1, 1);
        private readonly object processSync = new object();
        private readonly Queue<string> standardErrorLines = new Queue<string>();

        private Process process;
        private Task standardErrorPumpTask = Task.CompletedTask;
        private bool isWarm;
        private bool disposed;

        public PersistentPythonOcrWorker(string pythonExecutablePath, string runnerScriptPath)
        {
            this.pythonExecutablePath = pythonExecutablePath ?? "";
            this.runnerScriptPath = runnerScriptPath ?? "";
        }

        public string PythonExecutablePath => pythonExecutablePath;
        public string RunnerScriptPath => runnerScriptPath;

        public async Task<PersistentPythonOcrWorkerResult> SendRequestAsync(string requestJson, int requestTimeoutMs)
        {
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                throw new ArgumentException("요청 JSON이 비어 있습니다.", nameof(requestJson));
            }

            await requestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                bool startedNow;
                try
                {
                    startedNow = EnsureStarted();
                }
                catch (Win32Exception)
                {
                    return PersistentPythonOcrWorkerResult.CreateFailure(
                        "python 또는 py 실행 파일을 찾지 못했습니다.",
                        isPythonMissing: true);
                }
                catch (Exception ex)
                {
                    return PersistentPythonOcrWorkerResult.CreateFailure(ex.Message);
                }

                ClearStandardErrorLines();

                bool usedInitializationTimeout = startedNow || !isWarm;
                int effectiveTimeoutMs = usedInitializationTimeout
                    ? Math.Max(DefaultInitializationTimeoutMs, requestTimeoutMs)
                    : requestTimeoutMs;

                try
                {
                    Process currentProcess = process;
                    if (currentProcess == null || currentProcess.HasExited)
                    {
                        RestartProcess();
                        currentProcess = process;
                    }

                    if (currentProcess == null)
                    {
                        return PersistentPythonOcrWorkerResult.CreateFailure("Python 워커를 시작하지 못했습니다.");
                    }

                    await currentProcess.StandardInput.WriteLineAsync(requestJson).ConfigureAwait(false);
                    await currentProcess.StandardInput.FlushAsync().ConfigureAwait(false);

                    string responseLine = await currentProcess.StandardOutput.ReadLineAsync()
                        .WaitAsync(TimeSpan.FromMilliseconds(effectiveTimeoutMs))
                        .ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(responseLine))
                    {
                        RestartProcess();
                        return PersistentPythonOcrWorkerResult.CreateFailure(
                            "Python 워커가 빈 응답을 반환했습니다.",
                            standardError: GetCapturedStandardErrorText());
                    }

                    isWarm = true;
                    return PersistentPythonOcrWorkerResult.CreateSuccess(
                        responseLine,
                        GetCapturedStandardErrorText(),
                        usedInitializationTimeout);
                }
                catch (TimeoutException)
                {
                    string standardError = GetCapturedStandardErrorText();
                    RestartProcess();
                    return PersistentPythonOcrWorkerResult.CreateFailure(
                        $"Python 워커 응답이 {effectiveTimeoutMs}ms 안에 도착하지 않았습니다.",
                        standardError,
                        timedOut: true);
                }
                catch (Win32Exception)
                {
                    RestartProcess();
                    return PersistentPythonOcrWorkerResult.CreateFailure(
                        "python 또는 py 실행 파일을 찾지 못했습니다.",
                        isPythonMissing: true,
                        standardError: GetCapturedStandardErrorText());
                }
                catch (Exception ex)
                {
                    string standardError = GetCapturedStandardErrorText();
                    RestartProcess();
                    return PersistentPythonOcrWorkerResult.CreateFailure(ex.Message, standardError);
                }
            }
            finally
            {
                requestLock.Release();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StopProcess();
        }

        internal IReadOnlyList<string> BuildWorkerStartArguments()
        {
            return new[]
            {
                "-X",
                "utf8",
                "-u",
                runnerScriptPath,
                "--worker"
            };
        }

        private bool EnsureStarted()
        {
            lock (processSync)
            {
                ThrowIfDisposed();

                if (process != null && !process.HasExited)
                {
                    return false;
                }

                StopProcessUnsafe();

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = pythonExecutablePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardInputEncoding = Encoding.UTF8,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                foreach (string argument in BuildWorkerStartArguments())
                {
                    processStartInfo.ArgumentList.Add(argument);
                }

                process = Process.Start(processStartInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Python 워커 프로세스를 시작하지 못했습니다.");
                }

                isWarm = false;
                standardErrorPumpTask = PumpStandardErrorAsync(process);
                return true;
            }
        }

        private void RestartProcess()
        {
            lock (processSync)
            {
                if (disposed)
                {
                    return;
                }

                StopProcessUnsafe();
                try
                {
                    EnsureStarted();
                }
                catch
                {
                    StopProcessUnsafe();
                }
            }
        }

        private void StopProcess()
        {
            lock (processSync)
            {
                StopProcessUnsafe();
            }
        }

        private void StopProcessUnsafe()
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            try
            {
                process.Dispose();
            }
            catch
            {
            }

            process = null;
            isWarm = false;
            ClearStandardErrorLines();
        }

        private async Task PumpStandardErrorAsync(Process currentProcess)
        {
            try
            {
                while (true)
                {
                    string line = await currentProcess.StandardError.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                    {
                        break;
                    }

                    AppendStandardErrorLine(line);
                }
            }
            catch
            {
            }
        }

        private void AppendStandardErrorLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            lock (standardErrorLines)
            {
                standardErrorLines.Enqueue(line.Trim());
                while (standardErrorLines.Count > MaxCapturedStandardErrorLines)
                {
                    standardErrorLines.Dequeue();
                }
            }
        }

        private void ClearStandardErrorLines()
        {
            lock (standardErrorLines)
            {
                standardErrorLines.Clear();
            }
        }

        private string GetCapturedStandardErrorText()
        {
            lock (standardErrorLines)
            {
                return string.Join(Environment.NewLine, standardErrorLines.ToArray());
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(PersistentPythonOcrWorker));
            }
        }
    }

    public sealed class PersistentPythonOcrWorkerResult
    {
        private PersistentPythonOcrWorkerResult(
            bool success,
            string responseJson,
            string errorMessage,
            string standardError,
            bool isPythonMissing,
            bool timedOut,
            bool usedInitializationTimeout)
        {
            Success = success;
            ResponseJson = responseJson ?? "";
            ErrorMessage = errorMessage ?? "";
            StandardError = standardError ?? "";
            IsPythonMissing = isPythonMissing;
            TimedOut = timedOut;
            UsedInitializationTimeout = usedInitializationTimeout;
        }

        public bool Success { get; }
        public string ResponseJson { get; }
        public string ErrorMessage { get; }
        public string StandardError { get; }
        public bool IsPythonMissing { get; }
        public bool TimedOut { get; }
        public bool UsedInitializationTimeout { get; }

        public static PersistentPythonOcrWorkerResult CreateSuccess(
            string responseJson,
            string standardError,
            bool usedInitializationTimeout)
        {
            return new PersistentPythonOcrWorkerResult(
                true,
                responseJson,
                "",
                standardError,
                false,
                false,
                usedInitializationTimeout);
        }

        public static PersistentPythonOcrWorkerResult CreateFailure(
            string errorMessage,
            string standardError = "",
            bool isPythonMissing = false,
            bool timedOut = false)
        {
            return new PersistentPythonOcrWorkerResult(
                false,
                "",
                errorMessage,
                standardError,
                isPythonMissing,
                timedOut,
                false);
        }
    }
}
