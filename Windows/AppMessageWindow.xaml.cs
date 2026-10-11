using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Kronix.Windows;

public partial class AppMessageWindow : Window
{
    public AppMessageWindow(string title, string message, bool isError = true)
    {
        InitializeComponent();
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;

        if (!isError)
        {
            IconBackground.Background = new SolidColorBrush(Color.FromRgb(232, 244, 255));
            IconTextBlock.Text = "\uE946";
            IconTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(23, 105, 170));
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            DialogResult = true;
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
