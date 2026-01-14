using System.Windows.Controls;
using Kronix.Database;

namespace Kronix.Windows;

public partial class DatenWindow : UserControl
{
    public DatenWindow(DatabaseHelper dbHelper)
    {
        InitializeComponent();
    }
}