using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kronix.Database;
using Kronix.Logging;

namespace Kronix.Windows;

public partial class CustomerEditorWindow : Window
{
    private readonly string? _originalClientNumber;

    public DatabaseHelper.Customer Customer { get; private set; } = new();

    public CustomerEditorWindow(DatabaseHelper.Customer? customer = null, string? requestedClientNumber = null,
        IEnumerable<DatabaseHelper.Customer>? templates = null)
    {
        InitializeComponent();
        if (customer is null)
        {
            if (templates is not null)
            {
                foreach (var template in templates)
                {
                    TemplateComboBox.Items.Add(new ComboBoxItem { Content = DescribeTemplate(template), Tag = template });
                }

                TemplatePanel.Visibility = TemplateComboBox.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            SubtitleTextBlock.Text = requestedClientNumber is null
                ? "Kundendaten für Kronix speichern"
                : $"Kundennummer {requestedClientNumber} ist noch nicht gespeichert. Ergänzen Sie die Daten, um den Kunden anzulegen.";
            ClientNumberTextBox.Text = requestedClientNumber ?? string.Empty;
            ClientNumberTextBox.IsEnabled = requestedClientNumber is null;
            return;
        }

        TitleTextBlock.Text = "Kundendaten bearbeiten";
        SubtitleTextBlock.Text = "Änderungen an der Kundennummer werden auch in Zeiteinträgen übernommen.";
        _originalClientNumber = customer.ClientNumber;
        ClientNumberTextBox.Text = customer.ClientNumber;
        FirstNameTextBox.Text = customer.FirstName;
        LastNameTextBox.Text = customer.LastName;
        CompanyTextBox.Text = customer.Company;
        StreetTextBox.Text = customer.Street;
        PostalCodeTextBox.Text = customer.PostalCode;
        CityTextBox.Text = customer.City;
        PhoneNumberTextBox.Text = customer.PhoneNumber;
        HourlyRateTextBox.Text = customer.HourlyRate?.ToString("0.00", CultureInfo.GetCultureInfo("de-DE")) ?? string.Empty;
    }

    public string? OriginalClientNumber => _originalClientNumber;

    private static string DescribeTemplate(DatabaseHelper.Customer customer)
    {
        string name = $"{customer.FirstName} {customer.LastName}".Trim();
        string label = string.Join(" · ", new[] { customer.Company, name }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return label.Length == 0 ? customer.ClientNumber : $"{label} ({customer.ClientNumber})";
    }

    private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateComboBox.SelectedItem is not ComboBoxItem { Tag: DatabaseHelper.Customer template })
        {
            return;
        }

        FirstNameTextBox.Text = template.FirstName;
        LastNameTextBox.Text = template.LastName;
        CompanyTextBox.Text = template.Company;
        StreetTextBox.Text = template.Street;
        PostalCodeTextBox.Text = template.PostalCode;
        CityTextBox.Text = template.City;
        PhoneNumberTextBox.Text = template.PhoneNumber;
        HourlyRateTextBox.Text = template.HourlyRate?.ToString("0.00", CultureInfo.GetCultureInfo("de-DE")) ?? string.Empty;
        if (ClientNumberTextBox.IsEnabled)
        {
            ClientNumberTextBox.Focus();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string clientNumber = ClientNumberTextBox.Text.Trim();
        if (clientNumber.Length == 0)
        {
            ShowValidationError("Bitte geben Sie eine Kundennummer ein.");
            return;
        }

        decimal? hourlyRate = null;
        string hourlyRateText = HourlyRateTextBox.Text.Trim();
        if (hourlyRateText.Length > 0)
        {
            bool parsed = decimal.TryParse(hourlyRateText, NumberStyles.Number,
                CultureInfo.GetCultureInfo("de-DE"), out decimal rate) ||
                decimal.TryParse(hourlyRateText, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out rate);
            if (!parsed || rate < 0)
            {
                ShowValidationError("Bitte geben Sie einen gültigen, nicht negativen Stundensatz in Euro ein.");
                return;
            }

            hourlyRate = rate;
        }

        Customer = new DatabaseHelper.Customer
        {
            ClientNumber = clientNumber,
            FirstName = FirstNameTextBox.Text.Trim(),
            LastName = LastNameTextBox.Text.Trim(),
            Company = CompanyTextBox.Text.Trim(),
            Street = StreetTextBox.Text.Trim(),
            PostalCode = PostalCodeTextBox.Text.Trim(),
            City = CityTextBox.Text.Trim(),
            PhoneNumber = PhoneNumberTextBox.Text.Trim(),
            HourlyRate = hourlyRate
        };
        DialogResult = true;
    }

    private void ShowValidationError(string message)
    {
        AppLogger.Warning("validation.customer", "A customer form submission failed validation.");
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            SaveButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
