using System.Windows.Controls;
using Kronix.Database;

namespace Kronix.Windows;

public partial class KundenWindow : UserControl
{
    public KundenWindow(DatabaseHelper dbHelper)
    {
        InitializeComponent();
    }
}