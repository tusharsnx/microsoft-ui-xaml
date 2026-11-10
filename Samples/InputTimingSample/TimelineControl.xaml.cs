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

public sealed partial class TimelineControl : UserControl
{
    private List<TimingRecord> _allTimingRecords = new();
    private List<TimingRecord> _displayedTimingRecords = new();
    private List<FrameRecord> _allFrames = new();
    private List<FrameRecord> _displayedFrames = new();
    private List<RenderTimingRecord> _allRenderRecords = new();
    private List<RenderTimingRecord> _displayedRenderRecords = new();

    private double _minTime;
    private double _timeSpan = 1.0;

    // Zoom & Pan state
    private bool _customZoomActive;
    private double _currentViewMinTime;
    private double _currentViewMaxTime;
    private bool _isPanning;
    private Point _panStartPoint;
    private double _panStartMin;
    private double _panStartMax;

    // Palette: Categorized Brushes
    private readonly SolidColorBrush _frameBrush = new(Windows.UI.Color.FromArgb(255, 139, 92, 246));           // Purple (#8B5CF6)
    private readonly SolidColorBrush _compositionRenderingBrush = new(Windows.UI.Color.FromArgb(255, 168, 85, 247)); // Violet (#A855F7)
    private readonly SolidColorBrush _layoutSizeBrush = new(Windows.UI.Color.FromArgb(255, 59, 130, 246));        // Blue (#3B82F6)
    private readonly SolidColorBrush _layoutUpdatedBrush = new(Windows.UI.Color.FromArgb(255, 6, 182, 212));      // Cyan (#06B6D4)
    private readonly SolidColorBrush _viewportBringBrush = new(Windows.UI.Color.FromArgb(255, 20, 184, 166));     // Teal (#14B8A6)
    private readonly SolidColorBrush _viewportEffectiveBrush = new(Windows.UI.Color.FromArgb(255, 99, 102, 241)); // Indigo (#6366F1)
    private readonly SolidColorBrush _contentTextBrush = new(Windows.UI.Color.FromArgb(255, 16, 185, 129));       // Emerald (#10B981)
    private readonly SolidColorBrush _contentSelectionBrush = new(Windows.UI.Color.FromArgb(255, 45, 212, 191));  // Mint (#2DD4BF)
    private readonly SolidColorBrush _charBrush = new(Windows.UI.Color.FromArgb(255, 16, 185, 129));              // Emerald (#10B981)
    private readonly SolidColorBrush _keyBrush = new(Windows.UI.Color.FromArgb(255, 245, 158, 11));               // Amber (#F59E0B)
    private readonly SolidColorBrush _pointerDownBrush = new(Windows.UI.Color.FromArgb(255, 59, 130, 246));      // Blue (#3B82F6)
    private readonly SolidColorBrush _pointerUpBrush = new(Windows.UI.Color.FromArgb(255, 99, 102, 241));        // Indigo (#6366F1)
    private readonly SolidColorBrush _pointerMoveBrush = new(Windows.UI.Color.FromArgb(220, 6, 182, 212));       // Cyan (#06B6D4)
    private readonly SolidColorBrush _visualStateBrush = new(Windows.UI.Color.FromArgb(255, 244, 63, 94));        // Rose (#F43F5E)

    // Crisp Bar Borders
    private readonly SolidColorBrush _eventBarBorderBrush = new(Windows.UI.Color.FromArgb(230, 255, 255, 255));
    private readonly SolidColorBrush _frameBarBorderBrush = new(Windows.UI.Color.FromArgb(240, 237, 233, 254));

    // UI structural brushes
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
    private TextBlock? _tooltipTimings;

    // Cache of mapped visual items for hit-testing on hover
    private readonly List<(double X, double Y, double Width, double Height, TimingRecord Record)> _timingHitBoxes = new();
    private readonly List<(double X, double Y, double Width, double Height, FrameRecord Frame)> _frameHitBoxes = new();
    private readonly List<(double X, double Y, double Width, double Height, RenderTimingRecord Render)> _renderHitBoxes = new();

