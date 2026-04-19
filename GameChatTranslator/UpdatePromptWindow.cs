using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace GameTranslator
{
    /// <summary>
    /// 업데이트 확인 팝업에서 사용자가 선택한 결과입니다.
    /// Later는 나중에, OpenReleasePage는 릴리즈 페이지 이동, DisableStartupCheck는 자동 확인 비활성화를 뜻합니다.
    /// </summary>
    public enum UpdatePromptResult
    {
        Later,
        OpenReleasePage,
        DisableStartupCheck
    }

    /// <summary>
    /// 새 버전이 발견됐을 때 표시하는 간단한 업데이트 안내 창입니다.
    /// XAML 파일 없이 코드로 UI를 구성해 업데이트 확인 모듈 안에서 독립적으로 사용할 수 있게 했습니다.
    /// </summary>
    public sealed class UpdatePromptWindow : Window
    {
        public UpdatePromptResult Result { get; private set; } = UpdatePromptResult.Later;

        /// <summary>
        /// 업데이트 안내 창을 생성합니다.
        /// <paramref name="currentVersion"/>은 현재 실행 중인 앱 버전,
        /// <paramref name="latestVersion"/>은 GitHub 릴리즈에서 확인한 최신 버전,
        /// <paramref name="allowDisableStartupCheck"/>는 시작 시 자동 확인 비활성화 버튼을 보여줄지 여부입니다.
        /// </summary>
        public UpdatePromptWindow(string currentVersion, string latestVersion, bool allowDisableStartupCheck)
        {
            Title = "업데이트 확인";
            Width = 420;
            Height = allowDisableStartupCheck ? 240 : 210;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.ToolWindow;
            Topmost = true;
            Background = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#1E1E1E"));
            Foreground = WpfBrushes.White;

            var root = new StackPanel
            {
                Margin = new Thickness(20)
            };

            root.Children.Add(new TextBlock
            {
                Text = "새 버전이 있습니다.",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = WpfBrushes.LimeGreen,
                Margin = new Thickness(0, 0, 0, 12)
            });

            root.Children.Add(new TextBlock
            {
                Text = $"현재: {currentVersion}\n최신: {latestVersion}\n\n릴리즈 페이지로 이동하시겠습니까?",
                TextWrapping = TextWrapping.Wrap,
                Foreground = WpfBrushes.White,
                Margin = new Thickness(0, 0, 0, 16)
            });

            var buttons = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            buttons.Children.Add(CreateButton("릴리즈 페이지 열기", () =>
            {
                Result = UpdatePromptResult.OpenReleasePage;
                DialogResult = true;
            }));

            buttons.Children.Add(CreateButton("나중에", () =>
            {
                Result = UpdatePromptResult.Later;
                DialogResult = false;
            }));

            if (allowDisableStartupCheck)
            {
                buttons.Children.Add(CreateButton("다시 묻지 않기", () =>
                {
                    Result = UpdatePromptResult.DisableStartupCheck;
                    DialogResult = false;
                }));
            }

            root.Children.Add(buttons);
            Content = root;
        }

        /// <summary>
        /// 업데이트 팝업 하단의 버튼을 생성합니다.
        /// <paramref name="text"/>는 버튼에 표시할 문구,
        /// <paramref name="onClick"/>은 클릭 시 실행할 동작입니다.
        /// 반환값은 공통 스타일이 적용된 WPF Button입니다.
        /// </summary>
        private WpfButton CreateButton(string text, Action onClick)
        {
            var button = new WpfButton
            {
                Content = text,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#444")),
                Foreground = WpfBrushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            button.Click += (_, _) => onClick();
            return button;
        }
    }
}
