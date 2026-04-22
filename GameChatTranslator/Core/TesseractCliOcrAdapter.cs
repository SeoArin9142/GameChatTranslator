using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;

namespace GameTranslator
{
    /// <summary>
    /// 실험 브랜치에서 Tesseract CLI를 외부 OCR 어댑터로 호출합니다.
    /// 설치형 통합 전에 Win OCR 결과와 같은 진단 화면에서 비교할 수 있도록, 실행 파일 탐색/언어 코드 조합/텍스트 파싱만 담당합니다.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class TesseractCliOcrAdapter
    {
        private static readonly Dictionary<string, string> AppLanguageToTesseractMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ko"] = "kor",
            ["ko-KR"] = "kor",
            ["en"] = "eng",
            ["en-US"] = "eng",
            ["ja"] = "jpn",
            ["ja-JP"] = "jpn",
            ["zh-Hans-CN"] = "chi_sim",
            ["zh-CN"] = "chi_sim",
            ["ru"] = "rus",
            ["ru-RU"] = "rus"
        };

        private static readonly string[] CommonWindowsExecutablePaths =
        {
            @"C:\Program Files\Tesseract-OCR\tesseract.exe",
            @"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe"
        };

        public string BuildLanguageCodes(string configuredValue, string gameLanguage)
        {
            if (!string.IsNullOrWhiteSpace(configuredValue) &&
                !string.Equals(configuredValue.Trim(), SettingsService.DefaultTesseractLanguageCodes, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeLanguageCodes(configuredValue);
            }

            var codes = new List<string>();
            string mappedGameLanguage = MapAppLanguageTagToTesseract(gameLanguage);
            if (!string.IsNullOrWhiteSpace(mappedGameLanguage))
            {
                codes.Add(mappedGameLanguage);
            }

            codes.Add("eng");
            codes.Add("kor");
            codes.Add("jpn");
            codes.Add("chi_sim");

            return string.Join("+", codes.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        public string NormalizeLanguageCodes(string rawValue)
        {
            IEnumerable<string> tokens = (rawValue ?? "")
                .Split(new[] { '+', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => MapAppLanguageTagToTesseract(token.Trim()))
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            string normalized = string.Join("+", tokens);
            return string.IsNullOrWhiteSpace(normalized) ? SettingsService.DefaultTesseractLanguageCodes : normalized;
        }

        public string MapAppLanguageTagToTesseract(string languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                return "";
            }

            string normalized = languageTag.Trim();
            return AppLanguageToTesseractMap.TryGetValue(normalized, out string mapped)
                ? mapped
                : normalized;
        }

        public IReadOnlyList<string> ParseOutputLines(string output)
        {
            return (output ?? "")
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }

        public TesseractCliOcrResult Recognize(Bitmap bitmap, string configuredExecutablePath, string configuredLanguageCodes, int timeoutMs = 15000)
        {
            if (bitmap == null)
            {
                return TesseractCliOcrResult.CreateFailure("", "", "OCR 입력 이미지가 없습니다.");
            }

            string languageCodes = BuildLanguageCodes(configuredLanguageCodes, "");
            string inputDirectory = Path.Combine(Path.GetTempPath(), "GameChatTranslator", "Tesseract");
            Directory.CreateDirectory(inputDirectory);

            string inputFilePath = Path.Combine(inputDirectory, $"ocr_{Guid.NewGuid():N}.png");
            bitmap.Save(inputFilePath, ImageFormat.Png);

            try
            {
                foreach (string executablePath in GetExecutableCandidates(configuredExecutablePath))
                {
                    TesseractCliOcrResult runResult = TryRecognizeInternal(executablePath, languageCodes, inputFilePath, timeoutMs);
                    if (runResult.Success || !runResult.IsExecutableMissing)
                    {
                        return runResult;
                    }
                }

                return TesseractCliOcrResult.CreateFailure(
                    SettingsService.DefaultTesseractExecutablePath,
                    languageCodes,
                    "tesseract.exe를 찾지 못했습니다. PATH 또는 config.ini의 TesseractExePath를 확인해 주세요.",
                    isExecutableMissing: true);
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

        private IEnumerable<string> GetExecutableCandidates(string configuredExecutablePath)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(configuredExecutablePath))
            {
                candidates.Add(configuredExecutablePath.Trim());
            }

            string environmentPath = Environment.GetEnvironmentVariable("TESSERACT_PATH");
            if (!string.IsNullOrWhiteSpace(environmentPath))
            {
                candidates.Add(environmentPath.Trim());
            }

            candidates.AddRange(CommonWindowsExecutablePaths);
            candidates.Add(SettingsService.DefaultTesseractExecutablePath);

            return candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private TesseractCliOcrResult TryRecognizeInternal(string executablePath, string languageCodes, string inputFilePath, int timeoutMs)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            processStartInfo.ArgumentList.Add(inputFilePath);
            processStartInfo.ArgumentList.Add("stdout");
            processStartInfo.ArgumentList.Add("-l");
            processStartInfo.ArgumentList.Add(languageCodes);
            processStartInfo.ArgumentList.Add("--psm");
            processStartInfo.ArgumentList.Add("6");
            processStartInfo.ArgumentList.Add("--oem");
            processStartInfo.ArgumentList.Add("3");

            try
            {
                using Process process = Process.Start(processStartInfo);
                if (process == null)
                {
                    return TesseractCliOcrResult.CreateFailure(executablePath, languageCodes, "Tesseract 프로세스를 시작하지 못했습니다.");
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

                    return TesseractCliOcrResult.CreateFailure(executablePath, languageCodes, $"Tesseract 실행이 {timeoutMs}ms 안에 끝나지 않았습니다.");
                }

                List<string> lines = ParseOutputLines(standardOutput).ToList();
                if (process.ExitCode != 0 && lines.Count == 0)
                {
                    return TesseractCliOcrResult.CreateFailure(executablePath, languageCodes, EmptyToFallback(standardError, $"Tesseract 종료 코드: {process.ExitCode}"));
                }

                return TesseractCliOcrResult.CreateSuccess(executablePath, languageCodes, lines, standardError);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return TesseractCliOcrResult.CreateFailure(executablePath, languageCodes, "실행 파일을 찾지 못했습니다.", isExecutableMissing: true);
            }
            catch (Exception ex)
            {
                return TesseractCliOcrResult.CreateFailure(executablePath, languageCodes, ex.Message);
            }
        }

        private static string EmptyToFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }

    public sealed class TesseractCliOcrResult
    {
        private TesseractCliOcrResult(
            bool success,
            string executablePath,
            string languageCodes,
            IReadOnlyList<string> lines,
            string errorMessage,
            string standardError,
            bool isExecutableMissing)
        {
            Success = success;
            ExecutablePath = executablePath ?? "";
            LanguageCodes = languageCodes ?? "";
            Lines = lines ?? Array.Empty<string>();
            ErrorMessage = errorMessage ?? "";
            StandardError = standardError ?? "";
            IsExecutableMissing = isExecutableMissing;
        }

        public bool Success { get; }
        public string ExecutablePath { get; }
        public string LanguageCodes { get; }
        public IReadOnlyList<string> Lines { get; }
        public string ErrorMessage { get; }
        public string StandardError { get; }
        public bool IsExecutableMissing { get; }

        public static TesseractCliOcrResult CreateSuccess(string executablePath, string languageCodes, IReadOnlyList<string> lines, string standardError)
        {
            return new TesseractCliOcrResult(true, executablePath, languageCodes, lines, "", standardError, false);
        }

        public static TesseractCliOcrResult CreateFailure(string executablePath, string languageCodes, string errorMessage, bool isExecutableMissing = false)
        {
            return new TesseractCliOcrResult(false, executablePath, languageCodes, Array.Empty<string>(), errorMessage, "", isExecutableMissing);
        }
    }
}