    public TimelineControl()
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
        _tooltipTimings = new TextBlock { FontSize = 10, Foreground = _charBrush, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

        var stack = new StackPanel
        {
            Spacing = 4,
            Children = { _tooltipTitle, _tooltipTime, _tooltipDetails, _tooltipTimings }
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

    public void UpdateData(IEnumerable<TimingRecord> records, IEnumerable<FrameRecord> frames, IEnumerable<RenderTimingRecord> renderRecords)
    {
        _allTimingRecords = records.ToList();
        _allFrames = frames.ToList();
        _allRenderRecords = renderRecords.ToList();
        ResetZoom();
    }

    public void UpdateData(IEnumerable<TimingRecord> records, IEnumerable<FrameRecord> frames)
    {
        UpdateData(records, frames, Array.Empty<RenderTimingRecord>());
    }

    public void ResetZoom()
    {
        _customZoomActive = false;
        _isPanning = false;
        if (ResetZoomButton != null)
        {
            ResetZoomButton.Visibility = Visibility.Collapsed;
        }
        RedrawTimeline();
    }

    private void OnResetZoomClicked(object sender, RoutedEventArgs e)
    {
        ResetZoom();
    }

    private void OnTimelineSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawTimeline();
    }

    private void OnTimeWindowChanged(object sender, SelectionChangedEventArgs e)
    {
        ResetZoom();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        RedrawTimeline();
    }

    private (double Min, double Max) GetSessionTimeBounds()
    {
        bool showPointerMoves = ShowPointerMovesCheckBox?.IsChecked ?? true;
        var filteredTiming = _allTimingRecords.AsEnumerable();
        if (!showPointerMoves)
        {
            filteredTiming = filteredTiming.Where(r => r.EventType != "PointerMoved");
        }
        var filteredTimingList = filteredTiming.ToList();

        double maxTime = 0;
        double minTime = double.MaxValue;

        if (filteredTimingList.Count > 0)
        {
            maxTime = Math.Max(maxTime, filteredTimingList.Max(r => r.ElapsedSeconds));
            minTime = Math.Min(minTime, filteredTimingList.Min(r => r.ElapsedSeconds));
        }

        if (_allFrames.Count > 0)
        {
            maxTime = Math.Max(maxTime, _allFrames.Max(f => f.ElapsedSeconds));
            minTime = Math.Min(minTime, _allFrames.Min(f => f.ElapsedSeconds));
        }

        if (_allRenderRecords.Count > 0)
        {
            maxTime = Math.Max(maxTime, _allRenderRecords.Max(r => r.ElapsedSeconds));
            minTime = Math.Min(minTime, _allRenderRecords.Min(r => r.ElapsedSeconds));
        }

        if (minTime > maxTime) minTime = 0;
        return (minTime, maxTime);
    }

    private void RedrawTimeline()
    {
        if (TimelineCanvas == null) return;
        TimelineCanvas.Children.Clear();
        _timingHitBoxes.Clear();
        _frameHitBoxes.Clear();
        _renderHitBoxes.Clear();

        double width = TimelineCanvas.ActualWidth;
        double height = TimelineCanvas.ActualHeight;

        if (width <= 20 || height <= 40 || (_allTimingRecords.Count == 0 && _allFrames.Count == 0 && _allRenderRecords.Count == 0))
        {
            _displayedTimingRecords.Clear();
            _displayedFrames.Clear();
            _displayedRenderRecords.Clear();
            TimelineTitleBlock.Text = "Unified Input, Render & Frame Timeline (0 events)";
            HoverStatusText.Text = "Hover over any frame or event bar on the timeline to inspect full details";
            TimeRangeSummaryText.Text = "Range: --";
            return;
        }

        var (sessionMin, sessionMax) = GetSessionTimeBounds();
        double minTime;
        double maxTime;

        if (_customZoomActive)
        {
            minTime = _currentViewMinTime;
            maxTime = _currentViewMaxTime;
        }
        else
        {
            minTime = sessionMin;
            maxTime = sessionMax;

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
        }

        double timeSpan = Math.Max(0.002, maxTime - minTime);
        _minTime = minTime;
        _timeSpan = timeSpan;

        // Apply filters & visible range window
        bool showPointerMoves = ShowPointerMovesCheckBox?.IsChecked ?? true;
        var filteredTiming = _allTimingRecords.AsEnumerable();
        if (!showPointerMoves)
        {
            filteredTiming = filteredTiming.Where(r => r.EventType != "PointerMoved");
        }

        _displayedTimingRecords = filteredTiming.Where(r => r.ElapsedSeconds >= minTime - 0.05 && r.ElapsedSeconds <= maxTime + 0.05).ToList();
        _displayedFrames = _allFrames.Where(f => f.ElapsedSeconds >= minTime - 0.05 && f.ElapsedSeconds <= maxTime + 0.05).ToList();
        _displayedRenderRecords = _allRenderRecords.Where(r => r.ElapsedSeconds >= minTime - 0.05 && r.ElapsedSeconds <= maxTime + 0.05).ToList();

        int totalVisible = _displayedTimingRecords.Count + _displayedFrames.Count + _displayedRenderRecords.Count;
        TimelineTitleBlock.Text = $"Unified Input, Render & Frame Timeline ({totalVisible} visible markers: {_displayedTimingRecords.Count} inputs, {_displayedFrames.Count} frames, {_displayedRenderRecords.Count} render lifecycle)";

        if (_customZoomActive)
        {
            double totalSpan = Math.Max(0.001, sessionMax - sessionMin);
            double zoomRatio = totalSpan / timeSpan;
            TimeRangeSummaryText.Text = $"🔍 Zoom: {zoomRatio:F1}x | Window: +{minTime:F3}s -> +{maxTime:F3}s ({timeSpan:F3}s span) | Ctrl+Scroll: Zoom, Drag: Pan";
        }
        else
        {
            TimeRangeSummaryText.Text = $"Time Window: +{minTime:F2}s -> +{maxTime:F2}s ({timeSpan:F2}s span) | Ctrl+Scroll to zoom";
        }

        // Layout: 7 Swimlanes
        // Lane 0: Frames & Composition (FrameRecord + Rendering/Rendered)
        // Lane 1: Layout Passes (SizeChanged, LayoutUpdated)
        // Lane 2: Viewport & Scroll (BringIntoViewRequested, EffectiveViewportChanged)
        // Lane 3: Content & Selection (TextChanging, TextChanged, SelectionChanging, SelectionChanged)
        // Lane 4: Keyboard & Text Input (CharacterReceived, KeyDown)
        // Lane 5: Pointer Input (PointerPressed, PointerReleased, PointerMoved)
        // Lane 6: Visual State (GotFocus, LostFocus)
        const double rulerHeight = 30.0;
        const int laneCount = 7;
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

        // Draw Time Ruler Baseline at Top
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
        string tickFormat = timeSpan < 0.05 ? "F4" : (timeSpan < 0.5 ? "F3" : (timeSpan < 10.0 ? "F2" : "F1"));
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
                Text = $"+{tickTime.ToString(tickFormat)}s",
                FontSize = 10,
                Foreground = _textDimBrush
            };
            Canvas.SetLeft(tickLabel, Math.Clamp(tickX - 16, 4, width - 55));
            Canvas.SetTop(tickLabel, 6);
            TimelineCanvas.Children.Add(tickLabel);
        }

