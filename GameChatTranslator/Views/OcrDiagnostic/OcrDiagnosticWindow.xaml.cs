using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WpfMessageBox = System.Windows.MessageBox;
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
        private readonly OcrDiagnosticExporter diagnosticExporter = new OcrDiagnosticExporter();
        private readonly string defaultExportDirectory;
        private OcrDiagnosticResult lastDiagnosticResult;
        private OcrTranslationHarnessPreviewResult lastHarnessPreviewResult;

        /// <summary>
        /// OCR 진단 창을 생성합니다.
        /// <paramref name="mainWindow"/>는 현재 캡처 영역과 OCR 전처리/점수화 로직을 보유한 메인 창입니다.
        /// </summary>
        public OcrDiagnosticWindow(MainWindow mainWindow, string defaultExportDirectory = null)
        {
            InitializeComponent();
            this.mainWindow = mainWindow;
            this.defaultExportDirectory = defaultExportDirectory;
            UpdateSummaryHeader(null);

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
            SetResultActionButtonsEnabled(false);
            TxtStatus.Text = "OCR 진단 실행 중...";
            TxtHarnessStatus.Text = "선택 후보 번역 테스트 준비";
            TxtHarnessPreview.Text = "선택 후보를 대상으로 [캐릭터이름]: 내용 형식을 우선 파싱하고, 내용만 현재 번역 엔진으로 테스트합니다.";
            lastHarnessPreviewResult = null;
            UpdateSummaryHeader(null);

            try
            {
                OcrDiagnosticResult result = await mainWindow.RunOcrDiagnosticAsync();
                RenderResult(result);
                lastDiagnosticResult = result;
                SetResultActionButtonsEnabled(true);
                string externalStatus = string.IsNullOrWhiteSpace(result.ExternalOcrStatus)
                    ? ""
                    : $" / {result.ExternalOcrStatus}";
                TxtStatus.Text = $"완료: {result.SelectedCandidateName} 선택 / {result.TotalMs}ms{externalStatus} / 요약·전체 복사 또는 ZIP 저장 가능";
            }
            catch (Exception ex)
            {
                lastDiagnosticResult = null;
                lastHarnessPreviewResult = null;
                TabDiagnostics.Items.Clear();
                TabDiagnostics.Items.Add(CreateTextTab("오류", ex.Message));
                SetResultActionButtonsEnabled(false);
                UpdateSummaryHeader(null);
                TxtStatus.Text = $"실패: {ex.Message}";
            }
            finally
            {
                BtnRunDiagnostic.IsEnabled = true;
            }
        }

        /// <summary>
        /// 현재 화면에 표시된 OCR 진단 결과를 ZIP 파일로 저장합니다.
        /// ZIP에는 요약 텍스트, 원본/확대 이미지, 후보별 전처리/크롭 이미지와 OCR 텍스트가 포함됩니다.
        /// </summary>
        private void BtnSaveDiagnostic_Click(object sender, RoutedEventArgs e)
        {
            if (lastDiagnosticResult == null)
            {
                WpfMessageBox.Show(this, "저장할 OCR 진단 결과가 없습니다. 먼저 진단을 실행해 주세요.", "OCR 진단 결과 저장", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "OCR 진단 결과 저장",
                Filter = "ZIP 파일 (*.zip)|*.zip",
                FileName = diagnosticExporter.CreateDefaultFileName(lastDiagnosticResult.CapturedAt),
                InitialDirectory = ResolveInitialExportDirectory(),
                AddExtension = true,
                DefaultExt = ".zip",
                OverwritePrompt = true
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true) return;

            try
            {
                using FileStream stream = File.Create(dialog.FileName);
                diagnosticExporter.ExportToZip(lastDiagnosticResult, stream);
                TxtStatus.Text = $"저장 완료: {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, $"OCR 진단 결과 저장에 실패했습니다.\n{ex.Message}", "OCR 진단 결과 저장 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtStatus.Text = $"저장 실패: {ex.Message}";
            }
        }

        /// <summary>
        /// OCR 진단 요약만 클립보드에 복사합니다.
        /// 후보별 긴 원문 없이 설정/선택 후보/처리 시간만 빠르게 공유할 때 사용합니다.
        /// </summary>
        private void BtnCopySummary_Click(object sender, RoutedEventArgs e)
        {
            if (lastDiagnosticResult == null)
            {
                WpfMessageBox.Show(this, "복사할 OCR 진단 결과가 없습니다. 먼저 진단을 실행해 주세요.", "OCR 진단 요약 복사", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(diagnosticExporter.BuildSummaryText(lastDiagnosticResult).TrimEnd());
                TxtStatus.Text = "진단 요약을 클립보드에 복사했습니다.";
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, $"OCR 진단 요약 복사에 실패했습니다.\n{ex.Message}", "OCR 진단 요약 복사 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtStatus.Text = $"요약 복사 실패: {ex.Message}";
            }
        }

        private string ResolveInitialExportDirectory()
        {
            if (!string.IsNullOrWhiteSpace(defaultExportDirectory))
            {
                Directory.CreateDirectory(defaultExportDirectory);
                return defaultExportDirectory;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>
        /// 현재 OCR 진단 결과의 요약과 후보별 OCR 텍스트를 클립보드에 복사합니다.
        /// ZIP 파일 저장 없이 이슈나 채팅에 빠르게 붙여넣을 때 사용합니다.
        /// </summary>
        private void BtnCopyDiagnostic_Click(object sender, RoutedEventArgs e)
        {
            if (lastDiagnosticResult == null)
            {
                WpfMessageBox.Show(this, "복사할 OCR 진단 결과가 없습니다. 먼저 진단을 실행해 주세요.", "OCR 진단 결과 복사", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(diagnosticExporter.BuildFullText(lastDiagnosticResult));
                TxtStatus.Text = "진단 전체 텍스트를 클립보드에 복사했습니다.";
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, $"OCR 진단 결과 복사에 실패했습니다.\n{ex.Message}", "OCR 진단 결과 복사 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtStatus.Text = $"복사 실패: {ex.Message}";
            }
        }

        /// <summary>
        /// 현재 선택된 OCR 후보의 병합 라인을 현재 번역 엔진으로 시험 번역합니다.
        /// 채팅 형식은 [캐릭터명]: 접두어를 유지하고 본문만 번역하며, 파싱 실패 라인은 RAW 모드로 처리합니다.
        /// </summary>
        private async void BtnRunHarness_Click(object sender, RoutedEventArgs e)
        {
            if (lastDiagnosticResult == null)
            {
                WpfMessageBox.Show(this, "먼저 OCR 진단을 실행해 주세요.", "번역 테스트", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            OcrDiagnosticCandidate selectedCandidate = lastDiagnosticResult.Candidates
                .FirstOrDefault(candidate => candidate.Name == lastDiagnosticResult.SelectedCandidateName)
                ?? lastDiagnosticResult.Candidates.FirstOrDefault();

            if (selectedCandidate == null)
            {
                WpfMessageBox.Show(this, "번역 테스트에 사용할 OCR 후보가 없습니다.", "번역 테스트", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BtnRunHarness.IsEnabled = false;
            TxtHarnessStatus.Text = "선택 후보 번역 테스트 실행 중...";

            try
            {
                lastHarnessPreviewResult = await mainWindow.RunOcrTranslationHarnessAsync(selectedCandidate.Name, selectedCandidate.MergedLines);
                TxtHarnessPreview.Text = BuildHarnessPreviewText(lastHarnessPreviewResult);
                TxtHarnessStatus.Text = $"번역 테스트 완료: {lastHarnessPreviewResult.TranslatedLineCount}개 번역 / {lastHarnessPreviewResult.SkippedLineCount}개 스킵 / {lastHarnessPreviewResult.TranslateMs}ms";
            }
            catch (Exception ex)
            {
                lastHarnessPreviewResult = null;
                TxtHarnessPreview.Text = ex.Message;
                TxtHarnessStatus.Text = $"번역 테스트 실패: {ex.Message}";
            }
            finally
            {
                BtnRunHarness.IsEnabled = true;
            }
        }

        /// <summary>
        /// OCR 진단 결과 전체를 탭 컨트롤에 표시합니다.
        /// 첫 탭은 요약/원본이고, 이후 탭은 전처리 후보별 결과입니다.
        /// </summary>
        private void RenderResult(OcrDiagnosticResult result)
        {
            TabDiagnostics.Items.Clear();
            UpdateSummaryHeader(result);
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

            var summaryPanel = new DockPanel();
            FrameworkElement comparisonBlock = CreateCandidateComparisonBlock(result);
            DockPanel.SetDock(comparisonBlock, Dock.Top);
            summaryPanel.Children.Add(comparisonBlock);

            WpfTextBox summary = CreateReadOnlyTextBox(BuildSummaryText(result));
            summaryPanel.Children.Add(summary);
            Grid.SetColumn(summaryPanel, 1);
            root.Children.Add(summaryPanel);

            return new TabItem
            {
                Header = "요약",
                Content = root
            };
        }

        /// <summary>
        /// 요약 탭에서 후보별 점수와 결과량을 표 형태로 비교하는 영역을 만듭니다.
        /// ZIP/클립보드 텍스트를 열지 않아도 어떤 후보가 선택됐는지 빠르게 파악할 수 있게 합니다.
        /// </summary>
        private FrameworkElement CreateCandidateComparisonBlock(OcrDiagnosticResult result)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(85, 85, 85)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(MediaColor.FromRgb(37, 37, 37)),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "후보 비교",
                Foreground = MediaBrushes.White,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });

            AddComparisonRow(grid, 0, "후보", "선택", "점수", "결과량", header: true);

            int row = 1;
            foreach (OcrDiagnosticCandidate candidate in result.Candidates)
            {
                bool selected = candidate.Name == result.SelectedCandidateName;
                AddComparisonRow(
                    grid,
                    row++,
                    candidate.Name,
                    selected ? "선택" : "-",
                    candidate.Score.ToString(),
                    $"병합 {candidate.MergedLines.Count} / 언어 {candidate.Languages.Count}",
                    header: false,
                    selectedRow: selected);
            }

            panel.Children.Add(grid);
            border.Child = panel;
            return border;
        }

        private void AddComparisonRow(Grid grid, int rowIndex, string candidate, string selected, string score, string resultCount, bool header, bool selectedRow = false)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddComparisonCell(grid, rowIndex, 0, candidate, header, selectedRow);
            AddComparisonCell(grid, rowIndex, 1, selected, header, selectedRow);
            AddComparisonCell(grid, rowIndex, 2, score, header, selectedRow);
            AddComparisonCell(grid, rowIndex, 3, resultCount, header, selectedRow);
        }

        private void AddComparisonCell(Grid grid, int rowIndex, int columnIndex, string text, bool header, bool selectedRow)
        {
            var block = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(text) ? "-" : text,
                Foreground = header
                    ? new SolidColorBrush(MediaColor.FromRgb(170, 170, 170))
                    : selectedRow
                        ? new SolidColorBrush(MediaColor.FromRgb(143, 227, 136))
                        : new SolidColorBrush(MediaColor.FromRgb(230, 230, 230)),
                FontWeight = header || selectedRow ? FontWeights.Bold : FontWeights.Normal,
                FontSize = header ? 11 : 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(columnIndex == 0 ? 0 : 8, rowIndex == 0 ? 0 : 4, 0, 0)
            };

            Grid.SetRow(block, rowIndex);
            Grid.SetColumn(block, columnIndex);
            grid.Children.Add(block);
        }

        /// <summary>
        /// 전처리 후보 하나의 전처리/크롭 이미지와 OCR 텍스트 결과를 보여주는 탭을 만듭니다.
        /// </summary>
        private TabItem CreateCandidateTab(OcrDiagnosticCandidate candidate, bool selected)
        {
            var root = new DockPanel();
            FrameworkElement overview = CreateCandidateOverview(candidate, selected);
            DockPanel.SetDock(overview, Dock.Top);
            root.Children.Add(overview);

            var contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var imagePanel = new StackPanel();
            imagePanel.Children.Add(CreateImageBlock("전처리 이미지", candidate.PreprocessedPng));
            imagePanel.Children.Add(CreateImageBlock("OCR 입력 크롭 이미지", candidate.CroppedPng));
            Grid.SetColumn(imagePanel, 0);
            contentGrid.Children.Add(imagePanel);

            var detailGrid = new Grid();
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            detailGrid.Children.Add(new TextBlock
            {
                Text = "OCR 결과 / 점수 상세",
                Foreground = MediaBrushes.White,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            WpfTextBox details = CreateReadOnlyTextBox(BuildCandidateText(candidate, selected));
            Grid.SetRow(details, 1);
            detailGrid.Children.Add(details);
            Grid.SetColumn(detailGrid, 1);
            contentGrid.Children.Add(detailGrid);

            root.Children.Add(contentGrid);

            return new TabItem
            {
                Header = selected ? $"★ {candidate.Name} ({candidate.Score})" : $"{candidate.Name} ({candidate.Score})",
                Content = root
            };
        }

        /// <summary>
        /// 선택 후보, 점수, 처리 시간, OCR 호출 수를 창 상단 요약 영역에 표시합니다.
        /// </summary>
        private void UpdateSummaryHeader(OcrDiagnosticResult result)
        {
            if (result == null)
            {
                TxtSelectedCandidate.Text = "-";
                TxtSelectedScore.Text = "-";
                TxtTotalTime.Text = "-";
                TxtOcrCalls.Text = "-";
                return;
            }

            TxtSelectedCandidate.Text = string.IsNullOrWhiteSpace(result.SelectedCandidateName) ? "-" : result.SelectedCandidateName;
            TxtSelectedScore.Text = result.SelectedScore.ToString();
            TxtTotalTime.Text = $"{result.TotalMs}ms";
            TxtOcrCalls.Text = $"{result.OcrCallCount}회";
        }

        /// <summary>
        /// 후보 탭 최상단에 후보명, 선택 여부, 점수, OCR 결과량을 요약해 보여주는 영역을 만듭니다.
        /// </summary>
        private FrameworkElement CreateCandidateOverview(OcrDiagnosticCandidate candidate, bool selected)
        {
            var border = new Border
            {
                BorderBrush = selected
                    ? new SolidColorBrush(MediaColor.FromRgb(143, 227, 136))
                    : new SolidColorBrush(MediaColor.FromRgb(85, 85, 85)),
                BorderThickness = new Thickness(1),
                Background = selected
                    ? new SolidColorBrush(MediaColor.FromRgb(36, 54, 38))
                    : new SolidColorBrush(MediaColor.FromRgb(37, 37, 37)),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(CreateMetricBlock("후보", candidate.Name, selected ? "#8FE388" : "#FFFFFF", 0));
            grid.Children.Add(CreateMetricBlock("선택 여부", selected ? "선택됨" : "미선택", selected ? "#8FE388" : "#CCCCCC", 1));
            grid.Children.Add(CreateMetricBlock("점수", candidate.Score.ToString(), "#FFD966", 2));
            grid.Children.Add(CreateMetricBlock("결과량", $"병합 {candidate.MergedLines.Count}줄 / 언어 {candidate.Languages.Count}개", "#9CDCFE", 3));

            border.Child = grid;
            return border;
        }

        /// <summary>
        /// 요약 영역에서 사용할 작은 제목/값 묶음을 만듭니다.
        /// </summary>
        private FrameworkElement CreateMetricBlock(string label, string value, string valueColor, int column)
        {
            var panel = new StackPanel { Margin = column == 0 ? new Thickness(0) : new Thickness(12, 0, 0, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(170, 170, 170)),
                FontSize = 11
            });
            panel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
                Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom(valueColor),
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap
            });

            Grid.SetColumn(panel, column);
            return panel;
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
            return diagnosticExporter.BuildSummaryText(result);
        }

        /// <summary>
        /// 후보별 병합 라인과 언어별 OCR 원문을 텍스트로 정리합니다.
        /// </summary>
        private string BuildCandidateText(OcrDiagnosticCandidate candidate, bool selected)
        {
            return diagnosticExporter.BuildCandidateText(candidate, selected);
        }

        private void SetResultActionButtonsEnabled(bool enabled)
        {
            BtnRunHarness.IsEnabled = enabled;
            BtnCopySummary.IsEnabled = enabled;
            BtnCopyDiagnostic.IsEnabled = enabled;
            BtnSaveDiagnostic.IsEnabled = enabled;
        }

        private string BuildHarnessPreviewText(OcrTranslationHarnessPreviewResult result)
        {
            if (result == null)
            {
                return "선택 후보 번역 테스트 결과가 없습니다.";
            }

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("[선택 후보 번역 테스트]");
            builder.AppendLine($"후보: {EmptyToDash(result.CandidateName)}");
            builder.AppendLine($"전체 라인: {result.TotalLineCount}");
            builder.AppendLine($"번역 완료: {result.TranslatedLineCount}");
            builder.AppendLine($"스킵: {result.SkippedLineCount}");
            builder.AppendLine($"처리 시간: {result.TranslateMs}ms");

            foreach (OcrTranslationHarnessPreviewLine line in result.Lines)
            {
                builder.AppendLine();
                builder.AppendLine($"{line.Index:00}. 상태: {EmptyToDash(line.Status)}");
                builder.AppendLine($"OCR 원문: {EmptyToDash(line.RawText)}");

                if (!string.IsNullOrWhiteSpace(line.Prefix))
                {
                    builder.AppendLine($"출력 접두어: {line.Prefix}");
                }

                if (!string.IsNullOrWhiteSpace(line.OriginalContent))
                {
                    builder.AppendLine($"원문 본문: {line.OriginalContent}");
                }

                if (!string.IsNullOrWhiteSpace(line.PreparedContent))
                {
                    builder.AppendLine($"번역 입력: {line.PreparedContent}");
                }

                if (line.Translated)
                {
                    builder.AppendLine($"번역 결과: {line.Prefix}{line.TranslatedText}");
                    builder.AppendLine($"엔진: {EmptyToDash(line.EngineName)}");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static string EmptyToDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
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
