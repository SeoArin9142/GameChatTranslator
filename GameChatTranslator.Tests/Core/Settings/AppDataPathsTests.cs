using System;
using System.IO;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class AppDataPathsTests
    {
        [Fact]
        public void Constructor_BuildsExpectedUserDataPaths()
        {
            string installDirectory = Path.Combine(Path.GetTempPath(), "gct-install");
            string localAppDataRoot = Path.Combine(Path.GetTempPath(), "gct-localappdata");

            var paths = new AppDataPaths(installDirectory, localAppDataRoot);

            Assert.Equal(Path.Combine(localAppDataRoot, AppDataPaths.AppFolderName), paths.RootDirectory);
            Assert.Equal(Path.Combine(localAppDataRoot, AppDataPaths.AppFolderName, "config.ini"), paths.ConfigFilePath);
            Assert.Equal(Path.Combine(localAppDataRoot, AppDataPaths.AppFolderName, "logs"), paths.LogsDirectory);
            Assert.Equal(Path.Combine(localAppDataRoot, AppDataPaths.AppFolderName, "Captures"), paths.CapturesDirectory);
            Assert.Equal(Path.Combine(localAppDataRoot, AppDataPaths.AppFolderName, "OcrDiagnostics"), paths.OcrDiagnosticsDirectory);
        }

        [Fact]
        public void GetCharactersFilePath_PrefersUserDataCopyWhenPresent()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string installDirectory = Path.Combine(tempRoot, "install");
                string localAppDataRoot = Path.Combine(tempRoot, "local");
                Directory.CreateDirectory(installDirectory);

                var paths = new AppDataPaths(installDirectory, localAppDataRoot);
                paths.EnsureDirectories();

                File.WriteAllText(paths.DistributionCharactersFilePath, "base");
                File.WriteAllText(paths.UserCharactersFilePath, "user");

                Assert.Equal(paths.UserCharactersFilePath, paths.GetCharactersFilePath());
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Fact]
        public void MigrateLegacyFiles_CopiesConfigCharactersLogsAndCapturesWithoutDeletingSource()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string installDirectory = Path.Combine(tempRoot, "install");
                string localAppDataRoot = Path.Combine(tempRoot, "local");
                Directory.CreateDirectory(installDirectory);
                Directory.CreateDirectory(Path.Combine(installDirectory, "logs"));
                Directory.CreateDirectory(Path.Combine(installDirectory, "Captures"));

                File.WriteAllText(Path.Combine(installDirectory, "config.ini"), "[Settings]\nGameLanguage=ja");
                File.WriteAllText(Path.Combine(installDirectory, "characters.txt"), "미셸");
                File.WriteAllText(Path.Combine(installDirectory, "logs", "log_1.txt"), "sample log");
                File.WriteAllText(Path.Combine(installDirectory, "Captures", "capture_1.png"), "png");

                var paths = new AppDataPaths(installDirectory, localAppDataRoot);

                AppDataMigrationSummary summary = paths.MigrateLegacyFiles();

                Assert.True(summary.ConfigCopied);
                Assert.True(summary.CharactersCopied);
                Assert.Equal(1, summary.LogFilesCopied);
                Assert.Equal(1, summary.CaptureFilesCopied);

                Assert.True(File.Exists(paths.ConfigFilePath));
                Assert.True(File.Exists(paths.UserCharactersFilePath));
                Assert.True(File.Exists(Path.Combine(paths.LogsDirectory, "log_1.txt")));
                Assert.True(File.Exists(Path.Combine(paths.CapturesDirectory, "capture_1.png")));

                Assert.True(File.Exists(Path.Combine(installDirectory, "config.ini")));
                Assert.True(File.Exists(Path.Combine(installDirectory, "characters.txt")));
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Fact]
        public void MigrateLegacyFiles_DoesNotOverwriteExistingUserFiles()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string installDirectory = Path.Combine(tempRoot, "install");
                string localAppDataRoot = Path.Combine(tempRoot, "local");
                Directory.CreateDirectory(installDirectory);
                File.WriteAllText(Path.Combine(installDirectory, "config.ini"), "legacy");

                var paths = new AppDataPaths(installDirectory, localAppDataRoot);
                paths.EnsureDirectories();
                File.WriteAllText(paths.ConfigFilePath, "current");

                AppDataMigrationSummary summary = paths.MigrateLegacyFiles();

                Assert.False(summary.ConfigCopied);
                Assert.Equal("current", File.ReadAllText(paths.ConfigFilePath));
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Fact]
        public void Constructor_UsesInstallDirectoryWhenPortableConfigExists()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string installDirectory = Path.Combine(tempRoot, "install");
                Directory.CreateDirectory(installDirectory);
                File.WriteAllText(Path.Combine(installDirectory, "config.ini"), "[Language]\nGameLanguage=ko");

                var paths = new AppDataPaths(installDirectory);

                Assert.True(paths.IsPortableMode);
                Assert.Equal(installDirectory, paths.RootDirectory);
                Assert.Equal(Path.Combine(installDirectory, "config.ini"), paths.ConfigFilePath);
                Assert.Equal(Path.Combine(installDirectory, "logs"), paths.LogsDirectory);
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        private static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "gct-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
