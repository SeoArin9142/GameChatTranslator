using System;
using System.IO;

namespace GameTranslator
{
    /// <summary>
    /// 설치/실행 폴더와 사용자 데이터 폴더 경로를 분리해 관리합니다.
    /// 자동 업데이트 도입 시 설치 폴더가 교체되어도 config.ini, logs, Captures 같은 사용자 데이터가 유지되도록 합니다.
    /// </summary>
    public sealed class AppDataPaths
    {
        public const string AppFolderName = "GameChatTranslator";

        public AppDataPaths(string installDirectory = null, string localAppDataDirectory = null)
        {
            InstallDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(installDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : installDirectory);

            string appDataRoot = string.IsNullOrWhiteSpace(localAppDataDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : localAppDataDirectory;

            if (string.IsNullOrWhiteSpace(appDataRoot))
            {
                appDataRoot = InstallDirectory;
            }

            RootDirectory = Path.Combine(Path.GetFullPath(appDataRoot), AppFolderName);
            LogsDirectory = Path.Combine(RootDirectory, "logs");
            CapturesDirectory = Path.Combine(RootDirectory, "Captures");
            OcrDiagnosticsDirectory = Path.Combine(RootDirectory, "OcrDiagnostics");
            ConfigFilePath = Path.Combine(RootDirectory, "config.ini");
            UserCharactersFilePath = Path.Combine(RootDirectory, "characters.txt");
            DistributionCharactersFilePath = Path.Combine(InstallDirectory, "characters.txt");
        }

        public string InstallDirectory { get; }
        public string RootDirectory { get; }
        public string LogsDirectory { get; }
        public string CapturesDirectory { get; }
        public string OcrDiagnosticsDirectory { get; }
        public string ConfigFilePath { get; }
        public string UserCharactersFilePath { get; }
        public string DistributionCharactersFilePath { get; }

        /// <summary>
        /// 사용자가 수정한 characters.txt가 사용자 데이터 폴더에 있으면 그 파일을 우선 사용합니다.
        /// 아직 마이그레이션 전이면 배포 폴더의 기본 characters.txt를 fallback으로 사용합니다.
        /// </summary>
        public string GetCharactersFilePath()
        {
            return File.Exists(UserCharactersFilePath)
                ? UserCharactersFilePath
                : DistributionCharactersFilePath;
        }

        /// <summary>
        /// 앱에서 쓰는 사용자 데이터 하위 폴더를 생성합니다.
        /// </summary>
        public void EnsureDirectories()
        {
            Directory.CreateDirectory(RootDirectory);
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(CapturesDirectory);
            Directory.CreateDirectory(OcrDiagnosticsDirectory);
        }

        /// <summary>
        /// 기존 ZIP 배포 구조에서 실행 폴더에 있던 사용자 파일을 LocalAppData로 복사합니다.
        /// 원본은 삭제하지 않아 권한 문제나 사용자의 수동 백업 흐름을 깨지 않으며, 새 위치에 파일이 있으면 덮어쓰지 않습니다.
        /// </summary>
        public AppDataMigrationSummary MigrateLegacyFiles()
        {
            EnsureDirectories();

            var summary = new AppDataMigrationSummary();
            summary.ConfigCopied = CopyFileIfMissing(Path.Combine(InstallDirectory, "config.ini"), ConfigFilePath);
            summary.CharactersCopied = CopyFileIfMissing(DistributionCharactersFilePath, UserCharactersFilePath);
            summary.LogFilesCopied = CopyDirectoryFilesIfMissing(Path.Combine(InstallDirectory, "logs"), LogsDirectory);
            summary.CaptureFilesCopied = CopyDirectoryFilesIfMissing(Path.Combine(InstallDirectory, "Captures"), CapturesDirectory);
            return summary;
        }

        private static bool CopyFileIfMissing(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath) || File.Exists(destinationPath))
            {
                return false;
            }

            string destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(sourcePath, destinationPath, false);
            return true;
        }

        private static int CopyDirectoryFilesIfMissing(string sourceDirectory, string destinationDirectory)
        {
            if (!Directory.Exists(sourceDirectory))
            {
                return 0;
            }

            Directory.CreateDirectory(destinationDirectory);

            int copied = 0;
            foreach (string sourcePath in Directory.GetFiles(sourceDirectory))
            {
                string destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
                if (File.Exists(destinationPath))
                {
                    continue;
                }

                File.Copy(sourcePath, destinationPath, false);
                copied++;
            }

            return copied;
        }
    }

    /// <summary>
    /// 실행 폴더에서 사용자 데이터 폴더로 복사된 항목 수를 시작 로그에 남기기 위한 요약 모델입니다.
    /// </summary>
    public sealed class AppDataMigrationSummary
    {
        public bool ConfigCopied { get; set; }
        public bool CharactersCopied { get; set; }
        public int LogFilesCopied { get; set; }
        public int CaptureFilesCopied { get; set; }

        public bool HasChanges => ConfigCopied || CharactersCopied || LogFilesCopied > 0 || CaptureFilesCopied > 0;
    }
}
