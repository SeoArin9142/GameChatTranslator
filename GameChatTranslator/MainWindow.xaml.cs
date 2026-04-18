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
using Brushes = System.Windows.Media.Brushes;

namespace GameTranslator
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vlc);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_CONTROL = 0x0002;
        private const int WM_HOTKEY = 0x0312;

        private const int ID_HOTKEY_MOVE_LOCK = 9001;
        private const int ID_HOTKEY_AREA_SELECT = 9002;
        private const int ID_HOTKEY_TRANSLATE = 9003;
        private const int ID_HOTKEY_AUTO = 9004;

        private AreaSelector areaSelector;
        private Rectangle gameChatArea;
        private bool isTranslating = false;
        private bool isLocked = true;

        private IniFile ini;
        private string targetNicknameLang = "ko";
        private uint modMove, modArea, modTrans, modAuto;
        private uint keyMove, keyArea, keyTrans, keyAuto;

        private DispatcherTimer autoTranslateTimer;
        private bool isAutoTranslating = false;
        private string lastRawTextCombined = "";

        private HttpClient httpClient = new HttpClient();
        private Dictionary<string, OcrEngine> ocrEngines = new Dictionary<string, OcrEngine>();

        public MainWindow()
        {
            InitializeComponent();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            string iniPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");

            if (!System.IO.File.Exists(iniPath))
            {
                string defaultIni = @"; ==========================================
; 게임 채팅 번역기 환경설정 (Config)
; ==========================================
[Settings]
NicknameLanguage=ko
Threshold=130
AutoTranslateInterval=5
Opacity=70
Key_MoveLock=Ctrl+7
Key_AreaSelect=Ctrl+8
Key_Translate=Ctrl+9
Key_AutoTranslate=Ctrl+0
";
                System.IO.File.WriteAllText(iniPath, defaultIni, System.Text.Encoding.UTF8);
            }

            ini = new IniFile(iniPath);
            targetNicknameLang = ini.Read("NicknameLanguage");
            if (string.IsNullOrEmpty(targetNicknameLang)) targetNicknameLang = "ko";

            if (string.IsNullOrEmpty(ini.Read("Key_MoveLock"))) ini.Write("Key_MoveLock", "Ctrl+7");
            if (string.IsNullOrEmpty(ini.Read("Key_AreaSelect"))) ini.Write("Key_AreaSelect", "Ctrl+8");
            if (string.IsNullOrEmpty(ini.Read("Key_Translate"))) ini.Write("Key_Translate", "Ctrl+9");
            if (string.IsNullOrEmpty(ini.Read("Key_AutoTranslate"))) ini.Write("Key_AutoTranslate", "Ctrl+0");
            if (string.IsNullOrEmpty(ini.Read("AutoTranslateInterval"))) ini.Write("AutoTranslateInterval", "5");

            // 불투명도 적용
            if (int.TryParse(ini.Read("Opacity"), out int opVal))
            {
                this.Opacity = Math.Clamp(opVal, 10, 100) / 100.0;
            }

            int intervalSeconds = 5;
            if (int.TryParse(ini.Read("AutoTranslateInterval"), out int i)) intervalSeconds = i;

            autoTranslateTimer = new DispatcherTimer();
            autoTranslateTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
            autoTranslateTimer.Tick += (s, e) => { if (!isTranslating) runTranslation(); };

            string[] tags = { "ko", "en-US", "zh-Hans-CN", "ja", "ru" };
            foreach (var tag in tags)
            {
                var engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(tag));
                if (engine != null) ocrEngines.Add(tag, engine);
            }
        }

        private void UpdateYellowHotkeyGuideText()
        {
            string m = ini.Read("Key_MoveLock");
            string a = ini.Read("Key_AreaSelect");
            string t = ini.Read("Key_Translate");
            string au = ini.Read("Key_AutoTranslate");

            // 🌟 자동 번역 중일 때 '● 자동 번역 중...' 표시 추가
            string status = isAutoTranslating ? "  <Run Foreground='Lime'>● 자동 번역 중...</Run>" : "";
            string newGuide = $"[{m}] 이동/잠금  [{a}] 영역설정  [{t}] 번역  [{au}] 자동번역";

            foreach (var tb in FindVisualChildren<TextBlock>(this))
            {
                if (tb.Text.Contains("이동") && tb.Text.Contains("영역설정"))
                {
                    tb.Inlines.Clear();
                    tb.Inlines.Add(new Run(newGuide));
                    if (isAutoTranslating)
                    {
                        tb.Inlines.Add(new Run("  ● 자동 번역 중...") { Foreground = Brushes.Lime, FontWeight = FontWeights.Bold });
                    }
                    tb.Text = newGuide;
                    break;
                }
            }
        }

        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T) yield return (T)child;
                    foreach (T childOfChild in FindVisualChildren<T>(child)) yield return childOfChild;
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowUtils.SetClickThrough(this);
            UpdateYellowHotkeyGuideText();

            // 🌟 [핵심] 게임 UI에 맞춰 좌측 하단에 자동 배치
            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;

            // 번역창 크기 및 위치 세팅 (좌측 하단)
            this.Width = 500;
            this.Height = 130;
            this.Left = 20;
            this.Top = screenH - this.Height - 50;

            // 캡처 영역을 번역창 바로 위쪽 공간으로 자동 초기화
            gameChatArea = new Rectangle((int)this.Left, (int)(this.Top - 250 - 10), 500, 250);

            // 🌟 [핵심] 언어팩 설치 누락 검사 및 경고
            string missingLangs = "";
            if (!ocrEngines.ContainsKey("ru")) missingLangs += "러시아어 ";
            if (!ocrEngines.ContainsKey("ja")) missingLangs += "일본어 ";
            if (!ocrEngines.ContainsKey("zh-Hans-CN")) missingLangs += "중국어 ";

            if (missingLangs != "")
            {
                TxtResult.Text = $"⚠️ [경고] {missingLangs}언어팩이 윈도우에 없습니다!\n솔루션 폴더의 'LangInstall.bat'를 관리자 권한으로 실행하세요.\n\n📍 좌측 하단 기본 영역으로 자동 세팅되었습니다.";
            }
            else
            {
                TxtResult.Text = "📍 좌측 하단 기본 영역으로 자동 세팅되었습니다.\n영역이 조금 안 맞으면 단축키로 다시 잡아주세요.";
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(handle);
            source.AddHook(HwndHook);

            ParseHotkey(ini.Read("Key_MoveLock"), out modMove, out keyMove);
            ParseHotkey(ini.Read("Key_AreaSelect"), out modArea, out keyArea);
            ParseHotkey(ini.Read("Key_Translate"), out modTrans, out keyTrans);
            ParseHotkey(ini.Read("Key_AutoTranslate"), out modAuto, out keyAuto);

            bool b1 = RegisterHotKey(handle, ID_HOTKEY_MOVE_LOCK, modMove, keyMove);
            bool b2 = RegisterHotKey(handle, ID_HOTKEY_AREA_SELECT, modArea, keyArea);
            bool b3 = RegisterHotKey(handle, ID_HOTKEY_TRANSLATE, modTrans, keyTrans);
            bool b4 = RegisterHotKey(handle, ID_HOTKEY_AUTO, modAuto, keyAuto);

            if (!b1 || !b2 || !b3 || !b4)
            {
                System.Windows.MessageBox.Show("단축키 등록 실패! config.ini를 확인해주세요.", "에러", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ParseHotkey(string hotkeyStr, out uint modifier, out uint vk)
        {
            modifier = 0; vk = 0;
            if (string.IsNullOrEmpty(hotkeyStr)) return;

            hotkeyStr = hotkeyStr.ToUpper().Replace(" ", "");
            if (hotkeyStr.Contains("CTRL+")) { modifier |= MOD_CONTROL; hotkeyStr = hotkeyStr.Replace("CTRL+", ""); }
            if (hotkeyStr.Contains("ALT+")) { modifier |= 0x0001; hotkeyStr = hotkeyStr.Replace("ALT+", ""); }
            if (hotkeyStr.Contains("SHIFT+")) { modifier |= 0x0004; hotkeyStr = hotkeyStr.Replace("SHIFT+", ""); }

            if (Regex.IsMatch(hotkeyStr, @"^[0-9]$")) hotkeyStr = "D" + hotkeyStr;
            if (hotkeyStr == "~" || hotkeyStr == "`" || hotkeyStr == "TILDE") { vk = 0xC0; return; }

            if (Enum.TryParse(hotkeyStr, true, out Key wpfKey)) { vk = (uint)KeyInterop.VirtualKeyFromKey(wpfKey); }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                switch (wParam.ToInt32())
                {
                    case ID_HOTKEY_MOVE_LOCK: ToggleMoveLock(); handled = true; break;
                    case ID_HOTKEY_AREA_SELECT: startAreaSelection(); handled = true; break;
                    case ID_HOTKEY_TRANSLATE: runTranslation(); handled = true; break;
                    case ID_HOTKEY_AUTO: ToggleAutoTranslate(); handled = true; break;
                }
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, ID_HOTKEY_MOVE_LOCK);
            UnregisterHotKey(handle, ID_HOTKEY_AREA_SELECT);
            UnregisterHotKey(handle, ID_HOTKEY_TRANSLATE);
            UnregisterHotKey(handle, ID_HOTKEY_AUTO);
            base.OnClosed(e);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (!isLocked) this.DragMove(); }

        private void ToggleMoveLock()
        {
            isLocked = !isLocked;
            if (isLocked)
            {
                WindowUtils.SetClickThrough(this);
                MainBorder.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#55FFFFFF"));
            }
            else
            {
                WindowUtils.RemoveClickThrough(this);
                MainBorder.BorderBrush = Brushes.LimeGreen;
            }
        }

        private void ToggleAutoTranslate()
        {
            if (gameChatArea == Rectangle.Empty) return;

            isAutoTranslating = !isAutoTranslating;
            if (isAutoTranslating)
            {
                runTranslation();
                autoTranslateTimer.Start();
            }
            else
            {
                autoTranslateTimer.Stop();
            }
        }

        private void startAreaSelection()
        {
            if (areaSelector != null) { areaSelector.Close(); }
            areaSelector = new AreaSelector();
            areaSelector.Owner = this;
            areaSelector.Show();
        }

        public void SetCaptureArea(Rectangle area)
        {
            gameChatArea = area;

            // 🌟 [수정] 선택한 영역의 '최하단 좌표(area.Y + area.Height)'에서 50픽셀을 더 내려서 배치
            // 이렇게 하면 영역이 어디든 항상 그 영역의 아래쪽에 번역창이 뜹니다.
            this.Top = area.Y + area.Height + 50;
            this.Left = area.X - 10;
            this.Width = area.Width;

            this.Visibility = Visibility.Visible;
            this.Topmost = true;

            // 노란색 단축키 가이드 텍스트 업데이트
            UpdateYellowHotkeyGuideText();
        }

        private async void runTranslation()
        {
            if (isTranslating || gameChatArea == Rectangle.Empty) return;
            isTranslating = true;

            int threshold = 130;
            if (int.TryParse(ini.Read("Threshold"), out int t)) threshold = t;

            try
            {
                using Bitmap rawBitmap = new Bitmap(gameChatArea.Width, gameChatArea.Height);
                using (Graphics g = Graphics.FromImage(rawBitmap)) { g.CopyFromScreen(gameChatArea.Location, System.Drawing.Point.Empty, gameChatArea.Size); }

                int newWidth = rawBitmap.Width * 3;
                int newHeight = rawBitmap.Height * 3;
                using Bitmap resizedBitmap = new Bitmap(newWidth, newHeight);
                using (Graphics g = Graphics.FromImage(resizedBitmap))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(rawBitmap, 0, 0, newWidth, newHeight);
                }

                BitmapData bmpData = resizedBitmap.LockBits(new Rectangle(0, 0, resizedBitmap.Width, resizedBitmap.Height), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(bmpData.Stride) * resizedBitmap.Height;
                byte[] rgbValues = new byte[bytes];
                Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

                for (int i = 0; i < rgbValues.Length; i += 4)
                {
                    byte b = rgbValues[i]; byte g = rgbValues[i + 1]; byte r = rgbValues[i + 2];
                    int maxRGB = Math.Max(r, Math.Max(g, b));
                    byte color = maxRGB > threshold ? (byte)0 : (byte)255;
                    rgbValues[i] = color; rgbValues[i + 1] = color; rgbValues[i + 2] = color; rgbValues[i + 3] = 255;
                }
                Marshal.Copy(rgbValues, 0, bmpData.Scan0, bytes);
                resizedBitmap.UnlockBits(bmpData);

                using MemoryStream ms = new MemoryStream();
                resizedBitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
                SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();

                var ocrResults = new Dictionary<string, OcrResult>();
                foreach (var kvp in ocrEngines)
                {
                    ocrResults.Add(kvp.Key, await kvp.Value.RecognizeAsync(softwareBitmap));
                }

                var masterResult = ocrResults.ContainsKey(targetNicknameLang) ? ocrResults[targetNicknameLang] : (ocrResults.ContainsKey("ko") ? ocrResults["ko"] : null);
                if (masterResult == null) return;

                string currentRawTextCombined = string.Join("\n", masterResult.Lines.Select(l => l.Text.Trim()));
                if (currentRawTextCombined == lastRawTextCombined) return;
                lastRawTextCombined = currentRawTextCombined;

                TxtResult.Inlines.Clear();

                foreach (var masterLine in masterResult.Lines)
                {
                    if (masterLine.Words.Count == 0) continue;

                    double mTop = masterLine.Words[0].BoundingRect.Top;
                    double mBot = masterLine.Words[0].BoundingRect.Bottom;

                    string krRawText = masterLine.Text.Trim();

                    var strictMatch = Regex.Match(krRawText, @"^\s*([^\[\(\<]+?\s*[\[\(\<].+?[\]\)\>]\s*[:;：!])(.*)$");
                    if (!strictMatch.Success) continue;

                    string characterNameGold = strictMatch.Groups[1].Value + " ";
                    string bestMessage = "";
                    int bestScore = -1;

                    foreach (var kvp in ocrResults)
                    {
                        var line = kvp.Value.Lines
                            .FirstOrDefault(l => l.Words.Count > 0 &&
                                                 l.Words.Any(w => w.BoundingRect.Bottom > mTop && w.BoundingRect.Top < mBot));

                        if (line != null)
                        {
                            string fullText = line.Text;
                            string msgOnly = fullText;

                            int subCIdx = fullText.IndexOfAny(new char[] { ':', ';', '：' });
                            if (subCIdx != -1)
                            {
                                msgOnly = fullText.Substring(subCIdx + 1).Trim();
                            }
                            else
                            {
                                int brIdx = fullText.LastIndexOf(']');
                                if (brIdx != -1) msgOnly = fullText.Substring(brIdx + 1).Trim();
                            }

                            int score = msgOnly.Length;

                            if (kvp.Key == "ru" && Regex.Matches(msgOnly, @"[а-яА-ЯёЁ]").Count >= 1) score += 20000;
                            else if (kvp.Key == "ja" && Regex.Matches(msgOnly, @"[ぁ-んァ-ヶ]").Count >= 1) score += 10000;
                            else if (kvp.Key == "zh-Hans-CN" && Regex.Matches(msgOnly, @"[\u4e00-\u9fa5]").Count >= 1) score += 5000;
                            else if (kvp.Key == "en-US" && Regex.Matches(msgOnly, @"[a-zA-Z]").Count >= 3) score += 1000;

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestMessage = msgOnly;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(bestMessage))
                    {
                        bestMessage = strictMatch.Groups[2].Value.Trim();
                    }

                    string translated = bestMessage;
                    if (!string.IsNullOrEmpty(bestMessage) && !Regex.IsMatch(bestMessage, @"^[0-9\W]+$"))
                    {

                        int retryCount = 3;
                        while (retryCount > 0)
                        {
                            try
                            {
                                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=ko&dt=t&q={Uri.EscapeDataString(bestMessage)}";
                                var res = await httpClient.GetStringAsync(url);
                                using var doc = System.Text.Json.JsonDocument.Parse(res);
                                translated = "";
                                foreach (var item in doc.RootElement[0].EnumerateArray()) { translated += item[0].GetString(); }
                                break;
                            }
                            catch
                            {
                                retryCount--;
                                await Task.Delay(300);
                            }
                        }
                    }

                    TxtResult.Inlines.Add(new Run(characterNameGold) { Foreground = Brushes.Gold, FontWeight = FontWeights.Bold });
                    TxtResult.Inlines.Add(new Run(translated) { Foreground = Brushes.White });
                    TxtResult.Inlines.Add(new LineBreak());
                }
            }
            catch (Exception ex) { TxtResult.Text = "에러: " + ex.Message; }
            finally { isTranslating = false; }
        }
    }
}