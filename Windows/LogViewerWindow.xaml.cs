using System.Windows;

namespace Kronix.Windows;

public partial class LogViewerWindow : Window
{
    public LogViewerWindow(string path, string contents)
    {
        InitializeComponent();
        PathTextBlock.Text = path;
        LogTextBox.Text = string.IsNullOrEmpty(contents)
            ? "Noch keine Protokolleinträge vorhanden."
            : contents;
        LogTextBox.CaretIndex = LogTextBox.Text.Length;
        LogTextBox.ScrollToEnd();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
