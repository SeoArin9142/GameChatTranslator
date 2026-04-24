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

namespace GameTranslator
{
    /// <summary>
    /// 실험 브랜치에서 PaddleOCR를 외부 Python 프로세스로 호출합니다.
    /// 메인 번역 경로는 건드리지 않고, OCR 진단 화면에서 Win OCR/Tesseract/EasyOCR와 비교하기 위한 배치 실행만 담당합니다.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class PaddleOcrCliAdapter : IDisposable
    {
        private const string EngineType = "paddleocr";
        private const string RunnerFileName = "paddleocr_runner.py";
        private readonly Dictionary<string, PersistentPythonOcrWorkerLease> workerLeaseByPythonPath = new Dictionary<string, PersistentPythonOcrWorkerLease>(StringComparer.OrdinalIgnoreCase);
        private readonly object workerSync = new object();

        private static readonly Dictionary<string, string> AppLanguageToPaddleOcrMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ko"] = "korean",
            ["ko-KR"] = "korean",
            ["en"] = "en",
            ["en-US"] = "en",
            ["ja"] = "japan",
            ["ja-JP"] = "japan",
            ["zh-Hans-CN"] = "ch",
            ["zh-CN"] = "ch",
            ["ru"] = "ru",
            ["ru-RU"] = "ru"
        };

        private static readonly string[] DefaultLanguagePriority =
        {
            "en",
            "korean",
            "japan",
            "ch"
        };