        double plotWidth = width - 24;
        double plotMarginLeft = 12;

        // 1. Draw Rendered Frame Bars on Lane 0 (Thin bar with border)
        double frameLaneTop = rulerHeight + (0 * laneHeight);
        double frameBarW = 5.5;
        double frameBarH = Math.Max(6, laneHeight - 6.0);
        double frameBarY = frameLaneTop + ((laneHeight - frameBarH) / 2.0);

        foreach (var frame in _displayedFrames)
        {
            double relTime = frame.ElapsedSeconds - minTime;
            if (relTime < -0.001 || relTime > timeSpan + 0.001) continue;
            double normX = Math.Clamp(relTime / timeSpan, 0, 1);
            double frameX = plotMarginLeft + (normX * plotWidth);

            var frameBar = new Rectangle
            {
                Width = frameBarW,
                Height = frameBarH,
                Fill = _frameBrush,
                Stroke = _frameBarBorderBrush,
                StrokeThickness = 1.0,
                RadiusX = 1,
                RadiusY = 1
            };
            Canvas.SetLeft(frameBar, frameX - (frameBarW / 2.0));
            Canvas.SetTop(frameBar, frameBarY);
            TimelineCanvas.Children.Add(frameBar);

            _frameHitBoxes.Add((frameX, frameBarY, Math.Max(frameBarW, 14), frameBarH, frame));
        }

