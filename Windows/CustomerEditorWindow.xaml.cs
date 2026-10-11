using System.Windows;
using System.Windows.Input;
using Kronix.Database;

namespace Kronix.Windows;

public partial class CustomerEditorWindow : Window
{
    private readonly string? _originalClientNumber;

    public DatabaseHelper.Customer Customer { get; private set; } = new();

    public CustomerEditorWindow(DatabaseHelper.Customer? customer = null, string? requestedClientNumber = null)
    {
        InitializeComponent();
        if (customer is null)
        {
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
    }

    public string? OriginalClientNumber => _originalClientNumber;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string clientNumber = ClientNumberTextBox.Text.Trim();
        if (clientNumber.Length == 0)
        {
            ShowValidationError("Bitte geben Sie eine Kundennummer ein.");
            return;
        }
        if (!int.TryParse(clientNumber, out _))
        {
            ShowValidationError("Die Kundennummer muss wie bei der Zeiterfassung eine Zahl sein.");
            return;
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
            PhoneNumber = PhoneNumberTextBox.Text.Trim()
        };
        DialogResult = true;
    }

    private void ShowValidationError(string message)
    {
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
