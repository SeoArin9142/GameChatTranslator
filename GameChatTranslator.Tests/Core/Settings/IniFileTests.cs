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
}
