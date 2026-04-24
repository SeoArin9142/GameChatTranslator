using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameTranslator
{
    /// <summary>
    /// 실험 브랜치에서 EasyOCR를 외부 Python 프로세스로 호출합니다.
    /// 메인 번역 경로는 건드리지 않고, OCR 진단 화면에서 Win OCR/Tesseract와 비교하기 위한 배치 실행만 담당합니다.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class EasyOcrCliAdapter : IDisposable
    {
        private const string RunnerFileName = "easyocr_runner.py";
        private readonly Dictionary<string, PersistentPythonOcrWorker> workerByPythonPath = new Dictionary<string, PersistentPythonOcrWorker>(StringComparer.OrdinalIgnoreCase);
        private readonly object workerSync = new object();

        private static readonly Dictionary<string, string> AppLanguageToEasyOcrMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ko"] = "ko",
            ["ko-KR"] = "ko",
            ["en"] = "en",
            ["en-US"] = "en",
            ["ja"] = "ja",
            ["ja-JP"] = "ja",
            ["zh-Hans-CN"] = "ch_sim",
            ["zh-CN"] = "ch_sim",
            ["ru"] = "ru",
            ["ru-RU"] = "ru"
        };

        private static readonly string[] DefaultLanguagePriority =
        {
            "ja",
            "ko",
            "ch_sim"
        };

        public string BuildLanguageCodes(string configuredValue, string gameLanguage)
        {
            if (!string.IsNullOrWhiteSpace(configuredValue) &&
                !string.Equals(configuredValue.Trim(), SettingsService.DefaultEasyOcrLanguageCodes, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeLanguageCodes(configuredValue);
            }

            var codes = new List<string>();
            string mappedGameLanguage = MapAppLanguageTagToEasyOcr(gameLanguage);
            if (!string.IsNullOrWhiteSpace(mappedGameLanguage))
            {
                codes.Add(mappedGameLanguage);
            }

            codes.Add("en");
            codes.Add("ko");
            codes.Add("ja");
            codes.Add("ch_sim");

            return string.Join("+", codes.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        public IReadOnlyList<string> BuildLanguageCombinations(string configuredValue, string gameLanguage)
        {
            string normalizedConfiguredValue = (configuredValue ?? "").Trim();

            if (string.IsNullOrWhiteSpace(normalizedConfiguredValue) ||
                string.Equals(normalizedConfiguredValue, SettingsService.DefaultEasyOcrLanguageCodes, StringComparison.OrdinalIgnoreCase))
            {
                var combinations = new List<string>();
                string mappedGameLanguage = MapAppLanguageTagToEasyOcr(gameLanguage);
                if (!string.IsNullOrWhiteSpace(mappedGameLanguage) &&
                    !string.Equals(mappedGameLanguage, "en", StringComparison.OrdinalIgnoreCase))
                {
                    combinations.Add(BuildPair(mappedGameLanguage));
                }

                foreach (string languageCode in DefaultLanguagePriority)
                {
                    combinations.Add(BuildPair(languageCode));
                }

                return combinations
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (normalizedConfiguredValue.Contains('|') || normalizedConfiguredValue.Contains('\n'))
            {
                return normalizedConfiguredValue
                    .Split(new[] { '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(NormalizeLanguageCodes)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return new[] { NormalizeLanguageCodes(normalizedConfiguredValue) };
        }

        public string NormalizeLanguageCodes(string rawValue)
        {
            IEnumerable<string> tokens = (rawValue ?? "")
                .Split(new[] { '+', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => MapAppLanguageTagToEasyOcr(token.Trim()))
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            string normalized = string.Join("+", tokens);
            return string.IsNullOrWhiteSpace(normalized) ? SettingsService.DefaultEasyOcrLanguageCodes : normalized;
        }

        public string MapAppLanguageTagToEasyOcr(string languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                return "";
            }

            string normalized = languageTag.Trim();
            return AppLanguageToEasyOcrMap.TryGetValue(normalized, out string mapped)
                ? mapped
                : normalized;
        }

        public EasyOcrCliBatchResult Recognize(
            Bitmap bitmap,
            string configuredPythonPath,
            string configuredLanguageCodes,
            string gameLanguage,
            int timeoutMs = 120000)
        {
            if (bitmap == null)
            {
                return EasyOcrCliBatchResult.CreateFailure("", new List<string>(), "OCR 입력 이미지가 없습니다.");
            }

            IReadOnlyList<string> languageCombinations = BuildLanguageCombinations(configuredLanguageCodes, gameLanguage);
            string runnerScriptPath = GetRunnerScriptPath();
            if (!File.Exists(runnerScriptPath))
            {
                return EasyOcrCliBatchResult.CreateFailure(
                    "",
                    languageCombinations,
                    "easyocr_runner.py를 찾지 못했습니다. 게시 산출물에 EasyOCR 러너가 포함됐는지 확인해 주세요.");
            }

            string inputDirectory = Path.Combine(Path.GetTempPath(), "GameChatTranslator", "EasyOCR");
            Directory.CreateDirectory(inputDirectory);

            string inputFilePath = Path.Combine(inputDirectory, $"ocr_{Guid.NewGuid():N}.png");
            bitmap.Save(inputFilePath, ImageFormat.Png);

            try
            {
                foreach (string pythonPath in GetPythonCandidates(configuredPythonPath))
                {
                    EasyOcrCliBatchResult runResult = TryRecognizeWithResidentWorker(
                        pythonPath,
                        runnerScriptPath,
                        inputFilePath,
                        languageCombinations,
                        timeoutMs);
                    if (runResult.Success || !runResult.IsPythonMissing)
                    {
                        return runResult;
                    }
                }

                return EasyOcrCliBatchResult.CreateFailure(
                    SettingsService.DefaultEasyOcrPythonPath,
                    languageCombinations,
                    "python 또는 py 실행 파일을 찾지 못했습니다. PATH 또는 config.ini의 EasyOcrPythonPath를 확인해 주세요.",
                    isPythonMissing: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(inputFilePath))
                    {
                        File.Delete(inputFilePath);
                    }
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            lock (workerSync)
            {
                foreach (PersistentPythonOcrWorker worker in workerByPythonPath.Values)
                {
                    worker.Dispose();
                }

                workerByPythonPath.Clear();
            }
        }

        internal string GetRunnerScriptPath()
        {
            return Path.Combine(AppContext.BaseDirectory, RunnerFileName);
        }

        internal IReadOnlyList<string> BuildArguments(
            string runnerScriptPath,
            string inputFilePath,
            IReadOnlyList<string> languageCombinations)
        {
            return new[]
            {
                "-X",
                "utf8",
                runnerScriptPath,
                "--image",
                inputFilePath,
                "--groups",
                string.Join("|", languageCombinations ?? Array.Empty<string>()),
                "--gpu",
                "false"
            };
        }

        internal IReadOnlyList<string> BuildWorkerStartArguments(string runnerScriptPath)
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

        internal string BuildPair(string languageCode)
        {
            return string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase)
                ? "en"
                : $"{languageCode}+en";
        }

        internal string GetFailureMessageForExitCode(int exitCode, string standardError)
        {
            string stderr = EmptyToFallback(standardError, "");
            return exitCode switch
            {
                3 => "EasyOCR 모듈을 찾지 못했습니다. py -m pip install torch torchvision easyocr 또는 python -m pip install torch torchvision easyocr 후 다시 실행해 주세요.",
                4 => EmptyToFallback(stderr, "EasyOCR 실행 중 오류가 발생했습니다."),
                _ => EmptyToFallback(stderr, $"EasyOCR 종료 코드: {exitCode}")
            };
        }

        internal IReadOnlyList<string> GetPythonCandidatesForTesting(string configuredPythonPath)
        {
            return GetPythonCandidates(configuredPythonPath).ToList();
        }

        private IEnumerable<string> GetPythonCandidates(string configuredPythonPath)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(configuredPythonPath))
            {
                candidates.Add(configuredPythonPath.Trim());
            }

            string environmentPath = Environment.GetEnvironmentVariable("EASYOCR_PYTHON_PATH");
            if (!string.IsNullOrWhiteSpace(environmentPath))
            {
                candidates.Add(environmentPath.Trim());
            }

            candidates.Add(SettingsService.DefaultEasyOcrPythonPath);
            candidates.Add("py");
            candidates.Add("python3");

            return candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private EasyOcrCliBatchResult TryRecognizeInternal(
            string pythonExecutablePath,
            string runnerScriptPath,
            string inputFilePath,
            IReadOnlyList<string> languageCombinations,
            int timeoutMs)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = pythonExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (string argument in BuildArguments(runnerScriptPath, inputFilePath, languageCombinations))
            {
                processStartInfo.ArgumentList.Add(argument);
            }

            try
            {
                using Process process = Process.Start(processStartInfo);
                if (process == null)
                {
                    return EasyOcrCliBatchResult.CreateFailure(pythonExecutablePath, languageCombinations, "EasyOCR Python 프로세스를 시작하지 못했습니다.");
                }

                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    return EasyOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCombinations,
                        $"EasyOCR 실행이 {timeoutMs}ms 안에 끝나지 않았습니다.");
                }

                if (process.ExitCode != 0)
                {
                    return EasyOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCombinations,
                        GetFailureMessageForExitCode(process.ExitCode, standardError),
                        isModuleMissing: process.ExitCode == 3);
                }

                List<EasyOcrCliGroupResult> groupResults = ParseGroupResults(standardOutput);
                if (groupResults.Count == 0)
                {
                    return EasyOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCombinations,
                        EmptyToFallback(standardError, "EasyOCR 결과를 읽지 못했습니다."));
                }

                int successCount = groupResults.Count(group => group.Success);
                if (successCount == 0)
                {
                    string firstErrorMessage = groupResults
                        .Select(group => group.ErrorMessage)
                        .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
                    return EasyOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCombinations,
                        EmptyToFallback(firstErrorMessage, "EasyOCR 결과가 모두 실패했습니다."));
                }

                return EasyOcrCliBatchResult.CreateSuccess(pythonExecutablePath, languageCombinations, groupResults, standardError);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return EasyOcrCliBatchResult.CreateFailure(
                    pythonExecutablePath,
                    languageCombinations,
                    "python 또는 py 실행 파일을 찾지 못했습니다.",
                    isPythonMissing: true);
            }
            catch (Exception ex)
            {
                return EasyOcrCliBatchResult.CreateFailure(pythonExecutablePath, languageCombinations, ex.Message);
            }
        }

        private EasyOcrCliBatchResult TryRecognizeWithResidentWorker(
            string pythonExecutablePath,
            string runnerScriptPath,
            string inputFilePath,
            IReadOnlyList<string> languageCombinations,
            int timeoutMs)
        {
            try
            {
                PersistentPythonOcrWorker worker = GetOrCreateWorker(pythonExecutablePath, runnerScriptPath);
                string requestJson = JsonSerializer.Serialize(new EasyOcrWorkerRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    ImagePath = inputFilePath,
                    Groups = string.Join("|", languageCombinations ?? Array.Empty<string>()),
                    Gpu = false
                });

                PersistentPythonOcrWorkerResult workerResult = worker.SendRequestAsync(requestJson, timeoutMs).GetAwaiter().GetResult();
                if (!workerResult.Success)
                {
                    return EasyOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCombinations,
                        EmptyToFallback(workerResult.ErrorMessage, "EasyOCR 워커 요청에 실패했습니다."),
                        isPythonMissing: workerResult.IsPythonMissing,
                        standardError: workerResult.StandardError,
                        usedResidentWorker: true,
                        startedWorker: workerResult.StartedWorker,
                        restartedWorker: workerResult.RestartedWorker,
                        usedInitializationTimeout: workerResult.UsedInitializationTimeout,
                        timedOut: workerResult.TimedOut);
                }

                EasyOcrWorkerResponse response = ParseWorkerResponse(workerResult.ResponseJson);
                if (response == null)
                {
                    return EasyOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCombinations,
                        "EasyOCR 워커 응답을 해석하지 못했습니다.",
                        standardError: workerResult.StandardError,
                        usedResidentWorker: true,
                        startedWorker: workerResult.StartedWorker,
                        restartedWorker: workerResult.RestartedWorker,
                        usedInitializationTimeout: workerResult.UsedInitializationTimeout);
                }

                if (!response.Ok)
                {
                    bool isModuleMissing = string.Equals(response.ErrorCode, "module_missing", StringComparison.OrdinalIgnoreCase);
                    string errorMessage = isModuleMissing
                        ? GetFailureMessageForExitCode(3, response.Error)
                        : EmptyToFallback(response.Error, "EasyOCR 워커가 오류를 반환했습니다.");

                    return EasyOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCombinations,
                        errorMessage,
                        isModuleMissing: isModuleMissing,
                        standardError: workerResult.StandardError,
                        usedResidentWorker: true,
                        startedWorker: workerResult.StartedWorker,
                        restartedWorker: workerResult.RestartedWorker,
                        usedInitializationTimeout: workerResult.UsedInitializationTimeout);
                }

                List<EasyOcrCliGroupResult> groupResults = ConvertWorkerGroupsToResults(response.Groups);
                if (groupResults.Count == 0)
                {
                    return EasyOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCombinations,
                        EmptyToFallback(workerResult.StandardError, "EasyOCR 결과를 읽지 못했습니다."),
                        standardError: workerResult.StandardError,
                        usedResidentWorker: true,
                        startedWorker: workerResult.StartedWorker,
                        restartedWorker: workerResult.RestartedWorker,
                        usedInitializationTimeout: workerResult.UsedInitializationTimeout);
                }

                int successCount = groupResults.Count(group => group.Success);
                if (successCount == 0)
                {
                    string firstErrorMessage = groupResults
                        .Select(group => group.ErrorMessage)
                        .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
                    return EasyOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCombinations,
                        EmptyToFallback(firstErrorMessage, "EasyOCR 결과가 모두 실패했습니다."),
                        standardError: workerResult.StandardError,
                        usedResidentWorker: true,
                        startedWorker: workerResult.StartedWorker,
                        restartedWorker: workerResult.RestartedWorker,
                        usedInitializationTimeout: workerResult.UsedInitializationTimeout);
                }

                return EasyOcrCliBatchResult.CreateSuccess(
                    pythonExecutablePath,
                    languageCombinations,
                    groupResults,
                    workerResult.StandardError,
                    standardError: workerResult.StandardError,
                    usedResidentWorker: true,
                    startedWorker: workerResult.StartedWorker,
                    restartedWorker: workerResult.RestartedWorker,
                    usedInitializationTimeout: workerResult.UsedInitializationTimeout);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                lock (workerSync)
                {
                    if (workerByPythonPath.TryGetValue(pythonExecutablePath, out PersistentPythonOcrWorker worker))
                    {
                        worker.Dispose();
                        workerByPythonPath.Remove(pythonExecutablePath);
                    }
                }

                return EasyOcrCliBatchResult.CreateFailure(
                    pythonExecutablePath,
                    languageCombinations,
                    "python 또는 py 실행 파일을 찾지 못했습니다.",
                    isPythonMissing: true);
            }
            catch (Exception ex)
            {
                return EasyOcrCliBatchResult.CreateFailure(
                    pythonExecutablePath,
                    languageCombinations,
                    ex.Message);
            }
        }

        internal static List<EasyOcrCliGroupResult> ParseGroupResults(string json)
        {
            EasyOcrRunnerResponse response = JsonSerializer.Deserialize<EasyOcrRunnerResponse>(json ?? "", new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return (response?.Groups ?? new List<EasyOcrRunnerGroup>())
                .Select(group => new EasyOcrCliGroupResult(
                    group?.LanguageCodes ?? "",
                    group?.Success ?? false,
                    (group?.Lines ?? new List<EasyOcrRunnerLine>())
                        .Where(line => !string.IsNullOrWhiteSpace(line?.Text))
                        .Select(line => new OcrLine
                        {
                            Top = line.Top,
                            Bottom = line.Bottom,
                            Text = line.Text.Trim()
                        })
                        .ToList(),
                    group?.Error ?? ""))
                .ToList();
        }

        internal static EasyOcrWorkerResponse ParseWorkerResponse(string json)
        {
            return JsonSerializer.Deserialize<EasyOcrWorkerResponse>(json ?? "", new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        internal static List<EasyOcrCliGroupResult> ConvertWorkerGroupsToResults(IReadOnlyList<EasyOcrWorkerGroup> groups)
        {
            return (groups ?? Array.Empty<EasyOcrWorkerGroup>())
                .Select(group => new EasyOcrCliGroupResult(
                    group?.LanguageCodes ?? "",
                    group?.Success ?? false,
                    (group?.Lines ?? new List<EasyOcrWorkerLine>())
                        .Where(line => !string.IsNullOrWhiteSpace(line?.Text))
                        .Select(line => new OcrLine
                        {
                            Top = line.Top,
                            Bottom = line.Bottom,
                            Text = line.Text.Trim()
                        })
                        .ToList(),
                    group?.Error ?? ""))
                .ToList();
        }

        private static string EmptyToFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private PersistentPythonOcrWorker GetOrCreateWorker(string pythonExecutablePath, string runnerScriptPath)
        {
            lock (workerSync)
            {
                if (!workerByPythonPath.TryGetValue(pythonExecutablePath, out PersistentPythonOcrWorker worker))
                {
                    worker = new PersistentPythonOcrWorker(pythonExecutablePath, runnerScriptPath);
                    workerByPythonPath.Add(pythonExecutablePath, worker);
                }

                return worker;
            }
        }

        private sealed class EasyOcrRunnerResponse
        {
            public List<EasyOcrRunnerGroup> Groups { get; set; } = new List<EasyOcrRunnerGroup>();
        }

        private sealed class EasyOcrRunnerGroup
        {
            [JsonPropertyName("language_codes")]
            public string LanguageCodes { get; set; } = "";
            public bool Success { get; set; }
            public string Error { get; set; } = "";
            public List<EasyOcrRunnerLine> Lines { get; set; } = new List<EasyOcrRunnerLine>();
        }

        private sealed class EasyOcrRunnerLine
        {
            public double Top { get; set; }
            public double Bottom { get; set; }
            public string Text { get; set; } = "";
        }

        private sealed class EasyOcrWorkerRequest
        {
            public string RequestId { get; set; } = "";
            public string ImagePath { get; set; } = "";
            public string Groups { get; set; } = "";
            public bool Gpu { get; set; }
        }

        internal sealed class EasyOcrWorkerResponse
        {
            public string RequestId { get; set; } = "";
            public bool Ok { get; set; }
            public string Error { get; set; } = "";
            public string ErrorCode { get; set; } = "";
            public List<EasyOcrWorkerGroup> Groups { get; set; } = new List<EasyOcrWorkerGroup>();
        }

        internal sealed class EasyOcrWorkerGroup
        {
            [JsonPropertyName("language_codes")]
            public string LanguageCodes { get; set; } = "";
            public bool Success { get; set; }
            public string Error { get; set; } = "";
            public List<EasyOcrWorkerLine> Lines { get; set; } = new List<EasyOcrWorkerLine>();
        }

        internal sealed class EasyOcrWorkerLine
        {
            public double Top { get; set; }
            public double Bottom { get; set; }
            public string Text { get; set; } = "";
        }
    }

    public sealed class EasyOcrCliBatchResult
    {
        private EasyOcrCliBatchResult(
            bool success,
            string pythonExecutablePath,
            IReadOnlyList<string> languageCombinations,
            IReadOnlyList<EasyOcrCliGroupResult> groupResults,
            string errorMessage,
            bool isPythonMissing,
            bool isModuleMissing,
            string standardError,
            bool usedResidentWorker,
            bool startedWorker,
            bool restartedWorker,
            bool usedInitializationTimeout,
            bool timedOut)
        {
            Success = success;
            PythonExecutablePath = pythonExecutablePath ?? "";
            LanguageCombinations = languageCombinations ?? Array.Empty<string>();
            GroupResults = groupResults ?? Array.Empty<EasyOcrCliGroupResult>();
            ErrorMessage = errorMessage ?? "";
            IsPythonMissing = isPythonMissing;
            IsModuleMissing = isModuleMissing;
            StandardError = standardError ?? "";
            UsedResidentWorker = usedResidentWorker;
            StartedWorker = startedWorker;
            RestartedWorker = restartedWorker;
            UsedInitializationTimeout = usedInitializationTimeout;
            TimedOut = timedOut;
        }

        public bool Success { get; }
        public string PythonExecutablePath { get; }
        public IReadOnlyList<string> LanguageCombinations { get; }
        public IReadOnlyList<EasyOcrCliGroupResult> GroupResults { get; }
        public string ErrorMessage { get; }
        public bool IsPythonMissing { get; }
        public bool IsModuleMissing { get; }
        public string StandardError { get; }
        public bool UsedResidentWorker { get; }
        public bool StartedWorker { get; }
        public bool RestartedWorker { get; }
        public bool UsedInitializationTimeout { get; }
        public bool TimedOut { get; }

        public static EasyOcrCliBatchResult CreateSuccess(
            string pythonExecutablePath,
            IReadOnlyList<string> languageCombinations,
            IReadOnlyList<EasyOcrCliGroupResult> groupResults,
            string errorMessage = "",
            string standardError = "",
            bool usedResidentWorker = false,
            bool startedWorker = false,
            bool restartedWorker = false,
            bool usedInitializationTimeout = false)
        {
            return new EasyOcrCliBatchResult(
                true,
                pythonExecutablePath,
                languageCombinations,
                groupResults,
                errorMessage,
                false,
                false,
                standardError,
                usedResidentWorker,
                startedWorker,
                restartedWorker,
                usedInitializationTimeout,
                false);
        }

        public static EasyOcrCliBatchResult CreateFailure(
            string pythonExecutablePath,
            IReadOnlyList<string> languageCombinations,
            string errorMessage,
            bool isPythonMissing = false,
            bool isModuleMissing = false,
            string standardError = "",
            bool usedResidentWorker = false,
            bool startedWorker = false,
            bool restartedWorker = false,
            bool usedInitializationTimeout = false,
            bool timedOut = false)
        {
            return new EasyOcrCliBatchResult(
                false,
                pythonExecutablePath,
                languageCombinations,
                Array.Empty<EasyOcrCliGroupResult>(),
                errorMessage,
                isPythonMissing,
                isModuleMissing,
                standardError,
                usedResidentWorker,
                startedWorker,
                restartedWorker,
                usedInitializationTimeout,
                timedOut);
        }
    }

    public sealed class EasyOcrCliGroupResult
    {
        public EasyOcrCliGroupResult(string languageCodes, bool success, IReadOnlyList<OcrLine> lines, string errorMessage)
        {
            LanguageCodes = languageCodes ?? "";
            Success = success;
            Lines = lines ?? Array.Empty<OcrLine>();
            ErrorMessage = errorMessage ?? "";
        }

        public string LanguageCodes { get; }
        public bool Success { get; }
        public IReadOnlyList<OcrLine> Lines { get; }
        public string ErrorMessage { get; }
    }
}