        // 2. Draw Render Lifecycle Events (Lanes 0, 1, 2, 3, 6)
        foreach (var r in _displayedRenderRecords)
        {
            double relTime = r.ElapsedSeconds - minTime;
            if (relTime < -0.001 || relTime > timeSpan + 0.001) continue;
            double normX = Math.Clamp(relTime / timeSpan, 0, 1);
            double markerX = plotMarginLeft + (normX * plotWidth);

            int laneIndex;
            Brush markerBrush;
            double markerW = 4.5;

            switch (r.Category)
            {
                case "Composition":
                    laneIndex = 0;
                    markerBrush = r.EventName == "Rendering" ? _compositionRenderingBrush : _frameBrush;
                    markerW = 4.0;
                    break;

                case "Layout":
                    laneIndex = 1;
                    markerBrush = r.EventName == "SizeChanged" ? _layoutSizeBrush : _layoutUpdatedBrush;
                    markerW = 4.5;
                    break;

                case "Viewport":
                    laneIndex = 2;
                    markerBrush = r.EventName == "BringIntoViewRequested" ? _viewportBringBrush : _viewportEffectiveBrush;
                    markerW = 4.5;
                    break;

                case "Content":
                    laneIndex = 3;
                    markerBrush = r.EventName.Contains("Selection") ? _contentSelectionBrush : _contentTextBrush;
                    markerW = 4.5;
                    break;

                case "VisualState":
                default:
                    laneIndex = 6;
                    markerBrush = _visualStateBrush;
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
                Stroke = _eventBarBorderBrush,
                StrokeThickness = 1.0,
                RadiusX = 1,
                RadiusY = 1
            };
            Canvas.SetLeft(bar, markerX - (markerW / 2.0));
            Canvas.SetTop(bar, markerY);
            TimelineCanvas.Children.Add(bar);

            _renderHitBoxes.Add((markerX, markerY, Math.Max(markerW, 14), markerH, r));
        }

