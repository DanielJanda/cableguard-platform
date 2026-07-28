using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CableGuard.ControlCenter.ViewModels;

namespace CableGuard.ControlCenter;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _vm = viewModel;
        viewModel.Calibration.PointsChanged += (_, _) => RedrawRoiOverlay();
        viewModel.Calibration.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CalibrationViewModel.FrameImage)
                or nameof(CalibrationViewModel.FrameWidth)
                or nameof(CalibrationViewModel.FrameHeight))
                RedrawRoiOverlay();
        };
        Loaded += (_, _) => RedrawRoiOverlay();
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box && DataContext is MainViewModel { Logs.LiveTail: true })
            box.ScrollToEnd();
    }

    private void RoiCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawRoiOverlay();

    private void RoiCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null) return;
        var cal = _vm.Calibration;
        var pos = e.GetPosition(RoiCanvas);
        if (RoiCanvas.ActualWidth <= 1 || RoiCanvas.ActualHeight <= 1) return;

        // Map click from displayed canvas into native frame pixel space.
        var (ox, oy, dw, dh) = GetImageDrawRect();
        if (pos.X < ox || pos.Y < oy || pos.X > ox + dw || pos.Y > oy + dh)
            return; // click outside letterboxed image

        var x = (int)((pos.X - ox) / dw * cal.FrameWidth);
        var y = (int)((pos.Y - oy) / dh * cal.FrameHeight);
        x = Math.Clamp(x, 0, Math.Max(0, cal.FrameWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, cal.FrameHeight - 1));
        cal.AddPoint(new Point(x, y));
    }

    private void RedrawRoiOverlay()
    {
        if (_vm is null || RoiCanvas is null) return;
        RoiCanvas.Children.Clear();
        var cal = _vm.Calibration;
        if (cal.Points.Count == 0) return;

        var (ox, oy, dw, dh) = GetImageDrawRect();
        if (dw < 1 || dh < 1) return;

        double Sx(int px) => ox + px / (double)Math.Max(1, cal.FrameWidth) * dw;
        double Sy(int py) => oy + py / (double)Math.Max(1, cal.FrameHeight) * dh;

        var poly = new Polygon
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xCC, 0x00)),
        };
        foreach (var p in cal.Points)
            poly.Points.Add(new Point(Sx(p.X), Sy(p.Y)));
        RoiCanvas.Children.Add(poly);

        for (var i = 0; i < cal.Points.Count; i++)
        {
            var p = cal.Points[i];
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.Lime,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
            };
            Canvas.SetLeft(dot, Sx(p.X) - 5);
            Canvas.SetTop(dot, Sy(p.Y) - 5);
            RoiCanvas.Children.Add(dot);

            var label = new TextBlock
            {
                Text = (i + 1).ToString(),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)),
                FontSize = 11,
                Padding = new Thickness(2, 0, 2, 0),
            };
            Canvas.SetLeft(label, Sx(p.X) + 6);
            Canvas.SetTop(label, Sy(p.Y) - 8);
            RoiCanvas.Children.Add(label);
        }
    }

    /// <summary>Letterbox rect of Uniform-stretched image inside the canvas.</summary>
    private (double ox, double oy, double dw, double dh) GetImageDrawRect()
    {
        var cw = RoiCanvas.ActualWidth;
        var ch = RoiCanvas.ActualHeight;
        if (_vm is null || cw < 1 || ch < 1)
            return (0, 0, cw, ch);

        var fw = Math.Max(1, _vm.Calibration.FrameWidth);
        var fh = Math.Max(1, _vm.Calibration.FrameHeight);
        var scale = Math.Min(cw / fw, ch / fh);
        var dw = fw * scale;
        var dh = fh * scale;
        var ox = (cw - dw) / 2;
        var oy = (ch - dh) / 2;
        return (ox, oy, dw, dh);
    }
}
