using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Kronix.Database;
using Kronix.Logging;

namespace Kronix.Windows;

public partial class KundenWindow : UserControl
{
    private readonly DatabaseHelper _dbHelper;
    private readonly ObservableCollection<DatabaseHelper.Customer> _customers = new();
    private readonly ICollectionView _customersView;

    public KundenWindow(DatabaseHelper dbHelper)
    {
        InitializeComponent();
        _dbHelper = dbHelper;
        _customersView = CollectionViewSource.GetDefaultView(_customers);
        _customersView.Filter = FilterCustomer;
        CustomersDataGrid.ItemsSource = _customersView;
        LoadCustomers();
    }

    private void LoadCustomers()
    {
        _customers.Clear();
        foreach (DatabaseHelper.Customer customer in _dbHelper.GetAllCustomers())
        {
            _customers.Add(customer);
        }

        _customersView.Refresh();
        UpdateCustomerCount();
    }

    private bool FilterCustomer(object item)
    {
        if (item is not DatabaseHelper.Customer customer)
        {
            return false;
        }

        string search = SearchTextBox.Text.Trim();
        if (search.Length == 0)
        {
            return true;
        }

        return new[]
        {
            customer.ClientNumber, customer.FirstName, customer.LastName, customer.Company,
            customer.Street, customer.PostalCode, customer.City, customer.PhoneNumber
        }.Any(value => value.Contains(search, System.StringComparison.CurrentCultureIgnoreCase));
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholderTextBlock.Visibility = string.IsNullOrEmpty(SearchTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _customersView.Refresh();
        UpdateCustomerCount();
    }

    private void UpdateCustomerCount()
    {
        int count = _customersView.Cast<DatabaseHelper.Customer>().Count();
        CustomerCountTextBlock.Text = count == 1 ? "1 Kunde" : $"{count} Kunden";
    }

    private void AddCustomerButton_Click(object sender, RoutedEventArgs e)
    {
        ShowCustomerEditor(null);
    }

    private void EditCustomerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is DatabaseHelper.Customer customer)
        {
            ShowCustomerEditor(customer);
        }
    }

    private void CustomersDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CustomersDataGrid.SelectedItem is DatabaseHelper.Customer customer)
        {
            ShowCustomerEditor(customer);
        }
    }

    private void CustomersDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CustomersDataGrid.SelectedItem is not DatabaseHelper.Customer customer)
        {
            return;
        }

        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            DeleteCustomer(customer);
        }
        else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            ShowCustomerEditor(customer);
        }
    }

    private void DeleteCustomerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not DatabaseHelper.Customer customer)
        {
            return;
        }

        DeleteCustomer(customer);
    }

    private void DeleteCustomer(DatabaseHelper.Customer customer)
    {
        try
        {
            int timeLogCount = _dbHelper.GetTimeLogCountForCustomer(customer.ClientNumber);
            if (timeLogCount > 0)
            {
                AppLogger.Warning("customers.delete_blocked",
                    "Customer deletion was blocked because time entries are still associated.");
                string entryLabel = timeLogCount == 1 ? "Zeiteintrag" : "Zeiteinträge";
                var blockedDialog = new AppMessageWindow(
                    "Kunde kann nicht gelöscht werden",
                    $"Dem Kunden {customer.ClientNumber} sind {timeLogCount} {entryLabel} zugeordnet. Ein Kunde kann nur gelöscht werden, wenn keine Zeiteinträge mehr vorhanden sind. Löschen Sie zuerst alle zugehörigen Zeiteinträge.",
                    isError: false)
                {
                    Owner = Window.GetWindow(this)
                };
                blockedDialog.ShowDialog();
                return;
            }

            var confirmation = new DeleteConfirmationWindow(customer.ClientNumber)
            {
                Owner = Window.GetWindow(this)
            };
            if (confirmation.ShowDialog() != true)
            {
                return;
            }

            _dbHelper.DeleteCustomer(customer.ClientNumber);
            _customers.Remove(customer);
            _customersView.Refresh();
            UpdateCustomerCount();
            AppLogger.Information("customers.deleted", "Deleted a customer record.");
        }
        catch (System.Exception ex)
        {
            AppLogger.Error("customers.delete_failed", "Could not delete a customer record.", ex);
            var error = new AppMessageWindow("Kunde nicht gelöscht", ex.Message)
            {
                Owner = Window.GetWindow(this)
            };
            error.ShowDialog();
        }
    }

    private void ShowCustomerEditor(DatabaseHelper.Customer? customer)
    {
        var editor = new CustomerEditorWindow(customer)
        {
            Owner = Window.GetWindow(this)
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _dbHelper.SaveCustomer(editor.Customer, editor.OriginalClientNumber);
            LoadCustomers();
            AppLogger.Information(editor.OriginalClientNumber is null ? "customers.created" : "customers.updated",
                editor.OriginalClientNumber is null ? "Created a customer record." : "Updated a customer record.");
        }
        catch (System.Exception ex)
        {
            AppLogger.Error("customers.save_failed", "Could not save a customer record.", ex);
            var error = new AppMessageWindow("Kunde nicht gespeichert", ex.Message)
            {
                Owner = Window.GetWindow(this)
            };
            error.ShowDialog();
        }
    }
}