using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Kronix.Windows;

public partial class KronixToastWindow : Window
{
    private readonly DispatcherTimer _closeTimer;

    public KronixToastWindow(string title, string message, bool isSuccess)
    {
        InitializeComponent();
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;

        Color accent = isSuccess
            ? Color.FromRgb(8, 127, 91)
            : Color.FromRgb(32, 54, 79);
        Color tint = isSuccess
            ? Color.FromRgb(231, 244, 238)
            : Color.FromRgb(234, 240, 246);
        AccentBorder.Background = new SolidColorBrush(accent);
        IconBorder.Background = new SolidColorBrush(tint);
        IconTextBlock.Foreground = new SolidColorBrush(accent);
        IconTextBlock.Text = isSuccess ? "\uE73E" : "\uE946";

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _closeTimer.Tick += CloseTimer_Tick;
        Loaded += KronixToastWindow_Loaded;
        Closed += (_, _) => _closeTimer.Stop();
    }

    private void KronixToastWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 18;
        Top = workArea.Bottom - ActualHeight - 18;
        _closeTimer.Start();
    }

    private void CloseTimer_Tick(object? sender, EventArgs e)
    {
        _closeTimer.Stop();
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
