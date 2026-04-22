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
    public sealed class PaddleOcrCliAdapter
    {
        private const string RunnerFileName = "paddleocr_runner.py";

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
            if (bitmap == null)
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

            string inputFilePath = Path.Combine(inputDirectory, $"ocr_{Guid.NewGuid():N}.png");
            bitmap.Save(inputFilePath, ImageFormat.Png);

            try
            {
                foreach (string pythonPath in GetPythonCandidates(configuredPythonPath))
                {
                    PaddleOcrCliBatchResult runResult = TryRecognizeInternal(
                        pythonPath,
                        runnerScriptPath,
                        inputFilePath,
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

        internal string GetRunnerScriptPath()
        {
            return Path.Combine(AppContext.BaseDirectory, RunnerFileName);
        }

        internal IReadOnlyList<string> BuildArguments(
            string runnerScriptPath,
            string inputFilePath,
            IReadOnlyList<string> languageCandidates)
        {
            return new[]
            {
                "-X",
                "utf8",
                runnerScriptPath,
                "--image",
                inputFilePath,
                "--groups",
                string.Join("|", languageCandidates ?? Array.Empty<string>()),
                "--gpu",
                "false"
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
            string inputFilePath,
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

            foreach (string argument in BuildArguments(runnerScriptPath, inputFilePath, languageCandidates))
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

                List<PaddleOcrCliGroupResult> groupResults = ParseGroupResults(standardOutput);
                if (groupResults.Count == 0)
                {
                    return PaddleOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCandidates,
                        EmptyToFallback(standardError, "PaddleOCR 결과를 읽지 못했습니다."));
                }

                int successCount = groupResults.Count(group => group.Success);
                if (successCount == 0)
                {
                    string firstErrorMessage = groupResults
                        .Select(group => group.ErrorMessage)
                        .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
                    return PaddleOcrCliBatchResult.CreateFailure(
                        pythonExecutablePath,
                        languageCandidates,
                        EmptyToFallback(firstErrorMessage, "PaddleOCR 결과가 모두 실패했습니다."));
                }

                return PaddleOcrCliBatchResult.CreateSuccess(pythonExecutablePath, languageCandidates, groupResults, standardError);
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

        private static List<PaddleOcrCliGroupResult> ParseGroupResults(string json)
        {
            PaddleOcrRunnerResponse response = JsonSerializer.Deserialize<PaddleOcrRunnerResponse>(json ?? "", new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return (response?.Groups ?? new List<PaddleOcrRunnerGroup>())
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
                .ToList();
        }

        private static string EmptyToFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private sealed class PaddleOcrRunnerResponse
        {
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
    }

    public sealed class PaddleOcrCliBatchResult
    {
        private PaddleOcrCliBatchResult(
            bool success,
            string pythonExecutablePath,
            IReadOnlyList<string> languageCandidates,
            IReadOnlyList<PaddleOcrCliGroupResult> groupResults,
            string errorMessage,
            bool isPythonMissing,
            bool isModuleMissing)
        {
            Success = success;
            PythonExecutablePath = pythonExecutablePath ?? "";
            LanguageCandidates = languageCandidates ?? Array.Empty<string>();
            GroupResults = groupResults ?? Array.Empty<PaddleOcrCliGroupResult>();
            ErrorMessage = errorMessage ?? "";
            IsPythonMissing = isPythonMissing;
            IsModuleMissing = isModuleMissing;
        }

        public bool Success { get; }
        public string PythonExecutablePath { get; }
        public IReadOnlyList<string> LanguageCandidates { get; }
        public IReadOnlyList<PaddleOcrCliGroupResult> GroupResults { get; }
        public string ErrorMessage { get; }
        public bool IsPythonMissing { get; }
        public bool IsModuleMissing { get; }

        public static PaddleOcrCliBatchResult CreateSuccess(
            string pythonExecutablePath,
            IReadOnlyList<string> languageCandidates,
            IReadOnlyList<PaddleOcrCliGroupResult> groupResults,
            string errorMessage = "")
        {
            return new PaddleOcrCliBatchResult(true, pythonExecutablePath, languageCandidates, groupResults, errorMessage, false, false);
        }

        public static PaddleOcrCliBatchResult CreateFailure(
            string pythonExecutablePath,
            IReadOnlyList<string> languageCandidates,
            string errorMessage,
            bool isPythonMissing = false,
            bool isModuleMissing = false)
        {
            return new PaddleOcrCliBatchResult(false, pythonExecutablePath, languageCandidates, Array.Empty<PaddleOcrCliGroupResult>(), errorMessage, isPythonMissing, isModuleMissing);
        }
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
