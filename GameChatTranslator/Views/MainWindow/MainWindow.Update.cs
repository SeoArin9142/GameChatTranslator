using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Velopack;
using Velopack.Sources;

namespace GameTranslator
{
    /// <summary>
    /// GitHub Release 기반 업데이트 확인 로직을 담당하는 partial 파일입니다.
    /// 시작 시 자동 확인과 환경설정창의 수동 확인 버튼이 같은 내부 흐름을 사용합니다.
    /// </summary>
    public partial class MainWindow
    {
        private const string GitHubRepoUrl = "https://github.com/SeoArin9142/GameChatTranslator";
        private const string ReleaseListApiUrl = "https://api.github.com/repos/SeoArin9142/GameChatTranslator/releases?per_page=10";
        private const string ReleasePageUrl = "https://github.com/SeoArin9142/GameChatTranslator/releases";

        /// <summary>
        /// 업데이트 확인이 시작 시 자동으로 실행된 것인지, 사용자가 버튼으로 수동 실행한 것인지 구분합니다.
        /// 모드에 따라 메시지 박스 표시 여부와 "다시 묻지 않기" 버튼 노출 여부가 달라집니다.
        /// </summary>
        private enum UpdateCheckMode
        {
            Startup,
            Manual
        }

        /// <summary>
        /// GitHub Releases API에서 읽은 릴리즈 태그와 웹 URL을 담는 내부 DTO입니다.
        /// Tag는 예: v.1.0.6-alpha, Url은 사용자가 브라우저로 이동할 릴리즈 페이지입니다.
        /// </summary>
        private sealed class ReleaseInfo
        {
            public string Tag { get; set; } = "";
            public string Url { get; set; } = ReleasePageUrl;
        }

        /// <summary>
        /// 현재 실행 중인 애플리케이션 버전을 어셈블리 메타데이터에서 읽습니다.
        /// Git 커밋 해시 같은 InformationalVersion의 '+' 이후 메타데이터는 제거하고,
        /// 비교 로직과 표시 문구가 동일하게 "v.버전" 형식을 쓰도록 정규화합니다.
        /// </summary>
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

        /// <summary>
        /// 환경설정창에서 수동 업데이트 확인을 실행합니다.
        /// <paramref name="owner"/>는 메시지 박스의 부모 창이고,
        /// <paramref name="setStatus"/>는 환경설정창 상태 텍스트를 갱신하는 콜백입니다.
        /// </summary>
        internal async Task RunManualUpdateCheckAsync(Window owner, Action<string> setStatus)
        {
            await CheckForUpdatesAsync(UpdateCheckMode.Manual, owner, setStatus);
        }

        /// <summary>
        /// 프로그램 시작 시 업데이트 자동 확인 설정을 검사하고, 켜져 있으면 GitHub 릴리즈를 조회합니다.
        /// 반환값이 false이면 사용자가 릴리즈 페이지로 이동하기로 선택해 프로그램을 종료해야 한다는 의미입니다.
        /// </summary>
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