        // 3. Draw Input Events on Lanes 4 & 5 (Thin bars with distinct borders)
        foreach (var record in _displayedTimingRecords)
        {
            double relTime = record.ElapsedSeconds - minTime;
            if (relTime < -0.001 || relTime > timeSpan + 0.001) continue;
            double normX = Math.Clamp(relTime / timeSpan, 0, 1);
            double markerX = plotMarginLeft + (normX * plotWidth);

            int laneIndex;
            Brush markerBrush;
            double markerW = 4.5;

            switch (record.EventType)
            {
                case "CharacterReceived":
                    laneIndex = 4;
                    markerBrush = _charBrush;
                    markerW = 5.0;
                    break;

                case "KeyDown":
                    laneIndex = 4;
                    markerBrush = _keyBrush;
                    markerW = 4.5;
                    break;

                case "PointerPressed":
                    laneIndex = 5;
                    markerBrush = _pointerDownBrush;
                    markerW = 4.5;
                    break;

                case "PointerReleased":
                    laneIndex = 5;
                    markerBrush = _pointerUpBrush;
                    markerW = 4.5;
                    break;

                case "PointerMoved":
                default:
                    laneIndex = 5;
                    markerBrush = _pointerMoveBrush;
                    markerW = 3.0;
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
                Stroke = _eventBarBorderBrush,
                StrokeThickness = 1.0,
                RadiusX = 1,
                RadiusY = 1
            };
            Canvas.SetLeft(bar, markerX - (markerW / 2.0));
            Canvas.SetTop(bar, markerY);
            TimelineCanvas.Children.Add(bar);

            _timingHitBoxes.Add((markerX, markerY, Math.Max(markerW, 14), markerH, record));
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
        double width = TimelineCanvas.ActualWidth;
        double height = TimelineCanvas.ActualHeight;
        Point pos = e.GetCurrentPoint(TimelineCanvas).Position;

        if (_isPanning)
        {
            double dx = pos.X - _panStartPoint.X;
            if (Math.Abs(dx) > 1.0)
            {
                double plotWidth = width - 24;
                if (plotWidth > 0)
                {
                    double span = _panStartMax - _panStartMin;
                    double timeShift = (dx / plotWidth) * span;

                    var (sessionMin, sessionMax) = GetSessionTimeBounds();

                    double newMin = _panStartMin - timeShift;
                    double newMax = _panStartMax - timeShift;

                    if (newMin < sessionMin)
                    {
                        newMin = sessionMin;
                        newMax = sessionMin + span;
                    }
                    if (newMax > sessionMax)
                    {
                        newMax = sessionMax;
                        newMin = sessionMax - span;
                    }

                    _currentViewMinTime = newMin;
                    _currentViewMaxTime = newMax;
                    _customZoomActive = true;

                    if (ResetZoomButton != null)
                    {
                        ResetZoomButton.Visibility = Visibility.Visible;
                    }

                    RedrawTimeline();
                }
            }
            return;
        }

        if ((_displayedTimingRecords.Count == 0 && _displayedFrames.Count == 0 && _displayedRenderRecords.Count == 0) || _cursorLine == null || _tooltipBorder == null) return;

        _cursorLine.X1 = pos.X;
        _cursorLine.X2 = pos.X;
        _cursorLine.Visibility = Visibility.Visible;

        const double rulerHeight = 30.0;
        double laneAreaHeight = height - rulerHeight;
        double laneHeight = laneAreaHeight / 7.0;

        // Determine which of the 7 lanes cursor is currently over
        int hoveredLane = -1;
        if (pos.Y >= rulerHeight)
        {
            hoveredLane = Math.Clamp((int)((pos.Y - rulerHeight) / laneHeight), 0, 6);
        }

        // Check for closest Frame in Lane 0
        FrameRecord? closestFrame = null;
        double minFrameDist = double.MaxValue;
        if (hoveredLane == 0 || hoveredLane == -1)
        {
            foreach (var f in _frameHitBoxes)
            {
                double dist = Math.Abs(f.X - pos.X);
                if (dist < minFrameDist && dist < 24.0)
                {
                    minFrameDist = dist;
                    closestFrame = f.Frame;
                }
            }
        }

        // Check for closest Render event in Lanes 0, 1, 2, 3, 6
        RenderTimingRecord? closestRender = null;
        double minRenderDist = double.MaxValue;
        foreach (var r in _renderHitBoxes)
        {
            double dx = Math.Abs(r.X - pos.X);
            double dy = Math.Abs((r.Y + r.Height / 2.0) - pos.Y);
            if (dy <= laneHeight / 2.0 && dx < 24.0 && dx < minRenderDist)
            {
                minRenderDist = dx;
                closestRender = r.Render;
            }
        }

        // Check for closest Timing event in Lanes 4 & 5
        TimingRecord? closestTiming = null;
        double minTimingDist = double.MaxValue;
        foreach (var m in _timingHitBoxes)
        {
            double dx = Math.Abs(m.X - pos.X);
            double dy = Math.Abs((m.Y + m.Height / 2.0) - pos.Y);
            if (dy <= laneHeight / 2.0 && dx < 24.0 && dx < minTimingDist)
            {
                minTimingDist = dx;
                closestTiming = m.Record;
            }
        }

        // Fallback global check across all collections if none in hovered lane within 12px
        if (closestFrame == null && closestRender == null && closestTiming == null)
        {
            foreach (var f in _frameHitBoxes)
            {
                double dist = Math.Abs(f.X - pos.X);
                if (dist < 12.0 && dist < minFrameDist) { minFrameDist = dist; closestFrame = f.Frame; }
            }
            foreach (var r in _renderHitBoxes)
            {
                double dist = Math.Abs(r.X - pos.X);
                if (dist < 12.0 && dist < minRenderDist) { minRenderDist = dist; closestRender = r.Render; }
            }
            foreach (var m in _timingHitBoxes)
            {
                double dist = Math.Abs(m.X - pos.X);
                if (dist < 12.0 && dist < minTimingDist) { minTimingDist = dist; closestTiming = m.Record; }
            }
        }

        // Prioritize display
        if (hoveredLane == 0 && closestFrame != null)
        {
            ShowFrameTooltip(closestFrame);
        }
        else if (closestRender != null && minRenderDist <= minTimingDist && minRenderDist <= minFrameDist)
        {
            ShowRenderTooltip(closestRender);
        }
        else if (closestTiming != null && minTimingDist <= minFrameDist)
        {
            ShowTimingTooltip(closestTiming);
        }
        else if (closestFrame != null)
        {
            ShowFrameTooltip(closestFrame);
        }
        else
        {
            if (_tooltipBorder != null) _tooltipBorder.Visibility = Visibility.Collapsed;

            if (_displayedTimingRecords.Count > 0 || _displayedFrames.Count > 0 || _displayedRenderRecords.Count > 0)
            {
                double normX = Math.Clamp((pos.X - 12) / (width - 24), 0, 1);
                double cursorTime = _minTime + (normX * _timeSpan);
                HoverStatusText.Text = $"Time at cursor: +{cursorTime:F3}s (Hover near any bar to view event details)";
            }
            else
            {
                HoverStatusText.Text = "Hover over any frame or event bar on the timeline to inspect full details";
            }
            return;
        }

        // Position tooltip card safely within canvas bounds
        _tooltipBorder.Measure(new Size(420, 260));
        double tipW = _tooltipBorder.DesiredSize.Width > 0 ? _tooltipBorder.DesiredSize.Width : 320;
        double tipH = _tooltipBorder.DesiredSize.Height > 0 ? _tooltipBorder.DesiredSize.Height : 120;

        double tipX = pos.X + 16;
        if (tipX + tipW > width) tipX = pos.X - tipW - 16;
        if (tipX < 4) tipX = 4;

        double tipY = pos.Y - 20;
        if (tipY + tipH > height) tipY = height - tipH - 8;
        if (tipY < 4) tipY = 4;

        Canvas.SetLeft(_tooltipBorder, tipX);
        Canvas.SetTop(_tooltipBorder, tipY);
        _tooltipBorder.Visibility = Visibility.Visible;
    }

