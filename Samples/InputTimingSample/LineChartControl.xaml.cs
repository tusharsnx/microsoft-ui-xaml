using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace InputTimingSample;

public sealed partial class LineChartControl : UserControl
{
    private List<TimingRecord> _currentData = new();
    private const int MaxPoints = 1000; // Plot up to 1000 real-time events

    private readonly SolidColorBrush _dispatchBrush = new(Windows.UI.Color.FromArgb(255, 59, 130, 246));
    private readonly SolidColorBrush _processingBrush = new(Windows.UI.Color.FromArgb(255, 16, 185, 129));
    private readonly SolidColorBrush _renderBrush = new(Windows.UI.Color.FromArgb(255, 245, 158, 11));
    private readonly SolidColorBrush _totalBrush = new(Windows.UI.Color.FromArgb(255, 139, 92, 246));
    private readonly SolidColorBrush _gridBrush = new(Windows.UI.Color.FromArgb(50, 128, 128, 128));
    private readonly SolidColorBrush _axisTextBrush = new(Windows.UI.Color.FromArgb(180, 128, 128, 128));
    private readonly SolidColorBrush _cursorBrush = new(Windows.UI.Color.FromArgb(200, 160, 160, 160));

    private Line? _cursorLine;

    public LineChartControl()
    {
        this.InitializeComponent();
    }

    public void UpdateData(IEnumerable<TimingRecord> records)
    {
        // Display full recording chronologically (oldest to newest)
        _currentData = records.ToList();
        RedrawChart();
    }

