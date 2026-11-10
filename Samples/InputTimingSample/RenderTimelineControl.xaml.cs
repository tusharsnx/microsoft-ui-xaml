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

public sealed partial class RenderTimelineControl : UserControl
{
    private List<RenderTimingRecord> _allRecords = new();
    private List<RenderTimingRecord> _displayedRecords = new();

    // Category Color Palette
    private readonly SolidColorBrush _compositionRenderingBrush = new(Windows.UI.Color.FromArgb(255, 168, 85, 247)); // Violet (#A855F7)
    private readonly SolidColorBrush _compositionRenderedBrush = new(Windows.UI.Color.FromArgb(255, 139, 92, 246));  // Purple (#8B5CF6)
    private readonly SolidColorBrush _layoutSizeBrush = new(Windows.UI.Color.FromArgb(255, 59, 130, 246));           // Blue (#3B82F6)
    private readonly SolidColorBrush _layoutUpdatedBrush = new(Windows.UI.Color.FromArgb(255, 6, 182, 212));         // Cyan (#06B6D4)
    private readonly SolidColorBrush _viewportBringBrush = new(Windows.UI.Color.FromArgb(255, 20, 184, 166));        // Teal (#14B8A6)
    private readonly SolidColorBrush _viewportEffectiveBrush = new(Windows.UI.Color.FromArgb(255, 99, 102, 241));    // Indigo (#6366F1)
    private readonly SolidColorBrush _contentTextBrush = new(Windows.UI.Color.FromArgb(255, 16, 185, 129));          // Emerald (#10B981)
    private readonly SolidColorBrush _contentSelectionBrush = new(Windows.UI.Color.FromArgb(255, 45, 212, 191));     // Mint (#2DD4BF)
    private readonly SolidColorBrush _focusBrush = new(Windows.UI.Color.FromArgb(255, 245, 158, 11));                // Amber (#F59E0B)

    // Border Brushes (Crisp borders for thin vertical bars)
    private readonly SolidColorBrush _barBorderBrush = new(Windows.UI.Color.FromArgb(220, 255, 255, 255));

    // UI elements brushes
    private readonly SolidColorBrush _rulerLineBrush = new(Windows.UI.Color.FromArgb(90, 128, 128, 128));
    private readonly SolidColorBrush _laneDividerBrush = new(Windows.UI.Color.FromArgb(40, 128, 128, 128));
    private readonly SolidColorBrush _laneAltBrush = new(Windows.UI.Color.FromArgb(12, 128, 128, 128));
    private readonly SolidColorBrush _textDimBrush = new(Windows.UI.Color.FromArgb(180, 128, 128, 128));
    private readonly SolidColorBrush _cursorLineBrush = new(Windows.UI.Color.FromArgb(220, 240, 240, 240));

    // Dynamic hover overlay elements
    private Line? _cursorLine;
    private Border? _tooltipBorder;
    private TextBlock? _tooltipTitle;
    private TextBlock? _tooltipTime;
    private TextBlock? _tooltipDetails;

    // Hit-testing cache: (X, Y, Width, Height, Record)
    private readonly List<(double X, double Y, double Width, double Height, RenderTimingRecord Record)> _markerHitBoxes = new();

    public RenderTimelineControl()
    {
        this.InitializeComponent();
        CreateHoverOverlay();
    }

