using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using ColorConverter = System.Windows.Media.ColorConverter;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace GameTranslator
{
    /// <summary>
    /// 캐릭터 목록 로드, 기본 설정 보정, Gemini 설정 읽기 같은 설정 관련 로직을 담당합니다.
    /// config.ini 값이 비어 있거나 구버전 구조일 때도 런타임 기본값이 안정적으로 적용되게 합니다.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 실행 폴더의 characters.txt를 읽어 번역 허용 캐릭터명 목록을 초기화합니다.
        /// 줄 앞이 #인 항목은 주석으로 취급하고, 나머지 이름은 OCR 결과 검증에 사용합니다.
        /// </summary>
        private void LoadCharacters()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "characters.txt");
                if (File.Exists(path))
                {
                    var lines = File.ReadAllLines(path);
                    foreach (var line in lines)
                    {
                        string name = line.Trim();
                        if (!string.IsNullOrEmpty(name) && !name.StartsWith("#"))
                        {
                            characterNames.Add(name);
                        }
                    }
                    AppendLog($"캐릭터 {characterNames.Count}명 로드 완료.");
                }
            }
            catch (Exception ex) { AppendLog($"파일 로드 중 오류: {ex.Message}"); }
        }

        /// <summary>
        /// config.ini에 필수 설정 키가 없을 때 기본값을 기록합니다.
        /// 기존 사용자 설정을 덮어쓰지 않도록 비어 있는 키만 보정합니다.
        /// </summary>
        private void EnsureDefaultSettings()
        {
            if (string.IsNullOrWhiteSpace(ini.Read("GeminiKey")) && string.IsNullOrWhiteSpace(ini.Read("GeminiKey", "GeminiKey")))
            {
                ini.Write("GeminiKey", "");
            }

            if (string.IsNullOrWhiteSpace(ini.Read("GeminiModel")))
            {
                ini.Write("GeminiModel", SettingsService.DefaultGeminiModel);
            }

            if (string.IsNullOrWhiteSpace(ini.Read("LocalLlmEndpoint")))
            {
                ini.Write("LocalLlmEndpoint", SettingsService.DefaultLocalLlmEndpoint);
            }

            if (string.IsNullOrWhiteSpace(ini.Read("LocalLlmModel")))
            {
                ini.Write("LocalLlmModel", SettingsService.DefaultLocalLlmModel);
            }

            if (string.IsNullOrWhiteSpace(ini.Read("LocalLlmTimeoutSeconds")))
            {
                ini.Write("LocalLlmTimeoutSeconds", SettingsService.DefaultLocalLlmTimeoutSeconds.ToString());
            }

            if (string.IsNullOrWhiteSpace(ini.Read("LocalLlmMaxTokens")))
            {
                ini.Write("LocalLlmMaxTokens", SettingsService.DefaultLocalLlmMaxTokens.ToString());
            }

            if (string.IsNullOrWhiteSpace(ini.Read("SaveDebugImages")))
            {
                ini.Write("SaveDebugImages", "false");
            }

            if (string.IsNullOrWhiteSpace(ini.Read("CheckUpdatesOnStartup")))
            {
                ini.Write("CheckUpdatesOnStartup", "true");
            }

            if (string.IsNullOrWhiteSpace(ini.Read("Threshold")))
            {
                ini.Write("Threshold", SettingsValueNormalizer.DefaultThreshold.ToString());
            }

            if (string.IsNullOrWhiteSpace(ini.Read("AutoTranslateInterval")))
            {
                ini.Write("AutoTranslateInterval", SettingsValueNormalizer.DefaultAutoTranslateInterval.ToString());
            }

            if (string.IsNullOrWhiteSpace(ini.Read("ScaleFactor")))
            {
                ini.Write("ScaleFactor", SettingsValueNormalizer.DefaultScaleFactor.ToString());
            }

            if (string.IsNullOrWhiteSpace(ini.Read("Key_CopyResult")))
            {
                ini.Write("Key_CopyResult", SettingsService.DefaultKeyCopyResult);
            }

            if (string.IsNullOrWhiteSpace(ini.Read("Key_LogViewer")))
            {
                ini.Write("Key_LogViewer", SettingsService.DefaultKeyLogViewer);
            }

            if (string.IsNullOrWhiteSpace(ini.Read("Key_OcrDiagnostic")))
            {
                ini.Write("Key_OcrDiagnostic", SettingsService.DefaultKeyOcrDiagnostic);
            }

            if (string.IsNullOrWhiteSpace(ini.Read("Key_HotkeyGuideToggle")))
            {
                ini.Write("Key_HotkeyGuideToggle", SettingsService.DefaultKeyHotkeyGuideToggle);
            }

            if (string.IsNullOrWhiteSpace(ini.Read("ResultDisplayMode")))
            {
                ini.Write("ResultDisplayMode", SettingsService.DefaultResultDisplayMode);
            }

            if (string.IsNullOrWhiteSpace(ini.Read("ResultHistoryLimit")))
            {
                ini.Write("ResultHistoryLimit", SettingsValueNormalizer.DefaultResultHistoryLimit.ToString());
            }
        }

        /// <summary>
        /// Gemini API 키를 현재 [Settings] 섹션에서 읽고, 구버전 [GeminiKey] 섹션에 있던 키는 자동 마이그레이션합니다.
        /// 반환값은 공백이 제거된 API 키이며, 설정되어 있지 않으면 빈 문자열입니다.
        /// </summary>
        private string ReadGeminiKey()
        {
            GeminiKeySelection selectedKey = settingsService.SelectGeminiKey(
                ini.Read("GeminiKey"),
                ini.Read("GeminiKey", "GeminiKey"));
            if (selectedKey.ShouldMigrateLegacyKey)
            {
                ini.Write("GeminiKey", selectedKey.Key);
                AppendLog("기존 [GeminiKey] 섹션의 API 키를 [Settings] 섹션으로 이전했습니다.");
            }

            return selectedKey.Key;
        }

        /// <summary>
        /// Gemini 호출에 사용할 모델명을 읽습니다.
        /// config.ini의 GeminiModel 값이 비어 있으면 DefaultGeminiModel을 반환합니다.
        /// </summary>
        private string ReadGeminiModel()
        {
            return settingsService.NormalizeGeminiModel(ini.Read("GeminiModel"));
        }

        /// <summary>
        /// Local LLM 호출에 사용할 OpenAI 호환 chat completions endpoint를 읽습니다.
        /// 비어 있으면 LM Studio 기본 주소를 반환합니다.
        /// </summary>
        private string ReadLocalLlmEndpoint()
        {
            return settingsService.NormalizeLocalLlmEndpoint(ini.Read("LocalLlmEndpoint"));
        }

        /// <summary>
        /// Local LLM 호출에 사용할 모델 ID를 읽습니다.
        /// 비어 있으면 현재 검증한 Qwen 기본 모델명을 반환합니다.
        /// </summary>
        private string ReadLocalLlmModel()
        {
            return settingsService.NormalizeLocalLlmModel(ini.Read("LocalLlmModel"));
        }

        /// <summary>
        /// Local LLM 요청 타임아웃을 초 단위로 읽고 허용 범위로 보정합니다.
        /// </summary>
        private int ReadLocalLlmTimeoutSeconds()
        {
            return settingsService.NormalizeLocalLlmTimeoutSeconds(ini.Read("LocalLlmTimeoutSeconds"));
        }

        /// <summary>
        /// Local LLM 응답 최대 토큰 수를 읽고 허용 범위로 보정합니다.
        /// </summary>
        private int ReadLocalLlmMaxTokens()
        {
            return settingsService.NormalizeLocalLlmMaxTokens(ini.Read("LocalLlmMaxTokens"));
        }

        /// <summary>
        /// 디버그 캡처 이미지 저장 여부를 config.ini에서 읽어 bool로 변환합니다.
        /// true/1/yes/y 값을 켜짐으로 인정하고, 그 외 값은 꺼짐으로 처리합니다.
        /// </summary>
        private bool ShouldSaveDebugImages()
        {
            return settingsService.IsEnabled(ini.Read("SaveDebugImages"));
        }
    }
}
