using System;
using System.IO;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace GameTranslator
{
    /// <summary>
    /// 환경설정창의 config.ini 내보내기/가져오기 기능을 담당합니다.
    /// 프리셋은 config.ini 안에 저장되므로 별도 파일 없이 함께 백업/복원됩니다.
    /// </summary>
    public partial class OptionSelector
    {
        /// <summary>
        /// [설정 내보내기] 버튼 클릭 시 현재 config.ini를 사용자가 선택한 위치에 복사합니다.
        /// 내보내기 대상은 저장된 설정 파일이므로, UI에서 수정만 하고 저장하지 않은 값은 포함되지 않습니다.
        /// </summary>
        private void BtnExportSettings_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (!File.Exists(_ini.Path))
            {
                MessageBox.Show("내보낼 config.ini 파일이 아직 생성되지 않았습니다.", "설정 내보내기", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "GameChatTranslator 설정 내보내기",
                Filter = "INI 설정 파일 (*.ini)|*.ini|모든 파일 (*.*)|*.*",
                FileName = $"GameChatTranslator_config_{DateTime.Now:yyyyMMdd_HHmmss}.ini",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                File.Copy(_ini.Path, dialog.FileName, true);
                MessageBox.Show($"설정을 내보냈습니다.\n{dialog.FileName}", "설정 내보내기", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 내보내기 실패:\n{ex.Message}", "설정 내보내기", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// [설정 가져오기] 버튼 클릭 시 선택한 ini 파일을 현재 config.ini 위치로 복사하고 UI를 다시 읽습니다.
        /// 덮어쓰기 전 기존 config.ini는 config.backup_yyyyMMdd_HHmmss.ini 이름으로 실행 폴더에 보관합니다.
        /// </summary>
        private void BtnImportSettings_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "GameChatTranslator 설정 가져오기",
                Filter = "INI 설정 파일 (*.ini)|*.ini|모든 파일 (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true) return;

            System.Windows.MessageBoxResult confirm = MessageBox.Show(
                "선택한 설정 파일로 현재 config.ini를 덮어씁니다.\n기존 설정은 자동 백업됩니다.\n계속할까요?",
                "설정 가져오기",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            try
            {
                string backupPath = CreateConfigBackupIfExists();
                File.Copy(dialog.FileName, _ini.Path, true);

                LoadCurrentSettings();
                LoadPresetList();
                RefreshOcrLanguageStatus();

                string message = "설정을 가져왔습니다.\n환경설정창 UI를 가져온 값으로 갱신했습니다.";
                if (!string.IsNullOrWhiteSpace(backupPath))
                {
                    message += $"\n\n기존 설정 백업:\n{backupPath}";
                }

                MessageBox.Show(message, "설정 가져오기", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 가져오기 실패:\n{ex.Message}", "설정 가져오기", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 현재 config.ini가 있으면 실행 폴더에 백업 사본을 만들고 경로를 반환합니다.
        /// 파일이 아직 없으면 빈 문자열을 반환합니다.
        /// </summary>
        private string CreateConfigBackupIfExists()
        {
            if (!File.Exists(_ini.Path)) return "";

            string directory = Path.GetDirectoryName(_ini.Path) ?? AppDomain.CurrentDomain.BaseDirectory;
            string backupPath = Path.Combine(directory, $"config.backup_{DateTime.Now:yyyyMMdd_HHmmss}.ini");
            File.Copy(_ini.Path, backupPath, false);
            return backupPath;
        }
    }
}