    private void ShowFrameTooltip(FrameRecord frame)
    {
        if (_tooltipTitle != null)
        {
            _tooltipTitle.Text = $"Rendered Frame #{frame.FrameId}";
            _tooltipTitle.Foreground = _frameBrush;
        }
        if (_tooltipTime != null)
        {
            _tooltipTime.Text = $"Rendered At: {frame.FormattedTime} ({frame.FormattedElapsed})";
        }
        if (_tooltipDetails != null)
        {
            _tooltipDetails.Text = $"Events Processed: {frame.EventCount} event(s)\nBreakdown: {frame.EventSummary}";
        }
        if (_tooltipTimings != null)
        {
            _tooltipTimings.Text = $"Engine Layout/Draw Duration: {frame.FrameDuration}";
            _tooltipTimings.Foreground = _frameBrush;
        }
        HoverStatusText.Text = $"Hovering: Rendered Frame #{frame.FrameId} | At: {frame.FormattedTime} ({frame.FormattedElapsed}) | Events: {frame.EventCount} ({frame.EventSummary}) | Draw Duration: {frame.FrameDuration}";
    }

    private void ShowTimingTooltip(TimingRecord record)
    {
        if (_tooltipTitle != null)
        {
            _tooltipTitle.Text = $"Event #{record.Id}: {record.EventType}";
            _tooltipTitle.Foreground = GetBrushForEvent(record.EventType);
        }
        if (_tooltipTime != null)
        {
            _tooltipTime.Text = $"Occurred At: {record.FormattedTime} ({record.FormattedElapsed})";
        }
        if (_tooltipDetails != null)
        {
            string frameInfo = record.FrameId > 0 ? $"Committed in Frame #{record.FrameId}" : "Frame pending";
            _tooltipDetails.Text = $"Details: {record.Details}\n{frameInfo}";
        }
        if (_tooltipTimings != null)
        {
            _tooltipTimings.Text = $"Dispatch (T1->T2): {record.DispatchDelay}  |  TextBox (T2->T3): {record.TextBoxProcessing}\nFrame Commit (T3->T4): {record.RenderDelay}  |  Total Latency (T1->T4): {record.TotalLatency}";
            _tooltipTimings.Foreground = _charBrush;
        }
        HoverStatusText.Text = $"Hovering: Event #{record.Id} [{record.EventType}] | Time: {record.FormattedTime} ({record.FormattedElapsed}) | Details: {record.Details} | Processing: {record.TextBoxProcessing} | Total: {record.TotalLatency} | Frame #{record.FrameId}";
    }

