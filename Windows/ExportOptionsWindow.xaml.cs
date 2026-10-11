using System.Windows;
using System.Windows.Input;

namespace Kronix.Windows;

public partial class ExportOptionsWindow : Window
{
    public bool IsXlsx => XlsxRadioButton.IsChecked == true;
    public bool SeparateCustomers => SeparateCustomersCheckBox.IsChecked == true;
    public bool IncludeSummary => IncludeSummaryCheckBox.IsChecked == true;
    public bool UseDecimalHours => DecimalHoursRadioButton.IsChecked == true;

    public ExportOptionsWindow()
    {
        InitializeComponent();
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            DialogResult = e.Key == Key.Enter;
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
