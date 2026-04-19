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
        private enum UpdateCheckMode
        {
            Startup,
            Manual
        }

        private sealed class ReleaseInfo
        {
            public string Tag { get; set; } = "";
            public string Url { get; set; } = ReleasePageUrl;
        }

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

        internal async Task RunManualUpdateCheckAsync(Window owner, Action<string> setStatus)
        {
            await CheckForUpdatesAsync(UpdateCheckMode.Manual, owner, setStatus);
        }

        private async Task<bool> CheckForUpdatesOnStartupAsync()
        {
            string value = ini.Read("CheckUpdatesOnStartup") ?? "true";
            if (IsDisabledSetting(value))
            {
                AppendLog("시작 시 업데이트 자동 확인이 비활성화되어 있습니다.");
                return true;
            }

            return await CheckForUpdatesAsync(UpdateCheckMode.Startup, this, null);
        }

        private async Task<bool> CheckForUpdatesAsync(UpdateCheckMode mode, Window owner, Action<string> setStatus)
        {
            setStatus?.Invoke("확인 중...");

            try
            {
                ReleaseInfo releaseInfo = await FetchLatestReleaseAsync();

                if (releaseInfo != null)
                {
                    return ShowUpdateResult(mode, owner, setStatus, releaseInfo.Tag, releaseInfo.Url);
                }

                setStatus?.Invoke("릴리즈 없음");
                if (mode == UpdateCheckMode.Manual)
                {
                    MessageBox.Show(owner, "확인 가능한 릴리즈가 없습니다.", "업데이트 확인", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                setStatus?.Invoke("확인 실패");
                AppendLog($"업데이트 확인 실패: {ex.Message}");

                if (mode == UpdateCheckMode.Manual)
                {
                    MessageBox.Show(owner, $"업데이트 정보를 확인하지 못했습니다.\n{ex.Message}", "업데이트 확인 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            return true;
        }

        private async Task<ReleaseInfo> FetchLatestReleaseAsync()
        {
            using var response = await httpClient.GetAsync(ReleaseListApiUrl);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);

            return TryReadLatestRelease(document.RootElement);
        }

        private ReleaseInfo TryReadLatestRelease(JsonElement releases)
        {
            if (releases.ValueKind != JsonValueKind.Array) return null;

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

                var releaseInfo = new ReleaseInfo
                {
                    Tag = tag.Trim(),
                    Url = ReleasePageUrl
                };

                if (release.TryGetProperty("html_url", out JsonElement urlValue))
                {
                    string url = urlValue.GetString();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        releaseInfo.Url = url.Trim();
                    }
                }

                return releaseInfo;
            }

            return null;
        }

        private bool ShowUpdateResult(UpdateCheckMode mode, Window owner, Action<string> setStatus, string latestTag, string latestUrl)
        {
            if (IsNewerVersion(latestTag, CurrentAppVersion))
            {
                setStatus?.Invoke($"새 버전 {latestTag}");
                AppendLog($"새 버전 확인: {latestTag}");

                var prompt = new UpdatePromptWindow(CurrentAppVersion, latestTag, mode == UpdateCheckMode.Startup)
                {
                    Owner = owner,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                prompt.ShowDialog();

                if (prompt.Result == UpdatePromptResult.DisableStartupCheck)
                {
                    ini.Write("CheckUpdatesOnStartup", "false");
                    setStatus?.Invoke("자동 확인 끔");
                    AppendLog("시작 시 업데이트 자동 확인을 비활성화했습니다.");
                    return true;
                }

                if (prompt.Result == UpdatePromptResult.OpenReleasePage)
                {
                    OpenReleasePage(latestUrl);
                    System.Windows.Application.Current.Shutdown();
                    return false;
                }

                return true;
            }

            if (AreSameVersion(latestTag, CurrentAppVersion))
            {
                setStatus?.Invoke("최신 버전");
                AppendLog($"업데이트 확인: 현재 최신 버전입니다. ({CurrentAppVersion})");

                if (mode == UpdateCheckMode.Manual)
                {
                    MessageBox.Show(owner, $"현재 최신 버전입니다.\n현재: {CurrentAppVersion}", "업데이트 확인", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return true;
            }

            setStatus?.Invoke($"확인 필요 {latestTag}");

            if (mode == UpdateCheckMode.Manual)
            {
                MessageBoxResult fallbackResult = MessageBox.Show(
                    owner,
                    $"릴리즈 버전 형식이 달라 직접 확인이 필요합니다.\n현재: {CurrentAppVersion}\n확인된 릴리즈: {latestTag}\n\n릴리즈 페이지를 열까요?",
                    "업데이트 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (fallbackResult == MessageBoxResult.Yes)
                {
                    OpenReleasePage(latestUrl);
                }
            }

            return true;
        }

        private bool IsDisabledSetting(string value)
        {
            return value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("n", StringComparison.OrdinalIgnoreCase);
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
