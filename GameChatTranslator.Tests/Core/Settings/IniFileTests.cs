using System.IO;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests.Core.Settings;

public sealed class IniFileTests
{
    [Fact]
    public void SortSectionKeys_ReordersSettingsByPreferredGroupsAndKeepsUnknownKeys()
    {
        string tempDirectory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string iniPath = Path.Combine(tempDirectory, "config.ini");
            File.WriteAllText(
                iniPath,
                """
                [Settings]
                GeminiModel=gemini-2.5-flash
                Key_AutoTranslate=Ctrl+=
                UnknownCustomKey=custom
                GameLanguage=ko
                TargetLanguage=ja
                Key_OpenSettings=Ctrl+0
                MainOcrEngine=Tesseract
                Key_Translate=Ctrl+-

                [Other]
                Keep=1
                """);

            IniFile iniFile = new IniFile(iniPath);

            iniFile.SortSectionKeys("Settings", SettingsService.SettingsSectionKeyOrder);

            string[] lines = File.ReadAllLines(iniPath);

            int settingsIndex = Array.IndexOf(lines, "[Settings]");
            int otherIndex = Array.IndexOf(lines, "[Other]");

            Assert.True(settingsIndex >= 0);
            Assert.True(otherIndex > settingsIndex);

            string[] settingsLines = lines
                .Skip(settingsIndex + 1)
                .Take(otherIndex - settingsIndex - 2)
                .ToArray();

            Assert.Equal("GameLanguage=ko", settingsLines[0]);
            Assert.Equal("TargetLanguage=ja", settingsLines[1]);
            Assert.Equal("MainOcrEngine=Tesseract", settingsLines[2]);
            Assert.Equal("Key_OpenSettings=Ctrl+0", settingsLines[3]);
            Assert.Equal("Key_Translate=Ctrl+-", settingsLines[4]);
            Assert.Equal("Key_AutoTranslate=Ctrl+=", settingsLines[5]);
            Assert.Equal("GeminiModel=gemini-2.5-flash", settingsLines[6]);
            Assert.Equal("UnknownCustomKey=custom", settingsLines[7]);
            Assert.Contains("[Other]", lines);
            Assert.Contains("Keep=1", lines);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void RewriteManagedSettingsSections_MigratesLegacySettingsIntoFeatureSections()
    {
        string tempDirectory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string iniPath = Path.Combine(tempDirectory, "config.ini");
            File.WriteAllText(
                iniPath,
                """
                [Settings]
                GameLanguage=ko
                TargetLanguage=ja
                TranslationEngine=Google
                MainOcrEngine=Tesseract
                ScaleFactor=3
                Threshold=120
                Key_OpenSettings=Ctrl+0
                Key_Translate=Ctrl+-
                Key_AutoTranslate=Ctrl+=
                GeminiModel=gemini-2.5-flash
                UnknownCustomKey=custom

                [Presets]
                List=fast
                """);

            IniFile iniFile = new IniFile(iniPath);

            iniFile.RewriteManagedSettingsSections();

            string text = File.ReadAllText(iniPath);

            Assert.Contains("[Language]", text);
            Assert.Contains("GameLanguage=ko", text);
            Assert.Contains("TargetLanguage=ja", text);

            Assert.Contains("[Translation]", text);
            Assert.Contains("TranslationEngine=Google", text);
            Assert.Contains("MainOcrEngine=Tesseract", text);

            Assert.Contains("[OCR]", text);
            Assert.Contains("ScaleFactor=3", text);
            Assert.Contains("Threshold=120", text);

            Assert.Contains("[Hotkeys]", text);
            Assert.Contains("Key_OpenSettings=Ctrl+0", text);
            Assert.Contains("Key_Translate=Ctrl+-", text);
            Assert.Contains("Key_AutoTranslate=Ctrl+=", text);

            Assert.Contains("[Gemini]", text);
            Assert.Contains("GeminiModel=gemini-2.5-flash", text);

            Assert.Contains("[Settings]", text);
            Assert.Contains("UnknownCustomKey=custom", text);

            Assert.Contains("[Presets]", text);
            Assert.Contains("List=fast", text);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