        /// <summary>
        /// 공통 업데이트 확인 흐름입니다.
        /// <paramref name="mode"/>는 시작 자동 확인인지 수동 확인인지 구분하고,
        /// <paramref name="owner"/>는 팝업 부모 창,
        /// <paramref name="setStatus"/>는 UI 상태 텍스트 갱신 콜백입니다.
        /// 반환값 false는 릴리즈 페이지 이동 후 앱 종료가 필요함을 의미합니다.
        /// </summary>
        private async Task<bool> CheckForUpdatesAsync(UpdateCheckMode mode, Window owner, Action<string> setStatus)
        {
            SetUpdateStatus(setStatus, "확인 중...");

            try
            {
                UpdateManager updateManager = CreateUpdateManager();
                if (CanUseVelopackDirectUpdate(updateManager))
                {
                    return await CheckForVelopackUpdatesAsync(mode, owner, setStatus, updateManager);
                }

                return await CheckForLegacyReleaseUpdatesAsync(mode, owner, setStatus);
            }
            catch (Exception ex)
            {
                SetUpdateStatus(setStatus, "확인 실패");
                AppendLog($"업데이트 확인 실패: {ex.Message}");

                if (mode == UpdateCheckMode.Manual)
                {
                    MessageBox.Show(owner, $"업데이트 정보를 확인하지 못했습니다.\n{ex.Message}", "업데이트 확인 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            return true;
        }

        /// <summary>
        /// Setup.exe로 설치된 Velopack 환경에서는 releases.win.json을 기준으로 직접 다운로드/재시작 업데이트를 수행합니다.
        /// </summary>
        private async Task<bool> CheckForVelopackUpdatesAsync(UpdateCheckMode mode, Window owner, Action<string> setStatus, UpdateManager updateManager)
        {
            VelopackAsset pendingRestart = updateManager.UpdatePendingRestart;
            if (pendingRestart != null)
            {
                string pendingVersion = FormatVelopackVersion(pendingRestart.Version);
                SetUpdateStatus(setStatus, $"재시작 필요 {pendingVersion}");
                AppendLog($"다운로드된 업데이트가 대기 중입니다. ({pendingVersion})");

                UpdatePromptResult pendingResult = ShowUpdatePrompt(mode, owner, pendingVersion, canInstallDirectly: true);
                if (pendingResult == UpdatePromptResult.DisableStartupCheck)
                {
                    ini.Write("CheckUpdatesOnStartup", "false");
                    SetUpdateStatus(setStatus, "자동 확인 끔");
                    AppendLog("시작 시 업데이트 자동 확인을 비활성화했습니다.");
                    return true;
                }

                if (pendingResult == UpdatePromptResult.InstallNow)
                {
                    AppendLog($"대기 중인 업데이트를 적용하기 위해 재시작합니다. ({pendingVersion})");
                    updateManager.ApplyUpdatesAndRestart(pendingRestart);
                    return false;
                }

                return true;
            }

            UpdateInfo updateInfo = await updateManager.CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                SetUpdateStatus(setStatus, "최신 버전");
                AppendLog($"업데이트 확인: 현재 최신 버전입니다. ({CurrentAppVersion})");

                if (mode == UpdateCheckMode.Manual)
                {
                    MessageBox.Show(owner, $"현재 최신 버전입니다.\n현재: {CurrentAppVersion}", "업데이트 확인", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return true;
            }

            string latestVersion = FormatVelopackVersion(updateInfo.TargetFullRelease.Version);
            SetUpdateStatus(setStatus, $"새 버전 {latestVersion}");
            AppendLog($"새 버전 확인: {latestVersion}");

            UpdatePromptResult promptResult = ShowUpdatePrompt(mode, owner, latestVersion, canInstallDirectly: true);
            if (promptResult == UpdatePromptResult.DisableStartupCheck)
            {
                ini.Write("CheckUpdatesOnStartup", "false");
                SetUpdateStatus(setStatus, "자동 확인 끔");
                AppendLog("시작 시 업데이트 자동 확인을 비활성화했습니다.");
                return true;
            }

            if (promptResult != UpdatePromptResult.InstallNow)
            {
                return true;
            }

            AppendLog($"업데이트 다운로드 시작: {latestVersion}");
            await updateManager.DownloadUpdatesAsync(
                updateInfo,
                progress => SetUpdateStatus(setStatus, $"다운로드 중... {progress}%"),
                CancellationToken.None);

            SetUpdateStatus(setStatus, "업데이트 적용 중...");
            AppendLog($"업데이트 다운로드 완료: {latestVersion}. 재시작 후 적용합니다.");
            updateManager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
            return false;
        }

        /// <summary>
        /// 설치형이 아닌 ZIP/직접 실행 환경에서는 기존 GitHub 릴리즈 페이지 안내 흐름을 유지합니다.
        /// </summary>
        private async Task<bool> CheckForLegacyReleaseUpdatesAsync(UpdateCheckMode mode, Window owner, Action<string> setStatus)
        {
            ReleaseInfo releaseInfo = await FetchLatestReleaseAsync();

            if (releaseInfo != null)
            {
                return ShowLegacyUpdateResult(mode, owner, setStatus, releaseInfo.Tag, releaseInfo.Url);
            }

            SetUpdateStatus(setStatus, "릴리즈 없음");
            if (mode == UpdateCheckMode.Manual)
            {
                MessageBox.Show(owner, "확인 가능한 릴리즈가 없습니다.", "업데이트 확인", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return true;
        }

        /// <summary>
        /// GitHub Releases API에서 최신 릴리즈 목록을 받아 첫 번째 유효 릴리즈 정보를 반환합니다.
        /// 초안(draft)은 배포 대상이 아니므로 TryReadLatestRelease에서 제외됩니다.
        /// </summary>
        private async Task<ReleaseInfo> FetchLatestReleaseAsync()
        {
            using var response = await httpClient.GetAsync(ReleaseListApiUrl);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);

            return TryReadLatestRelease(document.RootElement);
        }

        /// <summary>
        /// GitHub Releases JSON 배열에서 사용자에게 보여줄 최신 릴리즈 하나를 추출합니다.
        /// <paramref name="releases"/>는 API 응답의 루트 JSON 배열입니다.
        /// 반환값은 태그와 URL을 담은 ReleaseInfo이며, 유효 릴리즈가 없으면 null입니다.
        /// </summary>
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

        /// <summary>
        /// 최신 릴리즈와 현재 버전을 비교해 사용자 안내 UI를 표시합니다.
        /// <paramref name="mode"/>는 자동/수동 확인 모드,
        /// <paramref name="owner"/>는 팝업 부모 창,
        /// <paramref name="setStatus"/>는 상태 텍스트 콜백,
        /// <paramref name="latestTag"/>는 GitHub에서 확인한 최신 태그,
        /// <paramref name="latestUrl"/>은 열어야 할 릴리즈 페이지 URL입니다.
        /// 반환값 false는 업데이트 페이지로 이동하면서 앱을 종료해야 한다는 의미입니다.
        /// </summary>
        private bool ShowLegacyUpdateResult(UpdateCheckMode mode, Window owner, Action<string> setStatus, string latestTag, string latestUrl)
        {
            if (IsNewerVersion(latestTag, CurrentAppVersion))
            {
                SetUpdateStatus(setStatus, $"새 버전 {latestTag}");
                AppendLog($"새 버전 확인: {latestTag}");

                UpdatePromptResult promptResult = ShowUpdatePrompt(mode, owner, latestTag, canInstallDirectly: false);

                if (promptResult == UpdatePromptResult.DisableStartupCheck)
                {
                    ini.Write("CheckUpdatesOnStartup", "false");
                    SetUpdateStatus(setStatus, "자동 확인 끔");
                    AppendLog("시작 시 업데이트 자동 확인을 비활성화했습니다.");
                    return true;
                }

                if (promptResult == UpdatePromptResult.OpenReleasePage)
                {
                    OpenReleasePage(latestUrl);
                    System.Windows.Application.Current.Shutdown();
                    return false;
                }

                return true;
            }

            if (AreSameVersion(latestTag, CurrentAppVersion))
            {
                SetUpdateStatus(setStatus, "최신 버전");
                AppendLog($"업데이트 확인: 현재 최신 버전입니다. ({CurrentAppVersion})");

                if (mode == UpdateCheckMode.Manual)
                {
                    MessageBox.Show(owner, $"현재 최신 버전입니다.\n현재: {CurrentAppVersion}", "업데이트 확인", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return true;
            }

            SetUpdateStatus(setStatus, $"확인 필요 {latestTag}");

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

        /// <summary>
        /// 현재 실행 환경에서 Velopack 인앱 업데이트를 사용할 수 있는지 판단합니다.
        /// 설치형(current + Update.exe) 환경에서만 직접 다운로드/적용을 허용하고,
        /// ZIP/직접 실행 모드는 기존 릴리즈 페이지 방식으로 유지합니다.
        /// </summary>
        private bool CanUseVelopackDirectUpdate(UpdateManager updateManager)
        {
            return updateManager != null && updateManager.IsInstalled && !updateManager.IsPortable;
        }

        /// <summary>
        /// GitHub Releases를 업데이트 소스로 읽는 Velopack UpdateManager를 생성합니다.
        /// 현재 저장소는 public이므로 access token 없이 조회합니다.
        /// </summary>
        private UpdateManager CreateUpdateManager()
        {
            return new UpdateManager(new GithubSource(GitHubRepoUrl, "", false));
        }

        /// <summary>
        /// 현재 실행 중인 앱의 설치/실행 경로를 사용자에게 보여줄 문자열로 반환합니다.
        /// 설치형이면 Velopack current 폴더 경로를, ZIP 직접 실행이면 현재 EXE 폴더 경로를 표시합니다.
        /// </summary>
        internal string GetInstallLocationPath()
        {
            try
            {
                return Path.GetFullPath(AppContext.BaseDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return (AppContext.BaseDirectory ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        /// <summary>
        /// 현재 실행 중인 앱이 Velopack 설치형 환경인지 판단합니다.
        /// 설치형이면 인앱 업데이트 가능 여부와 실행 경로 표시 문구가 함께 설치형 기준으로 바뀝니다.
        /// </summary>
        internal bool IsInstalledExecution()
        {
            try
            {
                UpdateManager updateManager = CreateUpdateManager();
                return CanUseVelopackDirectUpdate(updateManager);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 현재 실행 중인 앱의 설치/실행 경로를 사용자에게 보여줄 문자열로 반환합니다.
        /// 설치형이면 Velopack current 폴더 경로를, ZIP 직접 실행이면 현재 EXE 폴더 경로를 표시합니다.
        /// </summary>
        internal string GetInstallLocationDisplayText()
        {
            string baseDirectory = GetInstallLocationPath();
            return IsInstalledExecution()
                ? $"설치형 실행 경로: {baseDirectory}"
                : $"직접 실행 경로: {baseDirectory}";
        }

        /// <summary>
        /// Velopack 버전 객체를 현재 앱 표기 형식(v.1.0.24-alpha)으로 정규화합니다.
        /// </summary>
        private string FormatVelopackVersion(object version)
        {
            string text = version?.ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text)) return "v.0.0.0";
            return text.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? text : $"v.{text}";
        }

        /// <summary>
        /// 수동/자동 업데이트 확인에서 공통으로 사용하는 업데이트 선택 팝업을 띄웁니다.
        /// </summary>
        private UpdatePromptResult ShowUpdatePrompt(UpdateCheckMode mode, Window owner, string latestVersion, bool canInstallDirectly)
        {
            var prompt = new UpdatePromptWindow(CurrentAppVersion, latestVersion, mode == UpdateCheckMode.Startup, canInstallDirectly)
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            prompt.ShowDialog();
            return prompt.Result;
        }

        /// <summary>
        /// 백그라운드 다운로드 진행률 콜백에서도 안전하게 설정창 상태 문구를 갱신합니다.
        /// </summary>
        private void SetUpdateStatus(Action<string> setStatus, string value)
        {
            if (setStatus == null) return;

            if (Dispatcher.CheckAccess())
            {
                setStatus(value);
                return;
            }

            Dispatcher.Invoke(() => setStatus(value));
        }

        /// <summary>
        /// 설정 문자열이 시작 시 자동 업데이트 확인을 끄는 값인지 판단합니다.
        /// <paramref name="value"/>는 config.ini의 CheckUpdatesOnStartup 값입니다.
        /// </summary>
        private bool IsDisabledSetting(string value)
        {
            return settingsService.IsDisabled(value);
        }

        /// <summary>
        /// 두 버전 태그가 동일한 버전인지 비교합니다.
        /// <paramref name="left"/>와 <paramref name="right"/>는 v. 접두사 또는 alpha 접미사가 포함될 수 있는 버전 문자열입니다.
        /// </summary>
        private bool AreSameVersion(string left, string right)
        {
            return NormalizeVersionTag(left) == NormalizeVersionTag(right);
        }

        /// <summary>
        /// 최신 릴리즈 태그가 현재 실행 버전보다 높은지 비교합니다.
        /// <paramref name="latestTag"/>는 GitHub 릴리즈 태그,
        /// <paramref name="currentTag"/>는 현재 앱 버전 태그입니다.
        /// 숫자 파트만 비교하므로 alpha 접미사가 있어도 1.0.6처럼 비교됩니다.
        /// </summary>
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

        /// <summary>
        /// 버전 태그에서 숫자 파트를 추출해 정수 배열로 변환합니다.
        /// <paramref name="tag"/>는 v.1.0.6-alpha 같은 버전 문자열입니다.
        /// 반환값은 비교에 사용할 [1, 0, 6] 형태의 숫자 배열입니다.
        /// </summary>
        private int[] ExtractVersionParts(string tag)
        {
            Match match = Regex.Match(NormalizeVersionTag(tag), @"\d+(\.\d+)*");
            if (!match.Success) return Array.Empty<int>();

            return match.Value
                .Split('.')
                .Select(part => int.TryParse(part, out int value) ? value : 0)
                .ToArray();
        }

        /// <summary>
        /// 버전 문자열에서 v 또는 v. 접두사를 제거하고 소문자로 정규화합니다.
        /// <paramref name="tag"/>는 비교할 원본 버전 문자열입니다.
        /// </summary>
        private string NormalizeVersionTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return "";

            string normalized = tag.Trim().ToLowerInvariant();
            if (normalized.StartsWith("v.")) normalized = normalized.Substring(2);
            else if (normalized.StartsWith("v")) normalized = normalized.Substring(1);

            return normalized.Trim();
        }

        /// <summary>
        /// 기본 브라우저로 릴리즈 페이지를 엽니다.
        /// <paramref name="url"/>은 이동할 릴리즈 URL이며, 비어 있으면 전체 Releases 페이지를 사용합니다.
        /// </summary>
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
