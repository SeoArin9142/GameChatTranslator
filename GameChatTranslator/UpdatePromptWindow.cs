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
    public enum UpdatePromptResult
    {
        Later,
        OpenReleasePage,
        DisableStartupCheck
    }

    public sealed class UpdatePromptWindow : Window
    {
        public UpdatePromptResult Result { get; private set; } = UpdatePromptResult.Later;

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