        public string BuildLanguageCodes(string configuredValue, string gameLanguage)
        {
            if (!string.IsNullOrWhiteSpace(configuredValue) &&
                !string.Equals(configuredValue.Trim(), SettingsService.DefaultPaddleOcrLanguageCodes, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeLanguageCodes(configuredValue);
            }

            var codes = new List<string>();
            string mappedGameLanguage = MapAppLanguageTagToPaddleOcr(gameLanguage);
            if (!string.IsNullOrWhiteSpace(mappedGameLanguage))
            {
                codes.Add(mappedGameLanguage);
            }

            codes.AddRange(DefaultLanguagePriority);
            return string.Join("+", codes.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        public IReadOnlyList<string> BuildLanguageCandidates(string configuredValue, string gameLanguage)
        {
            string normalizedConfiguredValue = (configuredValue ?? "").Trim();

            if (string.IsNullOrWhiteSpace(normalizedConfiguredValue) ||
                string.Equals(normalizedConfiguredValue, SettingsService.DefaultPaddleOcrLanguageCodes, StringComparison.OrdinalIgnoreCase))
            {
                var candidates = new List<string>();
                string mappedGameLanguage = MapAppLanguageTagToPaddleOcr(gameLanguage);
                if (!string.IsNullOrWhiteSpace(mappedGameLanguage))
                {
                    candidates.Add(mappedGameLanguage);
                }

                candidates.AddRange(DefaultLanguagePriority);
                return candidates
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

            return NormalizeLanguageCodes(normalizedConfiguredValue)
                .Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string NormalizeLanguageCodes(string rawValue)
        {
            IEnumerable<string> tokens = (rawValue ?? "")
                .Split(new[] { '+', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => MapAppLanguageTagToPaddleOcr(token.Trim()))
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            string normalized = string.Join("+", tokens);
            return string.IsNullOrWhiteSpace(normalized) ? SettingsService.DefaultPaddleOcrLanguageCodes : normalized;
        }

        public string MapAppLanguageTagToPaddleOcr(string languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                return "";
            }

            string normalized = languageTag.Trim();
            return AppLanguageToPaddleOcrMap.TryGetValue(normalized, out string mapped)
                ? mapped
                : normalized;
        }

        public PaddleOcrCliBatchResult Recognize(
            Bitmap bitmap,
            string configuredPythonPath,
            string configuredLanguageCodes,
            string gameLanguage,
            int timeoutMs = 120000)
        {
            return RecognizeBatch(
                bitmap == null ? Array.Empty<Bitmap>() : new[] { bitmap },
                configuredPythonPath,
                configuredLanguageCodes,
                gameLanguage,
                timeoutMs);
        }

        public PaddleOcrCliBatchResult RecognizeBatch(
            IReadOnlyList<Bitmap> bitmaps,
            string configuredPythonPath,
            string configuredLanguageCodes,
            string gameLanguage,
            int timeoutMs = 120000)
        {
            if (bitmaps == null || bitmaps.Count == 0 || bitmaps.Any(bitmap => bitmap == null))
            {
                return PaddleOcrCliBatchResult.CreateFailure("", new List<string>(), "OCR 입력 이미지가 없습니다.");
            }

            IReadOnlyList<string> languageCandidates = BuildLanguageCandidates(configuredLanguageCodes, gameLanguage);
            string runnerScriptPath = GetRunnerScriptPath();
            if (!File.Exists(runnerScriptPath))
            {
                return PaddleOcrCliBatchResult.CreateFailure(
                    "",
                    languageCandidates,
                    "paddleocr_runner.py를 찾지 못했습니다. 게시 산출물에 PaddleOCR 러너가 포함됐는지 확인해 주세요.");
            }

            string inputDirectory = Path.Combine(Path.GetTempPath(), "GameChatTranslator", "PaddleOCR");
            Directory.CreateDirectory(inputDirectory);

            var inputFilePaths = new List<string>();
            for (int index = 0; index < bitmaps.Count; index++)
            {
                string inputFilePath = Path.Combine(inputDirectory, $"ocr_{Guid.NewGuid():N}_{index}.png");
                bitmaps[index].Save(inputFilePath, ImageFormat.Png);
                inputFilePaths.Add(inputFilePath);
            }

            try
            {
                foreach (string pythonPath in GetPythonCandidates(configuredPythonPath))
                {
                    PaddleOcrCliBatchResult runResult = TryRecognizeWithResidentWorker(
                        pythonPath,
                        runnerScriptPath,
                        inputFilePaths,
                        languageCandidates,
                        timeoutMs);
                    if (runResult.Success || !runResult.IsPythonMissing)
                    {
                        return runResult;
                    }
                }

                return PaddleOcrCliBatchResult.CreateFailure(
                    SettingsService.DefaultPaddleOcrPythonPath,
                    languageCandidates,
                    "python 또는 py 실행 파일을 찾지 못했습니다. PATH 또는 config.ini의 PaddleOcrPythonPath를 확인해 주세요.",
                    isPythonMissing: true);
            }
            finally
            {
                try
                {
                    foreach (string inputFilePath in inputFilePaths)
                    {
                        if (File.Exists(inputFilePath))
                        {
                            File.Delete(inputFilePath);
                        }
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
                foreach (PersistentPythonOcrWorkerLease workerLease in workerLeaseByPythonPath.Values)
                {
                    workerLease.Dispose();
                }

                workerLeaseByPythonPath.Clear();
            }
        }

        internal string GetRunnerScriptPath()
        {
            return Path.Combine(AppContext.BaseDirectory, RunnerFileName);
        }

        internal IReadOnlyList<string> BuildArguments(
            string runnerScriptPath,
            string inputFilePath,
            IReadOnlyList<string> languageCandidates)
        {
            return BuildBatchArguments(runnerScriptPath, new[] { inputFilePath }, languageCandidates);
        }

        internal IReadOnlyList<string> BuildBatchArguments(
            string runnerScriptPath,
            IReadOnlyList<string> inputFilePaths,
            IReadOnlyList<string> languageCandidates)
        {
            return new[]
            {
                "-X",
                "utf8",
                runnerScriptPath,
                "--images",
                string.Join("|", inputFilePaths ?? Array.Empty<string>()),
                "--groups",
                string.Join("|", languageCandidates ?? Array.Empty<string>()),
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

        internal string GetFailureMessageForExitCode(int exitCode, string standardError)
        {
            string stderr = EmptyToFallback(standardError, "");
            return exitCode switch
            {
                3 => "PaddleOCR 모듈을 찾지 못했습니다. py -m pip install paddlepaddle 및 py -m pip install \"paddleocr[all]\" 후 다시 실행해 주세요.",
                4 => EmptyToFallback(stderr, "PaddleOCR 실행 중 오류가 발생했습니다."),
                _ => EmptyToFallback(stderr, $"PaddleOCR 종료 코드: {exitCode}")
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

            string environmentPath = Environment.GetEnvironmentVariable("PADDLEOCR_PYTHON_PATH");
            if (!string.IsNullOrWhiteSpace(environmentPath))
            {
                candidates.Add(environmentPath.Trim());
            }

            candidates.Add(SettingsService.DefaultPaddleOcrPythonPath);
            candidates.Add("py");
            candidates.Add("python3");

            return candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private PaddleOcrCliBatchResult TryRecognizeInternal(
            string pythonExecutablePath,
            string runnerScriptPath,
            IReadOnlyList<string> inputFilePaths,
            IReadOnlyList<string> languageCandidates,
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

            foreach (string argument in BuildBatchArguments(runnerScriptPath, inputFilePaths, languageCandidates))
            {
                processStartInfo.ArgumentList.Add(argument);
            }

            try
            {
                using Process process = Process.Start(processStartInfo);
                if (process == null)
                {
                    return PaddleOcrCliBatchResult.CreateFailure(pythonExecutablePath, languageCandidates, "PaddleOCR Python 프로세스를 시작하지 못했습니다.");
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

                    return PaddleOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCandidates,
                        $"PaddleOCR 실행이 {timeoutMs}ms 안에 끝나지 않았습니다.");
                }

                if (process.ExitCode != 0)
                {
                    return PaddleOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCandidates,
                        GetFailureMessageForExitCode(process.ExitCode, standardError),
                        isModuleMissing: process.ExitCode == 3);
                }

                List<PaddleOcrCliImageResult> imageResults = ParseImageResults(standardOutput);
                if (imageResults.Count == 0)
                {
                    return PaddleOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCandidates,
                        EmptyToFallback(standardError, "PaddleOCR 결과를 읽지 못했습니다."));
                }

                int successCount = imageResults.Sum(image => image.GroupResults.Count(group => group.Success));
                if (successCount == 0)
                {
                    string firstErrorMessage = imageResults
                        .SelectMany(image => image.GroupResults)
                        .Select(group => group.ErrorMessage)
                        .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
                    return PaddleOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCandidates,
                        EmptyToFallback(firstErrorMessage, "PaddleOCR 결과가 모두 실패했습니다."));
                }

                return PaddleOcrCliBatchResult.CreateSuccess(pythonExecutablePath, languageCandidates, imageResults, standardError);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return PaddleOcrCliBatchResult.CreateFailure(
                    pythonExecutablePath,
                    languageCandidates,
                    "python 또는 py 실행 파일을 찾지 못했습니다.",
                    isPythonMissing: true);
            }
            catch (Exception ex)
            {
                return PaddleOcrCliBatchResult.CreateFailure(pythonExecutablePath, languageCandidates, ex.Message);
            }
        }

        private PaddleOcrCliBatchResult TryRecognizeWithResidentWorker(
            string pythonExecutablePath,
            string runnerScriptPath,
            IReadOnlyList<string> inputFilePaths,
            IReadOnlyList<string> languageCandidates,
            int timeoutMs)
        {
            try
            {
                PersistentPythonOcrWorker worker = GetOrCreateWorker(pythonExecutablePath, runnerScriptPath);
                string requestJson = JsonSerializer.Serialize(new PaddleOcrWorkerRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    ImagePaths = inputFilePaths?.ToList() ?? new List<string>(),
                    Groups = string.Join("|", languageCandidates ?? Array.Empty<string>()),
                    Gpu = false
                });

                PersistentPythonOcrWorkerResult workerResult = worker.SendRequestAsync(requestJson, timeoutMs).GetAwaiter().GetResult();
                if (!workerResult.Success)
                {
                    return PaddleOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCandidates,
                        EmptyToFallback(workerResult.ErrorMessage, "PaddleOCR 워커 요청에 실패했습니다."),
                        isPythonMissing: workerResult.IsPythonMissing,
                        standardError: workerResult.StandardError,
                        usedResidentWorker: true,
                        startedWorker: workerResult.StartedWorker,
                        restartedWorker: workerResult.RestartedWorker,
                        usedInitializationTimeout: workerResult.UsedInitializationTimeout,
                        timedOut: workerResult.TimedOut);
                }

                PaddleOcrWorkerResponse response = ParseWorkerResponse(workerResult.ResponseJson);
                if (response == null)
                {
                    return PaddleOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCandidates,
                        "PaddleOCR 워커 응답을 해석하지 못했습니다.",
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
                        : EmptyToFallback(response.Error, "PaddleOCR 워커가 오류를 반환했습니다.");

                    return PaddleOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCandidates,
                        errorMessage,
                        isModuleMissing: isModuleMissing,
                        standardError: workerResult.StandardError,
                        usedResidentWorker: true,
                        startedWorker: workerResult.StartedWorker,
                        restartedWorker: workerResult.RestartedWorker,
                        usedInitializationTimeout: workerResult.UsedInitializationTimeout);
                }

                List<PaddleOcrCliImageResult> imageResults = ConvertWorkerImagesToResults(response.Images);
                if (imageResults.Count == 0)
                {
                    return PaddleOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCandidates,
                        EmptyToFallback(workerResult.StandardError, "PaddleOCR 결과를 읽지 못했습니다."),
                        standardError: workerResult.StandardError,
                        usedResidentWorker: true,
                        startedWorker: workerResult.StartedWorker,
                        restartedWorker: workerResult.RestartedWorker,
                        usedInitializationTimeout: workerResult.UsedInitializationTimeout);
                }

                int successCount = imageResults.Sum(image => image.GroupResults.Count(group => group.Success));
                if (successCount == 0)
                {
                    string firstErrorMessage = imageResults
                        .SelectMany(image => image.GroupResults)
                        .Select(group => group.ErrorMessage)
                        .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
                    return PaddleOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCandidates,
                        EmptyToFallback(firstErrorMessage, "PaddleOCR 결과가 모두 실패했습니다."),
                        standardError: workerResult.StandardError,
                        usedResidentWorker: true,
                        startedWorker: workerResult.StartedWorker,
                        restartedWorker: workerResult.RestartedWorker,
                        usedInitializationTimeout: workerResult.UsedInitializationTimeout);
                }

                return PaddleOcrCliBatchResult.CreateSuccess(
                    pythonExecutablePath,
                    languageCandidates,
                    imageResults,
                    workerResult.StandardError,
                    standardError: workerResult.StandardError,
                    usedResidentWorker: true,
                    startedWorker: workerResult.StartedWorker,
                    restartedWorker: workerResult.RestartedWorker,
                    usedInitializationTimeout: workerResult.UsedInitializationTimeout);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                ReleaseWorkerLease(pythonExecutablePath);

                return PaddleOcrCliBatchResult.CreateFailure(
                    pythonExecutablePath,
                    languageCandidates,
                    "python 또는 py 실행 파일을 찾지 못했습니다.",
                    isPythonMissing: true);
            }
            catch (Exception ex)
            {
                return PaddleOcrCliBatchResult.CreateFailure(
                    pythonExecutablePath,
                    languageCandidates,
                    ex.Message);
            }
        }

        private static List<PaddleOcrCliImageResult> ParseImageResults(string json)
        {
            PaddleOcrRunnerResponse response = JsonSerializer.Deserialize<PaddleOcrRunnerResponse>(json ?? "", new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (response?.Images?.Count > 0)
            {
                return response.Images
                    .OrderBy(image => image?.Index ?? int.MaxValue)
                    .Select(image => new PaddleOcrCliImageResult(
                        image?.Index ?? -1,
                        (image?.Groups ?? new List<PaddleOcrRunnerGroup>())
                            .Select(group => new PaddleOcrCliGroupResult(
                                group?.LanguageCodes ?? "",
                                group?.Success ?? false,
                                (group?.Lines ?? new List<PaddleOcrRunnerLine>())
                                    .Where(line => !string.IsNullOrWhiteSpace(line?.Text))
                                    .Select(line => new OcrLine
                                    {
                                        Top = line.Top,
                                        Bottom = line.Bottom,
                                        Text = line.Text.Trim()
                                    })
                                    .ToList(),
                                group?.Error ?? ""))
                            .ToList()))
                    .ToList();
            }

            if (response?.Groups?.Count > 0)
            {
                return new List<PaddleOcrCliImageResult>
                {
                    new PaddleOcrCliImageResult(
                        0,
                        response.Groups
                            .Select(group => new PaddleOcrCliGroupResult(
                                group?.LanguageCodes ?? "",
                                group?.Success ?? false,
                                (group?.Lines ?? new List<PaddleOcrRunnerLine>())
                                    .Where(line => !string.IsNullOrWhiteSpace(line?.Text))
                                    .Select(line => new OcrLine
                                    {
                                        Top = line.Top,
                                        Bottom = line.Bottom,
                                        Text = line.Text.Trim()
                                    })
                                    .ToList(),
                                group?.Error ?? ""))
                            .ToList())
                };
            }

            return new List<PaddleOcrCliImageResult>();
        }

        internal static PaddleOcrWorkerResponse ParseWorkerResponse(string json)
        {
            return JsonSerializer.Deserialize<PaddleOcrWorkerResponse>(json ?? "", new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        internal static List<PaddleOcrCliImageResult> ConvertWorkerImagesToResults(IReadOnlyList<PaddleOcrWorkerImage> images)
        {
            return (images ?? Array.Empty<PaddleOcrWorkerImage>())
                .OrderBy(image => image?.Index ?? int.MaxValue)
                .Select(image => new PaddleOcrCliImageResult(
                    image?.Index ?? -1,
                    (image?.Groups ?? new List<PaddleOcrWorkerGroup>())
                        .Select(group => new PaddleOcrCliGroupResult(
                            group?.LanguageCodes ?? "",
                            group?.Success ?? false,
                            (group?.Lines ?? new List<PaddleOcrWorkerLine>())
                                .Where(line => !string.IsNullOrWhiteSpace(line?.Text))
                                .Select(line => new OcrLine
                                {
                                    Top = line.Top,
                                    Bottom = line.Bottom,
                                    Text = line.Text.Trim()
                                })
                                .ToList(),
                            group?.Error ?? ""))
                        .ToList()))
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
                if (!workerLeaseByPythonPath.TryGetValue(pythonExecutablePath, out PersistentPythonOcrWorkerLease workerLease))
                {
                    workerLease = PersistentPythonOcrWorkerRegistry.Acquire(EngineType, pythonExecutablePath, runnerScriptPath);
                    workerLeaseByPythonPath.Add(pythonExecutablePath, workerLease);
                }

                return workerLease.Worker;
            }
        }

        private void ReleaseWorkerLease(string pythonExecutablePath)
        {
            if (string.IsNullOrWhiteSpace(pythonExecutablePath))
            {
                return;
            }

            lock (workerSync)
            {
                if (!workerLeaseByPythonPath.TryGetValue(pythonExecutablePath, out PersistentPythonOcrWorkerLease workerLease))
                {
                    return;
                }

                workerLease.Dispose();
                workerLeaseByPythonPath.Remove(pythonExecutablePath);
            }
        }

        private sealed class PaddleOcrRunnerResponse
        {
            public List<PaddleOcrRunnerImage> Images { get; set; } = new List<PaddleOcrRunnerImage>();
            public List<PaddleOcrRunnerGroup> Groups { get; set; } = new List<PaddleOcrRunnerGroup>();
        }

        private sealed class PaddleOcrRunnerImage
        {
            public int Index { get; set; }
            public List<PaddleOcrRunnerGroup> Groups { get; set; } = new List<PaddleOcrRunnerGroup>();
        }

        private sealed class PaddleOcrRunnerGroup
        {
            public string LanguageCodes { get; set; } = "";
            public bool Success { get; set; }
            public string Error { get; set; } = "";
            public List<PaddleOcrRunnerLine> Lines { get; set; } = new List<PaddleOcrRunnerLine>();
        }

        private sealed class PaddleOcrRunnerLine
        {
            public double Top { get; set; }
            public double Bottom { get; set; }
            public string Text { get; set; } = "";
        }

        private sealed class PaddleOcrWorkerRequest
        {
            public string RequestId { get; set; } = "";
            public List<string> ImagePaths { get; set; } = new List<string>();
            public string Groups { get; set; } = "";
            public bool Gpu { get; set; }
        }

        internal sealed class PaddleOcrWorkerResponse
        {
            public string RequestId { get; set; } = "";
            public bool Ok { get; set; }
            public string Error { get; set; } = "";
            public string ErrorCode { get; set; } = "";
            public List<PaddleOcrWorkerImage> Images { get; set; } = new List<PaddleOcrWorkerImage>();
        }

        internal sealed class PaddleOcrWorkerImage
        {
            public int Index { get; set; }
            public List<PaddleOcrWorkerGroup> Groups { get; set; } = new List<PaddleOcrWorkerGroup>();
        }

        internal sealed class PaddleOcrWorkerGroup
        {
            public string LanguageCodes { get; set; } = "";
            public bool Success { get; set; }
            public string Error { get; set; } = "";
            public List<PaddleOcrWorkerLine> Lines { get; set; } = new List<PaddleOcrWorkerLine>();
        }

        internal sealed class PaddleOcrWorkerLine
        {
            public double Top { get; set; }
            public double Bottom { get; set; }
            public string Text { get; set; } = "";
        }
    }

    public sealed class PaddleOcrCliBatchResult
    {
        private PaddleOcrCliBatchResult(
            bool success,
            string pythonExecutablePath,
            IReadOnlyList<string> languageCandidates,
            IReadOnlyList<PaddleOcrCliImageResult> imageResults,
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
            LanguageCandidates = languageCandidates ?? Array.Empty<string>();
            ImageResults = imageResults ?? Array.Empty<PaddleOcrCliImageResult>();
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
        public IReadOnlyList<string> LanguageCandidates { get; }
        public IReadOnlyList<PaddleOcrCliImageResult> ImageResults { get; }
        public IReadOnlyList<PaddleOcrCliGroupResult> GroupResults => ImageResults.FirstOrDefault()?.GroupResults ?? Array.Empty<PaddleOcrCliGroupResult>();
        public string ErrorMessage { get; }
        public bool IsPythonMissing { get; }
        public bool IsModuleMissing { get; }
        public string StandardError { get; }
        public bool UsedResidentWorker { get; }
        public bool StartedWorker { get; }
        public bool RestartedWorker { get; }
        public bool UsedInitializationTimeout { get; }
        public bool TimedOut { get; }

        public static PaddleOcrCliBatchResult CreateSuccess(
            string pythonExecutablePath,
            IReadOnlyList<string> languageCandidates,
            IReadOnlyList<PaddleOcrCliImageResult> imageResults,
            string errorMessage = "",
            string standardError = "",
            bool usedResidentWorker = false,
            bool startedWorker = false,
            bool restartedWorker = false,
            bool usedInitializationTimeout = false)
        {
            return new PaddleOcrCliBatchResult(
                true,
                pythonExecutablePath,
                languageCandidates,
                imageResults,
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

        public static PaddleOcrCliBatchResult CreateFailure(
            string pythonExecutablePath,
            IReadOnlyList<string> languageCandidates,
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
            return new PaddleOcrCliBatchResult(
                false,
                pythonExecutablePath,
                languageCandidates,
                Array.Empty<PaddleOcrCliImageResult>(),
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

    public sealed class PaddleOcrCliImageResult
    {
        public PaddleOcrCliImageResult(int index, IReadOnlyList<PaddleOcrCliGroupResult> groupResults)
        {
            Index = index;
            GroupResults = groupResults ?? Array.Empty<PaddleOcrCliGroupResult>();
        }

        public int Index { get; }
        public IReadOnlyList<PaddleOcrCliGroupResult> GroupResults { get; }
    }

    public sealed class PaddleOcrCliGroupResult
    {
        public PaddleOcrCliGroupResult(string languageCodes, bool success, IReadOnlyList<OcrLine> lines, string errorMessage)
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