    private void CreateHoverOverlay()
    {
        // 1. Crosshair vertical time line
        _cursorLine = new Line
        {
            Stroke = _cursorLineBrush,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Visibility = Visibility.Collapsed
        };

        // 2. Floating interactive tooltip card
        _tooltipTitle = new TextBlock { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        _tooltipTime = new TextBlock { FontSize = 11, Foreground = _textDimBrush };
        _tooltipDetails = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap };

        var stack = new StackPanel
        {
            Spacing = 4,
            Children = { _tooltipTitle, _tooltipTime, _tooltipDetails }
        };

        _tooltipBorder = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(245, 30, 30, 35)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 100, 100, 110)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            MaxWidth = 420,
            Child = stack,
            Visibility = Visibility.Collapsed
        };
    }

    public void UpdateData(IEnumerable<RenderTimingRecord> records)
    {
        _allRecords = records.ToList();
        RedrawTimeline();
    }

    private void OnTimelineSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawTimeline();
    }

    private void OnTimeWindowChanged(object sender, SelectionChangedEventArgs e)
    {
        RedrawTimeline();
    }

    private void RedrawTimeline()
    {
        if (TimelineCanvas == null) return;
        TimelineCanvas.Children.Clear();
        _markerHitBoxes.Clear();

        double width = TimelineCanvas.ActualWidth;
        double height = TimelineCanvas.ActualHeight;

        if (width <= 20 || height <= 40 || _allRecords.Count == 0)
        {
            _displayedRecords.Clear();
            TimelineTitleBlock.Text = "TextBox Render & Visual Lifecycle Timeline (0 events)";
            HoverStatusText.Text = "Hover over any event bar on the timeline to inspect full details";
            TimeRangeSummaryText.Text = "Range: --";
            return;
        }

        double maxTime = _allRecords.Max(r => r.ElapsedSeconds);
        double minTime = _allRecords.Min(r => r.ElapsedSeconds);

        if (minTime > maxTime) minTime = 0;

        // Apply Time Window (Full Session vs 5s vs 10s)
        int timeWindowMode = TimeWindowCombo?.SelectedIndex ?? 0;
        if (timeWindowMode == 1) // Last 5 seconds
        {
            minTime = Math.Max(minTime, maxTime - 5.0);
        }
        else if (timeWindowMode == 2) // Last 10 seconds
        {
            minTime = Math.Max(minTime, maxTime - 10.0);
        }

        _displayedRecords = _allRecords.Where(r => r.ElapsedSeconds >= minTime).ToList();
        double timeSpan = Math.Max(0.5, maxTime - minTime);

        TimelineTitleBlock.Text = $"TextBox Render & Visual Lifecycle Timeline ({_displayedRecords.Count} events)";
        TimeRangeSummaryText.Text = $"Time Window: +{minTime:F2}s -> +{maxTime:F2}s ({timeSpan:F2}s span)";

        // Layout Constants: 5 Swimlanes
        // Lane 0: Composition (Rendering, Rendered)
        // Lane 1: Layout (SizeChanged, LayoutUpdated)
        // Lane 2: Viewport (BringIntoViewRequested, EffectiveViewportChanged)
        // Lane 3: Content (TextChanging, TextChanged, SelectionChanging, SelectionChanged)
        // Lane 4: Visual State (GotFocus, LostFocus)
        const double rulerHeight = 30.0;
        const int laneCount = 5;
        double laneAreaHeight = height - rulerHeight;
        double laneHeight = laneAreaHeight / laneCount;

        // Draw Swimlane Backgrounds & Dividers
        for (int i = 0; i < laneCount; i++)
        {
            double laneY = rulerHeight + (i * laneHeight);

            if (i % 2 == 1)
            {
                var laneRect = new Rectangle
                {
                    Width = width,
                    Height = laneHeight,
                    Fill = _laneAltBrush
                };
                Canvas.SetLeft(laneRect, 0);
                Canvas.SetTop(laneRect, laneY);
                TimelineCanvas.Children.Add(laneRect);
            }

            var divider = new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = laneY,
                Y2 = laneY,
                Stroke = _laneDividerBrush,
                StrokeThickness = 1
            };
            TimelineCanvas.Children.Add(divider);
        }

        // Draw Time Ruler at Top (Y = 0 to 30)
        var rulerBaseline = new Line
        {
            X1 = 0,
            X2 = width,
            Y1 = rulerHeight,
            Y2 = rulerHeight,
            Stroke = _rulerLineBrush,
            StrokeThickness = 1.5
        };
        TimelineCanvas.Children.Add(rulerBaseline);

        // Draw Time Ticks & Labels across ruler
        int tickCount = Math.Max(4, (int)(width / 120));
        for (int i = 0; i <= tickCount; i++)
        {
            double fraction = (double)i / tickCount;
            double tickX = fraction * (width - 24) + 12;
            double tickTime = minTime + (fraction * timeSpan);

            var tickLine = new Line
            {
                X1 = tickX,
                X2 = tickX,
                Y1 = rulerHeight - 6,
                Y2 = rulerHeight,
                Stroke = _rulerLineBrush,
                StrokeThickness = 1
            };
            TimelineCanvas.Children.Add(tickLine);

            var tickLabel = new TextBlock
            {
                Text = $"+{tickTime:F2}s",
                FontSize = 10,
                Foreground = _textDimBrush
            };
            Canvas.SetLeft(tickLabel, Math.Clamp(tickX - 16, 4, width - 45));
            Canvas.SetTop(tickLabel, 6);
            TimelineCanvas.Children.Add(tickLabel);
        }

        double plotWidth = width - 24;
        double plotMarginLeft = 12;

        // Draw Event Bars (Thin vertical bar with border)
        foreach (var record in _displayedRecords)
        {
            double relTime = record.ElapsedSeconds - minTime;
            double normX = Math.Clamp(relTime / timeSpan, 0, 1);
            double markerX = plotMarginLeft + (normX * plotWidth);

            int laneIndex;
            Brush markerBrush;
            double markerW = 4.5;

            switch (record.Category)
            {
                case "Composition":
                    laneIndex = 0;
                    markerBrush = record.EventName == "Rendering" ? _compositionRenderingBrush : _compositionRenderedBrush;
                    markerW = 5.0;
                    break;

                case "Layout":
                    laneIndex = 1;
                    markerBrush = record.EventName == "SizeChanged" ? _layoutSizeBrush : _layoutUpdatedBrush;
                    markerW = 4.5;
                    break;

                case "Viewport":
                    laneIndex = 2;
                    markerBrush = record.EventName == "BringIntoViewRequested" ? _viewportBringBrush : _viewportEffectiveBrush;
                    markerW = 4.5;
                    break;

                case "Content":
                    laneIndex = 3;
                    markerBrush = (record.EventName.Contains("Selection")) ? _contentSelectionBrush : _contentTextBrush;
                    markerW = 4.0;
                    break;

                case "VisualState":
                default:
                    laneIndex = 4;
                    markerBrush = _focusBrush;
                    markerW = 4.5;
                    break;
            }

            double laneTop = rulerHeight + (laneIndex * laneHeight);
            double markerH = Math.Max(6, laneHeight - 6.0);
            double markerY = laneTop + ((laneHeight - markerH) / 2.0);

            var bar = new Rectangle
            {
                Width = markerW,
                Height = markerH,
                Fill = markerBrush,
                Stroke = _barBorderBrush,
                StrokeThickness = 1.0,
                RadiusX = 1,
                RadiusY = 1
            };
            Canvas.SetLeft(bar, markerX - (markerW / 2.0));
            Canvas.SetTop(bar, markerY);
            TimelineCanvas.Children.Add(bar);

            _markerHitBoxes.Add((markerX, markerY, Math.Max(markerW, 14), markerH, record));
        }

        // Add Hover Overlay Elements (Z-order on top)
        if (_cursorLine != null)
        {
            _cursorLine.Y1 = 0;
            _cursorLine.Y2 = height;
            _cursorLine.Visibility = Visibility.Collapsed;
            TimelineCanvas.Children.Add(_cursorLine);
        }

        if (_tooltipBorder != null)
        {
            _tooltipBorder.Visibility = Visibility.Collapsed;
            TimelineCanvas.Children.Add(_tooltipBorder);
        }
    }

    private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_displayedRecords.Count == 0 || _cursorLine == null || _tooltipBorder == null) return;

        double width = TimelineCanvas.ActualWidth;
        double height = TimelineCanvas.ActualHeight;
        Point pos = e.GetCurrentPoint(TimelineCanvas).Position;

        _cursorLine.X1 = pos.X;
        _cursorLine.X2 = pos.X;
        _cursorLine.Visibility = Visibility.Visible;

        const double rulerHeight = 30.0;
        double laneAreaHeight = height - rulerHeight;
        double laneHeight = laneAreaHeight / 5.0;

        // Determine which lane cursor is in
        int hoveredLane = -1;
        if (pos.Y >= rulerHeight)
        {
            hoveredLane = Math.Clamp((int)((pos.Y - rulerHeight) / laneHeight), 0, 4);
        }

        // Find closest event in the hovered lane (within horizontal proximity 24px)
        // If none found in that lane, check across all lanes within 14px
        RenderTimingRecord? closestRecord = null;
        double minDistance = double.MaxValue;

        foreach (var item in _markerHitBoxes)
        {
            double dx = Math.Abs(item.X - pos.X);
            double dy = Math.Abs((item.Y + item.Height / 2.0) - pos.Y);

            // Favor events in the same lane
            if (dy <= laneHeight / 2.0)
            {
                if (dx < 24.0 && dx < minDistance)
                {
                    minDistance = dx;
                    closestRecord = item.Record;
                }
            }
        }

        if (closestRecord == null)
        {
            // Fallback: check any lane within 12px
            foreach (var item in _markerHitBoxes)
            {
                double dx = Math.Abs(item.X - pos.X);
                double dy = Math.Abs((item.Y + item.Height / 2.0) - pos.Y);
                if (dx < 12.0 && dy < laneHeight && dx < minDistance)
                {
                    minDistance = dx;
                    closestRecord = item.Record;
                }
            }
        }

        if (closestRecord != null && _tooltipTitle != null && _tooltipTime != null && _tooltipDetails != null)
        {
            _tooltipTitle.Text = $"[{closestRecord.Category}] {closestRecord.EventName}";
            _tooltipTitle.Foreground = GetCategoryBrush(closestRecord.Category);
            _tooltipTime.Text = $"Time: {closestRecord.FormattedTime} (+{closestRecord.ElapsedSeconds:F3}s) | Event #{closestRecord.Id}";
            _tooltipDetails.Text = closestRecord.Details;

            _tooltipBorder.Visibility = Visibility.Visible;

            // Position tooltip dynamically near cursor
            double tooltipX = pos.X + 16;
            if (tooltipX + 320 > width)
            {
                tooltipX = pos.X - 330;
            }
            if (tooltipX < 4) tooltipX = 4;

            double tooltipY = pos.Y - 20;
            if (tooltipY + 110 > height)
            {
                tooltipY = height - 120;
            }
            if (tooltipY < 4) tooltipY = 4;

            Canvas.SetLeft(_tooltipBorder, tooltipX);
            Canvas.SetTop(_tooltipBorder, tooltipY);

            HoverStatusText.Text = $"[{closestRecord.Category}] {closestRecord.EventName} at {closestRecord.FormattedTime} (+{closestRecord.ElapsedSeconds:F3}s) - {closestRecord.Details}";
        }
        else
        {
            _tooltipBorder.Visibility = Visibility.Collapsed;

            // Display time at cursor safely
            if (_displayedRecords.Count > 0)
            {
                double maxTime = _displayedRecords.Max(r => r.ElapsedSeconds);
                double minTime = _displayedRecords.Min(r => r.ElapsedSeconds);
                int timeWindowMode = TimeWindowCombo?.SelectedIndex ?? 0;
                if (timeWindowMode == 1) minTime = Math.Max(minTime, maxTime - 5.0);
                else if (timeWindowMode == 2) minTime = Math.Max(minTime, maxTime - 10.0);
                double timeSpan = Math.Max(0.5, maxTime - minTime);

                double normX = Math.Clamp((pos.X - 12) / (width - 24), 0, 1);
                double cursorTime = minTime + (normX * timeSpan);

                HoverStatusText.Text = $"Time at cursor: +{cursorTime:F3}s (Hover near any bar to view event details)";
            }
            else
            {
                HoverStatusText.Text = "Hover over any event bar on the timeline to inspect full details";
            }
        }
    }

    private Brush GetCategoryBrush(string category)
    {
        return category switch
        {
            "Composition" => _compositionRenderedBrush,
            "Layout" => _layoutSizeBrush,
            "Viewport" => _viewportBringBrush,
            "Content" => _contentTextBrush,
            "VisualState" => _focusBrush,
            _ => _cursorLineBrush
        };
    }

    private void OnCanvasPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_cursorLine != null) _cursorLine.Visibility = Visibility.Collapsed;
        if (_tooltipBorder != null) _tooltipBorder.Visibility = Visibility.Collapsed;
        HoverStatusText.Text = "Hover over any event bar on the timeline to inspect full details";
    }
}
