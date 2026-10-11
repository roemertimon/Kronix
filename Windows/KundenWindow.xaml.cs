using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Kronix.Database;

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
        }
        catch (System.Exception ex)
        {
            var error = new AppMessageWindow("Kunde nicht gespeichert", ex.Message)
            {
                Owner = Window.GetWindow(this)
            };
            error.ShowDialog();
        }
    }
}