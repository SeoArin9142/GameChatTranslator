using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace GameTranslator
{
    public partial class MainWindow
    {
        private const string ReleaseListApiUrl = "https://api.github.com/repos/SeoArin9142/GameChatTranslator/releases?per_page=10";
        private const string ReleasePageUrl = "https://github.com/SeoArin9142/GameChatTranslator/releases";
        private static string CurrentAppVersion
        {
            get
            {
                string version = Assembly
                    .GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;

                if (string.IsNullOrWhiteSpace(version))
                {
                    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
                }

                version = version.Split('+')[0].Trim();
                return version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : $"v.{version}";
            }
        }

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            BtnCheckUpdate.IsEnabled = false;
            TxtUpdateStatus.Text = "확인 중...";

            try
            {
                using var response = await httpClient.GetAsync(ReleaseListApiUrl);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                using JsonDocument document = JsonDocument.Parse(json);

                if (TryReadLatestRelease(document.RootElement, out string latestTag, out string latestUrl))
                {
                    ShowUpdateResult(latestTag, latestUrl);
                    return;
                }

                TxtUpdateStatus.Text = "릴리즈 없음";
                MessageBox.Show("확인 가능한 릴리즈가 없습니다.", "업데이트 확인", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TxtUpdateStatus.Text = "확인 실패";
                AppendLog($"업데이트 확인 실패: {ex.Message}");
                MessageBox.Show($"업데이트 정보를 확인하지 못했습니다.\n{ex.Message}", "업데이트 확인 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                BtnCheckUpdate.IsEnabled = true;
            }
        }

        private bool TryReadLatestRelease(JsonElement releases, out string latestTag, out string latestUrl)
        {
            latestTag = "";
            latestUrl = ReleasePageUrl;

            if (releases.ValueKind != JsonValueKind.Array) return false;

            foreach (JsonElement release in releases.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out JsonElement draftValue) && draftValue.GetBoolean())
                {
                    continue;
                }

                if (!release.TryGetProperty("tag_name", out JsonElement tagValue))
                {
                    continue;
                }

                string tag = tagValue.GetString();
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                latestTag = tag.Trim();

                if (release.TryGetProperty("html_url", out JsonElement urlValue))
                {
                    string url = urlValue.GetString();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        latestUrl = url.Trim();
                    }
                }

                return true;
            }

            return false;
        }

        private void ShowUpdateResult(string latestTag, string latestUrl)
        {
            if (IsNewerVersion(latestTag, CurrentAppVersion))
            {
                TxtUpdateStatus.Text = $"새 버전 {latestTag}";
                AppendLog($"새 버전 확인: {latestTag}");

                MessageBoxResult result = MessageBox.Show(
                    $"새 버전이 있습니다.\n현재: {CurrentAppVersion}\n최신: {latestTag}\n\n릴리즈 페이지를 열까요?",
                    "업데이트 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    OpenReleasePage(latestUrl);
                }

                return;
            }

            if (AreSameVersion(latestTag, CurrentAppVersion))
            {
                TxtUpdateStatus.Text = "최신 버전";
                MessageBox.Show($"현재 최신 버전입니다.\n현재: {CurrentAppVersion}", "업데이트 확인", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            TxtUpdateStatus.Text = $"확인 필요 {latestTag}";
            MessageBoxResult fallbackResult = MessageBox.Show(
                $"릴리즈 버전 형식이 달라 직접 확인이 필요합니다.\n현재: {CurrentAppVersion}\n확인된 릴리즈: {latestTag}\n\n릴리즈 페이지를 열까요?",
                "업데이트 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (fallbackResult == MessageBoxResult.Yes)
            {
                OpenReleasePage(latestUrl);
            }
        }

        private bool AreSameVersion(string left, string right)
        {
            return NormalizeVersionTag(left) == NormalizeVersionTag(right);
        }

        private bool IsNewerVersion(string latestTag, string currentTag)
        {
            int[] latestParts = ExtractVersionParts(latestTag);
            int[] currentParts = ExtractVersionParts(currentTag);

            int maxLength = Math.Max(latestParts.Length, currentParts.Length);
            for (int i = 0; i < maxLength; i++)
            {
                int latestValue = i < latestParts.Length ? latestParts[i] : 0;
                int currentValue = i < currentParts.Length ? currentParts[i] : 0;

                if (latestValue > currentValue) return true;
                if (latestValue < currentValue) return false;
            }

            return false;
        }

        private int[] ExtractVersionParts(string tag)
        {
            Match match = Regex.Match(NormalizeVersionTag(tag), @"\d+(\.\d+)*");
            if (!match.Success) return Array.Empty<int>();

            return match.Value
                .Split('.')
                .Select(part => int.TryParse(part, out int value) ? value : 0)
                .ToArray();
        }

        private string NormalizeVersionTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return "";

            string normalized = tag.Trim().ToLowerInvariant();
            if (normalized.StartsWith("v.")) normalized = normalized.Substring(2);
            else if (normalized.StartsWith("v")) normalized = normalized.Substring(1);

            return normalized.Trim();
        }

        private void OpenReleasePage(string url)
        {
            string targetUrl = string.IsNullOrWhiteSpace(url) ? ReleasePageUrl : url;
            Process.Start(new ProcessStartInfo
            {
                FileName = targetUrl,
                UseShellExecute = true
            });
        }
    }
}
