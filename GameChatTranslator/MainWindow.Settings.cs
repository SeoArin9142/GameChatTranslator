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
    public partial class MainWindow
    {
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
        private void EnsureDefaultSettings()
        {
            if (string.IsNullOrWhiteSpace(ini.Read("GeminiKey")) && string.IsNullOrWhiteSpace(ini.Read("GeminiKey", "GeminiKey")))
            {
                ini.Write("GeminiKey", "");
            }

            if (string.IsNullOrWhiteSpace(ini.Read("GeminiModel")))
            {
                ini.Write("GeminiModel", DefaultGeminiModel);
            }

            if (string.IsNullOrWhiteSpace(ini.Read("SaveDebugImages")))
            {
                ini.Write("SaveDebugImages", "false");
            }

            if (string.IsNullOrWhiteSpace(ini.Read("CheckUpdatesOnStartup")))
            {
                ini.Write("CheckUpdatesOnStartup", "true");
            }

            if (string.IsNullOrWhiteSpace(ini.Read("Key_CopyResult")))
            {
                ini.Write("Key_CopyResult", "Ctrl+6");
            }
        }
        private string ReadGeminiKey()
        {
            string settingsKey = ini.Read("GeminiKey");
            if (!string.IsNullOrWhiteSpace(settingsKey))
            {
                return settingsKey.Trim();
            }

            string legacySectionKey = ini.Read("GeminiKey", "GeminiKey");
            if (!string.IsNullOrWhiteSpace(legacySectionKey))
            {
                string trimmedKey = legacySectionKey.Trim();
                ini.Write("GeminiKey", trimmedKey);
                AppendLog("기존 [GeminiKey] 섹션의 API 키를 [Settings] 섹션으로 이전했습니다.");
                return trimmedKey;
            }

            return "";
        }
        private string ReadGeminiModel()
        {
            string modelName = ini.Read("GeminiModel");
            return string.IsNullOrWhiteSpace(modelName) ? DefaultGeminiModel : modelName.Trim();
        }

        private bool ShouldSaveDebugImages()
        {
            string value = ini.Read("SaveDebugImages") ?? "false";
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("y", StringComparison.OrdinalIgnoreCase);
        }
    }
}
