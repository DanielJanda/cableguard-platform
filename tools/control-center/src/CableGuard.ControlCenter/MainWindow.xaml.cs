using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private void RoiCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel main) return;
        var pos = e.GetPosition((IInputElement)sender);
        // Map click into nominal 1280x720 calibration space.
        if (sender is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
        {
            var x = (int)(pos.X / fe.ActualWidth * 1280);
            var y = (int)(pos.Y / fe.ActualHeight * 720);
            main.Calibration.AddPoint(new System.Windows.Point(x, y));
        }
    }
}
