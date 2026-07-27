using System.Windows;
using System.Windows.Controls;
using CableGuard.ControlCenter.ViewModels;

namespace CableGuard.ControlCenter;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box && DataContext is MainViewModel { Logs.LiveTail: true })
            box.ScrollToEnd();
    }
}
