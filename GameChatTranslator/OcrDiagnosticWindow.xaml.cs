using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfImage = System.Windows.Controls.Image;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace GameTranslator
{
    /// <summary>
    /// 현재 캡처 영역의 OCR 전처리 후보와 OCR 결과를 비교해서 보여주는 진단 창입니다.
    /// 번역 API는 호출하지 않고 Windows OCR과 점수화 결과만 표시합니다.
    /// </summary>
    public partial class OcrDiagnosticWindow : Window
    {
        private readonly MainWindow mainWindow;

        /// <summary>
        /// OCR 진단 창을 생성합니다.
        /// <paramref name="mainWindow"/>는 현재 캡처 영역과 OCR 전처리/점수화 로직을 보유한 메인 창입니다.
        /// </summary>
        public OcrDiagnosticWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            this.mainWindow = mainWindow;

            Loaded += async (s, e) => await RunDiagnosticAsync();
        }

        /// <summary>
        /// [현재 캡처 영역 진단] 버튼 클릭 시 OCR 진단을 다시 수행합니다.
        /// </summary>
        private async void BtnRunDiagnostic_Click(object sender, RoutedEventArgs e)
        {
            await RunDiagnosticAsync();
        }

        /// <summary>
        /// MainWindow의 OCR 진단 API를 호출하고 결과를 탭 UI에 렌더링합니다.
        /// 실행 중에는 중복 클릭을 막고, 실패 시 오류 내용을 상태와 요약 탭에 표시합니다.
        /// </summary>
        private async System.Threading.Tasks.Task RunDiagnosticAsync()
        {
            BtnRunDiagnostic.IsEnabled = false;
            TxtStatus.Text = "OCR 진단 실행 중...";

            try
            {
                OcrDiagnosticResult result = await mainWindow.RunOcrDiagnosticAsync();
                RenderResult(result);
                TxtStatus.Text = $"완료: {result.SelectedCandidateName} 선택 / {result.TotalMs}ms";
            }
            catch (Exception ex)
            {
                TabDiagnostics.Items.Clear();
                TabDiagnostics.Items.Add(CreateTextTab("오류", ex.Message));
                TxtStatus.Text = $"실패: {ex.Message}";
            }
            finally
            {
                BtnRunDiagnostic.IsEnabled = true;
            }
        }

        /// <summary>
        /// OCR 진단 결과 전체를 탭 컨트롤에 표시합니다.
        /// 첫 탭은 요약/원본이고, 이후 탭은 전처리 후보별 결과입니다.
        /// </summary>
        private void RenderResult(OcrDiagnosticResult result)
        {
            TabDiagnostics.Items.Clear();
            TabDiagnostics.Items.Add(CreateSummaryTab(result));

            foreach (OcrDiagnosticCandidate candidate in result.Candidates)
            {
                bool selected = candidate.Name == result.SelectedCandidateName;
                TabDiagnostics.Items.Add(CreateCandidateTab(candidate, selected));
            }

            if (TabDiagnostics.Items.Count > 0)
            {
                TabDiagnostics.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 전체 캡처/처리 시간과 원본/확대 이미지를 보여주는 요약 탭을 만듭니다.
        /// </summary>
        private TabItem CreateSummaryTab(OcrDiagnosticResult result)
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var imagePanel = new StackPanel();
            imagePanel.Children.Add(CreateImageBlock("원본 캡처", result.RawPng));
            imagePanel.Children.Add(CreateImageBlock("OCR 확대 이미지", result.ResizedPng));
            Grid.SetColumn(imagePanel, 0);
            root.Children.Add(imagePanel);

            WpfTextBox summary = CreateReadOnlyTextBox(BuildSummaryText(result));
            Grid.SetColumn(summary, 1);
            root.Children.Add(summary);

            return new TabItem
            {
                Header = "요약",
                Content = root
            };
        }

        /// <summary>
        /// 전처리 후보 하나의 전처리/크롭 이미지와 OCR 텍스트 결과를 보여주는 탭을 만듭니다.
        /// </summary>
        private TabItem CreateCandidateTab(OcrDiagnosticCandidate candidate, bool selected)
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var imagePanel = new StackPanel();
            imagePanel.Children.Add(CreateImageBlock("전처리 이미지", candidate.PreprocessedPng));
            imagePanel.Children.Add(CreateImageBlock("OCR 입력 크롭 이미지", candidate.CroppedPng));
            Grid.SetColumn(imagePanel, 0);
            root.Children.Add(imagePanel);

            WpfTextBox details = CreateReadOnlyTextBox(BuildCandidateText(candidate, selected));
            Grid.SetColumn(details, 1);
            root.Children.Add(details);

            return new TabItem
            {
                Header = selected ? $"{candidate.Name} 선택" : candidate.Name,
                Content = root
            };
        }

        /// <summary>
        /// 텍스트만 표시하는 탭을 만듭니다. 주로 오류 메시지에 사용합니다.
        /// </summary>
        private TabItem CreateTextTab(string header, string text)
        {
            return new TabItem
            {
                Header = header,
                Content = CreateReadOnlyTextBox(text)
            };
        }

        /// <summary>
        /// 제목과 PNG byte 배열을 받아 스크롤 가능한 이미지 블록을 만듭니다.
        /// </summary>
        private FrameworkElement CreateImageBlock(string title, byte[] pngBytes)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 10) };
            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = MediaBrushes.White,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var image = new WpfImage
            {
                Source = CreateImageSource(pngBytes),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left
            };

            var border = new Border
            {
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(85, 85, 85)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(MediaColor.FromRgb(17, 17, 17)),
                Padding = new Thickness(6),
                Child = image
            };

            panel.Children.Add(new ScrollViewer
            {
                Content = border,
                Height = 285,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            });

            return panel;
        }

        /// <summary>
        /// 긴 OCR 결과를 표시하기 위한 읽기 전용 TextBox를 만듭니다.
        /// </summary>
        private WpfTextBox CreateReadOnlyTextBox(string text)
        {
            return new WpfTextBox
            {
                Text = text,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(MediaColor.FromRgb(17, 17, 17)),
                Foreground = new SolidColorBrush(MediaColor.FromRgb(230, 230, 230)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(85, 85, 85)),
                FontFamily = new WpfFontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 0)
            };
        }

        /// <summary>
        /// OCR 진단 요약 텍스트를 생성합니다.
        /// </summary>
        private string BuildSummaryText(OcrDiagnosticResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[OCR 진단 요약]");
            builder.AppendLine($"진단 시각: {result.CapturedAt:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"캡처 영역: X={result.CaptureArea.X}, Y={result.CaptureArea.Y}, W={result.CaptureArea.Width}, H={result.CaptureArea.Height}");
            builder.AppendLine($"Threshold: {result.Threshold}");
            builder.AppendLine($"ScaleFactor: {result.ScaleFactor}");
            builder.AppendLine();
            builder.AppendLine("[선택 결과]");
            builder.AppendLine($"선택 후보: {result.SelectedCandidateName}");
            builder.AppendLine($"선택 점수: {result.SelectedScore}");
            builder.AppendLine($"후보 수: {result.Candidates.Count}");
            builder.AppendLine($"OCR 호출 수: {result.OcrCallCount}");
            builder.AppendLine();
            builder.AppendLine("[처리 시간]");
            builder.AppendLine($"Capture: {result.CaptureMs}ms");
            builder.AppendLine($"Resize: {result.ResizeMs}ms");
            builder.AppendLine($"Preprocess: {result.PreprocessMs}ms");
            builder.AppendLine($"Crop: {result.CropMs}ms");
            builder.AppendLine($"OCR: {result.OcrMs}ms");
            builder.AppendLine($"Scoring: {result.ScoringMs}ms");
            builder.AppendLine($"Total: {result.TotalMs}ms");
            builder.AppendLine();
            builder.AppendLine("[후보 점수]");
            foreach (OcrDiagnosticCandidate candidate in result.Candidates.OrderByDescending(c => c.Score))
            {
                builder.AppendLine($"- {candidate.Name}: {candidate.Score}");
            }

            return builder.ToString();
        }

        /// <summary>
        /// 후보별 병합 라인과 언어별 OCR 원문을 텍스트로 정리합니다.
        /// </summary>
        private string BuildCandidateText(OcrDiagnosticCandidate candidate, bool selected)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"[{candidate.Name}]");
            builder.AppendLine($"선택 여부: {(selected ? "YES" : "NO")}");
            builder.AppendLine($"점수: {candidate.Score}");
            builder.AppendLine();
            builder.AppendLine("[병합 라인]");
            AppendNumberedLines(builder, candidate.MergedLines);
            builder.AppendLine();
            builder.AppendLine("[언어별 OCR 결과]");

            foreach (OcrDiagnosticLanguageResult language in candidate.Languages)
            {
                builder.AppendLine();
                builder.AppendLine($"-- {language.LanguageTag} --");
                AppendNumberedLines(builder, language.Lines);
            }

            if (candidate.Languages.Count == 0)
            {
                builder.AppendLine("OCR 결과 없음");
            }

            return builder.ToString();
        }

        /// <summary>
        /// 문자열 목록을 01, 02 형식으로 정리해 StringBuilder에 추가합니다.
        /// </summary>
        private void AppendNumberedLines(StringBuilder builder, System.Collections.Generic.IList<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                builder.AppendLine("없음");
                return;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                builder.AppendLine($"{i + 1:00}. {lines[i]}");
            }
        }

        /// <summary>
        /// PNG byte 배열을 WPF Image.Source에 넣을 수 있는 BitmapImage로 변환합니다.
        /// </summary>
        private ImageSource CreateImageSource(byte[] pngBytes)
        {
            if (pngBytes == null || pngBytes.Length == 0) return null;

            var image = new BitmapImage();
            using (MemoryStream stream = new MemoryStream(pngBytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }
    }
}