    private void ShowRenderTooltip(RenderTimingRecord record)
    {
        if (_tooltipTitle != null)
        {
            _tooltipTitle.Text = $"[{record.Category}] {record.EventName}";
            _tooltipTitle.Foreground = GetCategoryBrush(record.Category);
        }
        if (_tooltipTime != null)
        {
            _tooltipTime.Text = $"Time: {record.FormattedTime} ({record.FormattedElapsed}) | ID: #{record.Id}";
        }
        if (_tooltipDetails != null)
        {
            _tooltipDetails.Text = record.Details;
        }
        if (_tooltipTimings != null)
        {
            _tooltipTimings.Text = $"Render Lifecycle Event: Category = {record.Category}";
            _tooltipTimings.Foreground = GetCategoryBrush(record.Category);
        }
        HoverStatusText.Text = $"Hovering: [{record.Category}] {record.EventName} at {record.FormattedTime} ({record.FormattedElapsed}) - {record.Details}";
    }

    private void OnCanvasPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_isPanning) return;
        if (_cursorLine != null) _cursorLine.Visibility = Visibility.Collapsed;
        if (_tooltipBorder != null) _tooltipBorder.Visibility = Visibility.Collapsed;
        HoverStatusText.Text = "Hover over any frame or event bar on the timeline to inspect full details";
    }

    private void OnTimelinePointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var keyModifiers = e.KeyModifiers;
        bool isCtrl = (keyModifiers & Windows.System.VirtualKeyModifiers.Control) == Windows.System.VirtualKeyModifiers.Control;
        if (!isCtrl)
        {
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }

        if (!isCtrl) return;

        var point = e.GetCurrentPoint(TimelineCanvas);
        int delta = point.Properties.MouseWheelDelta;
        if (delta == 0) return;

        e.Handled = true;
        ZoomAt(point.Position.X, delta > 0);
    }

    private void ZoomAt(double canvasX, bool zoomIn)
    {
        var (sessionMin, sessionMax) = GetSessionTimeBounds();
        double totalSpan = Math.Max(0.01, sessionMax - sessionMin);
        if (totalSpan <= 0.002) return;

        double width = TimelineCanvas.ActualWidth;
        if (width <= 24) return;

        double plotWidth = width - 24;
        double normX = Math.Clamp((canvasX - 12) / plotWidth, 0, 1);

        double currentMin = _customZoomActive ? _currentViewMinTime : _minTime;
        double currentMax = _customZoomActive ? _currentViewMaxTime : (_minTime + _timeSpan);
        double currentSpan = Math.Max(0.002, currentMax - currentMin);

        double focalTime = currentMin + (normX * currentSpan);

        double factor = 1.35; // 35% zoom step
        double newSpan;
        if (zoomIn)
        {
            newSpan = currentSpan / factor;
            if (newSpan < 0.002) // 2ms minimum span limit
            {
                newSpan = 0.002;
            }
        }
        else
        {
            newSpan = currentSpan * factor;
            if (newSpan >= totalSpan)
            {
                ResetZoom();
                return;
            }
        }

        double newMin = focalTime - (normX * newSpan);
        double newMax = newMin + newSpan;

        if (newMin < sessionMin)
        {
            newMin = sessionMin;
            newMax = Math.Min(sessionMax, newMin + newSpan);
        }
        if (newMax > sessionMax)
        {
            newMax = sessionMax;
            newMin = Math.Max(sessionMin, newMax - newSpan);
        }

        _customZoomActive = true;
        _currentViewMinTime = newMin;
        _currentViewMaxTime = newMax;

        if (ResetZoomButton != null)
        {
            ResetZoomButton.Visibility = Visibility.Visible;
        }

        RedrawTimeline();
    }

    private void OnCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(TimelineCanvas).Properties;
        if (props.IsLeftButtonPressed || props.IsMiddleButtonPressed)
        {
            var (sessionMin, sessionMax) = GetSessionTimeBounds();
            if (sessionMax <= sessionMin) return;

            _isPanning = true;
            _panStartPoint = e.GetCurrentPoint(TimelineCanvas).Position;
            _panStartMin = _customZoomActive ? _currentViewMinTime : _minTime;
            _panStartMax = _customZoomActive ? _currentViewMaxTime : (_minTime + _timeSpan);
            TimelineCanvas.CapturePointer(e.Pointer);

            if (_tooltipBorder != null) _tooltipBorder.Visibility = Visibility.Collapsed;
            if (_cursorLine != null) _cursorLine.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            try { TimelineCanvas.ReleasePointerCapture(e.Pointer); } catch { }
        }
    }

    private void OnCanvasPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isPanning = false;
    }

    private Brush GetBrushForEvent(string eventType)
    {
        return eventType switch
        {
            "CharacterReceived" => _charBrush,
            "KeyDown" => _keyBrush,
            "PointerPressed" => _pointerDownBrush,
            "PointerReleased" => _pointerUpBrush,
            "PointerMoved" => _pointerMoveBrush,
            _ => _textDimBrush
        };
    }

    private Brush GetCategoryBrush(string category)
    {
        return category switch
        {
            "Composition" => _frameBrush,
            "Layout" => _layoutSizeBrush,
            "Viewport" => _viewportBringBrush,
            "Content" => _contentTextBrush,
            "VisualState" => _visualStateBrush,
            _ => _textDimBrush
        };
    }
}
