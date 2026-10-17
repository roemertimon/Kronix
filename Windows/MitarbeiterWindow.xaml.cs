using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kronix.Database;
using Kronix.Logging;

namespace Kronix.Windows;

public partial class MitarbeiterWindow : UserControl
{
    private readonly DatabaseHelper _dbHelper;
    private readonly ObservableCollection<DatabaseHelper.Employee> _employees = new();
    private bool _loadingEmployees;

    public MitarbeiterWindow(DatabaseHelper dbHelper)
    {
        InitializeComponent();
        _dbHelper = dbHelper;
        EmployeesDataGrid.ItemsSource = _employees;
        LoadEmployees();
    }

    private void LoadEmployees()
    {
        _loadingEmployees = true;
        try
        {
            _employees.Clear();
            foreach (DatabaseHelper.Employee employee in _dbHelper.GetAllEmployees())
            {
                _employees.Add(employee);
            }

            string? currentNumber = _dbHelper.GetCurrentEmployeeNumber();
            CurrentEmployeeComboBox.ItemsSource = _employees;
            CurrentEmployeeComboBox.SelectedItem = _employees.FirstOrDefault(employee =>
                string.Equals(employee.EmployeeNumber, currentNumber,
                    System.StringComparison.OrdinalIgnoreCase));
            UpdateCurrentEmployeeDescription();
            EmployeeCountTextBlock.Text = _employees.Count == 1
                ? "1 Mitarbeiter"
                : $"{_employees.Count} Mitarbeiter";
        }
        finally
        {
            _loadingEmployees = false;
        }
    }

    private void UpdateCurrentEmployeeDescription()
    {
        CurrentEmployeeDescriptionTextBlock.Text =
            CurrentEmployeeComboBox.SelectedItem is DatabaseHelper.Employee employee
                ? $"Aktuell: {employee.EmployeeNumber} · {employee.DisplayName} (für neue Zeiteinträge)"
                : "Kein aktueller Mitarbeiter ausgewählt";
    }

    private void CurrentEmployeeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEmployees)
        {
            return;
        }

        try
        {
            DatabaseHelper.Employee? employee = CurrentEmployeeComboBox.SelectedItem as DatabaseHelper.Employee;
            _dbHelper.SetCurrentEmployeeNumber(employee?.EmployeeNumber);
            UpdateCurrentEmployeeDescription();
            AppLogger.Information("employees.current_changed",
                employee is null
                    ? "Cleared the current employee selection."
                    : "Changed the current employee selection.");
        }
        catch (System.Exception ex)
        {
            AppLogger.Error("employees.current_change_failed",
                "Could not change the current employee selection.", ex);
            ShowError("Aktueller Mitarbeiter nicht geändert", ex.Message);
            LoadEmployees();
        }
    }

    private void ClearCurrentEmployeeButton_Click(object sender, RoutedEventArgs e)
    {
        CurrentEmployeeComboBox.SelectedItem = null;
    }

    private void AddEmployeeButton_Click(object sender, RoutedEventArgs e)
    {
        ShowEmployeeEditor(null);
    }

    private void EditEmployeeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is DatabaseHelper.Employee employee)
        {
            ShowEmployeeEditor(employee);
        }
    }

    private void EmployeesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EmployeesDataGrid.SelectedItem is DatabaseHelper.Employee employee)
        {
            ShowEmployeeEditor(employee);
        }
    }

    private void ShowEmployeeEditor(DatabaseHelper.Employee? employee)
    {
        string? currentNumber = _dbHelper.GetCurrentEmployeeNumber();
        var editor = new EmployeeEditorWindow(employee,
            employee is not null && string.Equals(employee.EmployeeNumber, currentNumber,
                System.StringComparison.OrdinalIgnoreCase))
        {
            Owner = Window.GetWindow(this)
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _dbHelper.SaveEmployee(editor.Employee, editor.OriginalEmployeeNumber, editor.MakeCurrent);
            LoadEmployees();
            AppLogger.Information(editor.OriginalEmployeeNumber is null
                    ? "employees.created"
                    : "employees.updated",
                editor.OriginalEmployeeNumber is null
                    ? "Created an employee record."
                    : "Updated an employee record.");
        }
        catch (System.Exception ex)
        {
            AppLogger.Error("employees.save_failed", "Could not save an employee record.", ex);
            ShowError("Mitarbeiter nicht gespeichert", ex.Message);
        }
    }

    private void DeleteEmployeeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not DatabaseHelper.Employee employee)
        {
            return;
        }

        var confirmation = new DeleteConfirmationWindow(employee)
        {
            Owner = Window.GetWindow(this)
        };
        if (confirmation.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _dbHelper.DeleteEmployee(employee.EmployeeNumber);
            LoadEmployees();
            AppLogger.Information("employees.deleted", "Deleted an employee record.");
        }
        catch (System.Exception ex)
        {
            AppLogger.Error("employees.delete_failed", "Could not delete an employee record.", ex);
            ShowError("Mitarbeiter nicht gelöscht", ex.Message);
        }
    }

    private void ShowError(string title, string message)
    {
        var dialog = new AppMessageWindow(title, message)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }
}