    private void OnChartSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawChart();
    }

    private void OnSeriesToggleChanged(object sender, RoutedEventArgs e)
    {
        RedrawChart();
    }

    private void RedrawChart()
    {
        if (PlotCanvas == null) return;
        PlotCanvas.Children.Clear();
        _cursorLine = null;

        double width = PlotCanvas.ActualWidth;
        double height = PlotCanvas.ActualHeight;

        if (width <= 10 || height <= 10 || _currentData.Count == 0)
        {
            ChartTitleBlock.Text = "Real-Time Latency Stream (0 events)";
            return;
        }

        var latest = _currentData.Last();
        ChartTitleBlock.Text = $"Real-Time Event Stream ({_currentData.Count} events) | Latest #{latest.Id} ({latest.EventType}): TextBox = {latest.TextBoxProcessing}, Total = {latest.TotalLatency}";

        bool showDispatch = DispatchToggle?.IsChecked ?? true;
        bool showProcessing = ProcessingToggle?.IsChecked ?? true;
        bool showRender = RenderToggle?.IsChecked ?? true;
        bool showTotal = TotalToggle?.IsChecked ?? true;

        // Calculate dynamic maximum value for Y-axis from actual real-time values
        double maxValue = 1.0;
        foreach (var r in _currentData)
        {
            if (showDispatch) maxValue = Math.Max(maxValue, r.DispatchDelayMs);
            if (showProcessing) maxValue = Math.Max(maxValue, r.TextBoxProcessingMs);
            if (showRender) maxValue = Math.Max(maxValue, r.RenderDelayMs);
            if (showTotal) maxValue = Math.Max(maxValue, r.TotalLatencyMs);
        }

        // Round Y max up nicely with a small headroom
        maxValue = Math.Ceiling(maxValue * 1.15);
        if (maxValue < 2.0) maxValue = 2.0;

        // Draw horizontal gridlines (4 intervals)
        int gridDivisions = 4;
        for (int i = 0; i <= gridDivisions; i++)
        {
            double ratio = (double)i / gridDivisions;
            double y = height - (ratio * height);
            double val = ratio * maxValue;

            // Gridline
            var line = new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = y,
                Y2 = y,
                Stroke = _gridBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            };
            PlotCanvas.Children.Add(line);

            // Y-axis label on left
            var label = new TextBlock
            {
                Text = $"{val:F1}ms",
                FontSize = 10,
                Foreground = _axisTextBrush
            };
            Canvas.SetLeft(label, -38);
            Canvas.SetTop(label, y - 7);
            PlotCanvas.Children.Add(label);
        }

        // Draw X-axis bottom labels
        var xLabelStart = new TextBlock
        {
            Text = $"#{_currentData.First().Id} ({_currentData.First().EventType})",
            FontSize = 10,
            Foreground = _axisTextBrush
        };
        Canvas.SetLeft(xLabelStart, 0);
        Canvas.SetTop(xLabelStart, height + 4);
        PlotCanvas.Children.Add(xLabelStart);

        var xLabelEnd = new TextBlock
        {
            Text = $"Latest #{latest.Id} ({latest.EventType})",
            FontSize = 10,
            Foreground = _axisTextBrush
        };
        Canvas.SetLeft(xLabelEnd, Math.Max(0, width - 110));
        Canvas.SetTop(xLabelEnd, height + 4);
        PlotCanvas.Children.Add(xLabelEnd);

        int count = _currentData.Count;
        double stepX = count > 1 ? width / (count - 1) : width;

        // Build real-time series polylines
        Polyline? dispatchLine = showDispatch ? CreatePolyline(_dispatchBrush, 2) : null;
        Polyline? processingLine = showProcessing ? CreatePolyline(_processingBrush, 2) : null;
        Polyline? renderLine = showRender ? CreatePolyline(_renderBrush, 2) : null;
        Polyline? totalLine = showTotal ? CreatePolyline(_totalBrush, 2.5) : null;

        for (int i = 0; i < count; i++)
        {
            var r = _currentData[i];
            double x = count > 1 ? i * stepX : width / 2;

            if (dispatchLine != null)
            {
                double y = height - (Math.Clamp(r.DispatchDelayMs / maxValue, 0, 1) * height);
                dispatchLine.Points.Add(new Point(x, y));
            }

            if (processingLine != null)
            {
                double y = height - (Math.Clamp(r.TextBoxProcessingMs / maxValue, 0, 1) * height);
                processingLine.Points.Add(new Point(x, y));
            }

            if (renderLine != null)
            {
                double y = height - (Math.Clamp(r.RenderDelayMs / maxValue, 0, 1) * height);
                renderLine.Points.Add(new Point(x, y));
            }

            if (totalLine != null)
            {
                double y = height - (Math.Clamp(r.TotalLatencyMs / maxValue, 0, 1) * height);
                totalLine.Points.Add(new Point(x, y));
            }
        }

        // Add to canvas in render order
        if (dispatchLine != null) PlotCanvas.Children.Add(dispatchLine);
        if (processingLine != null) PlotCanvas.Children.Add(processingLine);
        if (renderLine != null) PlotCanvas.Children.Add(renderLine);
        if (totalLine != null) PlotCanvas.Children.Add(totalLine);

        // Hover cursor line
        _cursorLine = new Line
        {
            Y1 = 0,
            Y2 = height,
            Stroke = _cursorBrush,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            Visibility = Visibility.Collapsed
        };
        PlotCanvas.Children.Add(_cursorLine);
    }

    private static Polyline CreatePolyline(Brush brush, double thickness)
    {
        return new Polyline
        {
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            Points = new PointCollection()
        };
    }

    private void OnPlotPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_currentData.Count == 0 || _cursorLine == null) return;

        double width = PlotCanvas.ActualWidth;
        Point pos = e.GetCurrentPoint(PlotCanvas).Position;

        int count = _currentData.Count;
        double stepX = count > 1 ? width / (count - 1) : width;
        int nearestIndex = Math.Clamp((int)Math.Round(pos.X / stepX), 0, count - 1);

        double cursorX = count > 1 ? nearestIndex * stepX : width / 2;
        _cursorLine.X1 = cursorX;
        _cursorLine.X2 = cursorX;
        _cursorLine.Visibility = Visibility.Visible;

        var item = _currentData[nearestIndex];
        HoverInfoText.Text = $"Event #{item.Id} [{item.EventType} - {item.Details}] @ {item.FormattedTime} ({item.FormattedElapsed}) -> Dispatch={item.DispatchDelay} | TextBox={item.TextBoxProcessing} | RenderWait={item.RenderDelay} | Total={item.TotalLatency}";
    }

    private void OnPlotPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_cursorLine != null) _cursorLine.Visibility = Visibility.Collapsed;
        HoverInfoText.Text = "Hover over the chart to inspect individual event timings";
    }
}
