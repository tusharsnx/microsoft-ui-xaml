// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using WinUIProfiler.App.ViewModels;
using WinUIProfiler.Engine.Events;
using WinUIProfiler.Engine.Storage;
using WinUIProfiler.Engine.Timeline;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace WinUIProfiler.App.Views;

public sealed partial class TimelineView : UserControl
{
    private const int OverviewBins = 150;
    private const double LaneHeight = 18;
    private const double LaneSpacing = 2;
    private const double TrackTopPadding = 4;
    private const double MinTrackHeight = 44;

    private static readonly SolidColorBrush XamlBrush = new(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush XamlFill = new(Color.FromArgb(0x35, 0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush CompBrush = new(Color.FromArgb(0xFF, 0x87, 0x64, 0xB8));
    private static readonly SolidColorBrush CompFill = new(Color.FromArgb(0x35, 0x87, 0x64, 0xB8));
    private static readonly SolidColorBrush OtherBrush = new(Color.FromArgb(0xFF, 0xD8, 0x3B, 0x01));
    private static readonly SolidColorBrush OtherFill = new(Color.FromArgb(0x35, 0xD8, 0x3B, 0x01));
    private static readonly SolidColorBrush TextSecBrush = new(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush AttentionBrush = new(Color.FromArgb(0xFF, 0xFF, 0xC8, 0x3B));

    private readonly Microsoft.Windows.ApplicationModel.Resources.ResourceLoader strings = new();
    private SessionSource? source;
    private string? loadedPath;

    private sealed class VisualHitItem
    {
        public int Lane { get; set; }
        public double X1 { get; set; }
        public double X2 { get; set; }
        public double Y { get; set; }
        public double StartMs { get; set; }
        public double EndMs { get; set; }
        public int ClusterCount { get; set; }
        public TimelineActivity Dominant { get; set; } = null!;
        public bool IsDuration { get; set; }
    }

    private readonly List<VisualHitItem> xamlVisualItems = [];
    private readonly List<VisualHitItem> compVisualItems = [];
    private readonly List<VisualHitItem> otherVisualItems = [];

    // Timeline data
    private IReadOnlyList<TimelineActivity> allActivities = [];
    private IReadOnlyList<TimelineActivity> xamlActivities = [];
    private IReadOnlyList<TimelineActivity> compActivities = [];
    private IReadOnlyList<TimelineActivity> otherActivities = [];
    private double maxXamlDuration;
    private double maxCompDuration;
    private double maxOtherDuration;
    private double totalMinMs;
    private double totalMaxMs;
    private bool renderPending;

    // Viewport & Selection state
    private double viewStartMs;
    private double viewEndMs;

    // Interaction states
    private bool isPanning;
    private Point panStartPoint;
    private double panStartViewStart;
    private double panStartViewEnd;

    private enum OverviewDragMode { None, NewRange, LeftHandle, RightHandle, WindowBody }
    private OverviewDragMode overviewDrag = OverviewDragMode.None;
    private double overviewDragStartX;
    private double overviewDragStartViewStart;
    private double overviewDragStartViewEnd;

    private TimelineActivity? selectedActivity;
    private CancellationTokenSource? loadingCts;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;

    private static bool IsControlPressed()
    {
        try { return (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0; }
        catch { return false; }
    }

    private static bool IsShiftPressed()
    {
        try { return (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0; }
        catch { return false; }
    }

    public event EventHandler<ObjectMention>? ObjectRequested;

    private double lockedVerticalOffset = 0;
    private bool isHandlingHorizontalPan = false;

    public TimelineView()
    {
        InitializeComponent();
        TracksScrollViewer.ViewChanged += (_, _) =>
        {
            HideHover();
            if (isHandlingHorizontalPan && Math.Abs(TracksScrollViewer.VerticalOffset - lockedVerticalOffset) > 0.5)
            {
                TracksScrollViewer.ChangeView(null, lockedVerticalOffset, null, disableAnimation: true);
            }
            else if (!isHandlingHorizontalPan)
            {
                lockedVerticalOffset = TracksScrollViewer.VerticalOffset;
            }
        };
        SetEmptyState(true, Text("TimelineEmptyNotice"));
    }

    public SessionSource? Source
    {
        get => source;
        set
        {
            if (source is not null)
            {
                source.Changed -= Source_Changed;
            }

            source = value;
            if (source is not null)
            {
                source.Changed += Source_Changed;
                LoadSession();
            }
        }
    }

    private void Source_Changed(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(LoadSession);

    private void LoadSession()
    {
        if (source is null || source.Path is null)
        {
            loadedPath = null;
            SetEmptyState(true, Text("TimelineEmptyNotice"));
            return;
        }

        if (source.Live)
        {
            loadedPath = null;
            SetEmptyState(true, "Timeline will load automatically once the profiled executable disconnects.");
            return;
        }

        var path = source.Path;
        if (path == loadedPath && allActivities.Count > 0)
        {
            return;
        }

        loadedPath = path;
        loadingCts?.Cancel();
        var cts = new CancellationTokenSource();
        loadingCts = cts;

        SetEmptyState(true, Text("TimelineReadingNotice"), isLoading: true);

        Task.Run(() =>
        {
            try
            {
                var rawEvents = SessionFile.ReadTimelineRawEvents(path, cts.Token);
                var (activities, minMs, maxMs) = TimelineActivityBuilder.Build(rawEvents);
                return (activities, minMs, maxMs);
            }
            catch (OperationCanceledException)
            {
                return (activities: (IReadOnlyList<TimelineActivity>)[], minMs: 0.0, maxMs: 0.0);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read timeline: {ex.Message}", ex);
            }
        }, cts.Token).ContinueWith(t =>
        {
            if (cts.Token.IsCancellationRequested) return;

            if (t.IsFaulted)
            {
                SetEmptyState(true, t.Exception?.InnerException?.Message ?? "Failed to read timeline.");
                return;
            }

            var (activities, minMs, maxMs) = t.Result;
            if (activities.Count == 0)
            {
                SetEmptyState(true, "No events found in trace session.");
                return;
            }

            allActivities = activities;
            xamlActivities = activities.Where(a => a.Bucket == "Xaml").OrderBy(a => a.StartMs).ToList();
            compActivities = activities.Where(a => a.Bucket == "Composition").OrderBy(a => a.StartMs).ToList();
            otherActivities = activities.Where(a => a.Bucket == "Other").OrderBy(a => a.StartMs).ToList();

            maxXamlDuration = xamlActivities.Count > 0 ? xamlActivities.Max(a => a.DurationMs) : 0;
            maxCompDuration = compActivities.Count > 0 ? compActivities.Max(a => a.DurationMs) : 0;
            maxOtherDuration = otherActivities.Count > 0 ? otherActivities.Max(a => a.DurationMs) : 0;

            totalMinMs = minMs;
            totalMaxMs = Math.Max(minMs + 0.1, maxMs);

            viewStartMs = totalMinMs;
            viewEndMs = totalMaxMs;
            selectedActivity = null;

            SetEmptyState(false);
            UpdateTrackHeights();
            UpdateOverviewGraph();
            UpdateOverviewSelection();
            RenderAll();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void SetEmptyState(bool empty, string message = "", bool isLoading = false)
    {
        HideHover();
        StatusNoticeBorder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        StatusNoticeText.Text = message;
        StatusProgressRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        StatusProgressRing.IsActive = isLoading;

        OverviewContainer.Opacity = empty ? 0.3 : 1.0;
        TracksPanel.Opacity = empty ? 0.3 : 1.0;
        RulerCanvas.Opacity = empty ? 0.3 : 1.0;
        InspectorBorder.Visibility = Visibility.Collapsed;
    }

    private string Text(string key)
    {
        try { return strings.GetString(key); }
        catch { return key; }
    }

    #region Overview Rendering & Interaction

    private void OverviewContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateOverviewGraph();
        UpdateOverviewSelection();
    }

    private void UpdateOverviewGraph()
    {
        var width = OverviewContainer.ActualWidth;
        var height = OverviewContainer.ActualHeight;
        if (width <= 0 || height <= 0 || allActivities.Count == 0) return;

        OverviewStartText.Text = $"{totalMinMs:N1} ms";
        OverviewEndText.Text = $"{totalMaxMs:N1} ms";

        double duration = Math.Max(0.001, totalMaxMs - totalMinMs);
        int bins = OverviewBins;
        var xamlCounts = new int[bins];
        var compCounts = new int[bins];
        var otherCounts = new int[bins];

        foreach (var act in allActivities)
        {
            int binStart = (int)Math.Clamp((act.StartMs - totalMinMs) / duration * bins, 0, bins - 1);
            int binEnd = (int)Math.Clamp((act.EndMs - totalMinMs) / duration * bins, 0, bins - 1);

            for (int b = binStart; b <= binEnd; b++)
            {
                if (act.Bucket == "Xaml") xamlCounts[b]++;
                else if (act.Bucket == "Composition") compCounts[b]++;
                else otherCounts[b]++;
            }
        }

        int maxCount = 1;
        for (int i = 0; i < bins; i++)
        {
            if (xamlCounts[i] > maxCount) maxCount = xamlCounts[i];
            if (compCounts[i] > maxCount) maxCount = compCounts[i];
            if (otherCounts[i] > maxCount) maxCount = otherCounts[i];
        }

        OverviewXamlPath.Data = CreateLineGeometry(xamlCounts, maxCount, width, height);
        OverviewCompPath.Data = CreateLineGeometry(compCounts, maxCount, width, height);
        OverviewOtherPath.Data = CreateLineGeometry(otherCounts, maxCount, width, height);
    }

    private static PathGeometry CreateLineGeometry(int[] counts, int maxCount, double width, double height)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { IsClosed = false };
        double stepX = width / Math.Max(1, counts.Length - 1);

        for (int i = 0; i < counts.Length; i++)
        {
            double x = i * stepX;
            double norm = (double)counts[i] / maxCount;
            double y = height - (norm * (height - 8)) - 4;

            if (i == 0)
            {
                figure.StartPoint = new Point(x, y);
            }
            else
            {
                figure.Segments.Add(new LineSegment { Point = new Point(x, y) });
            }
        }

        geometry.Figures.Add(figure);
        return geometry;
    }

    private void UpdateOverviewSelection()
    {
        var width = OverviewContainer.ActualWidth;
        if (width <= 0 || allActivities.Count == 0) return;

        double totalDuration = Math.Max(0.001, totalMaxMs - totalMinMs);

        // Always show the visible range window
        SelDimLeft.Visibility = Visibility.Visible;
        SelDimRight.Visibility = Visibility.Visible;
        SelWindow.Visibility = Visibility.Visible;
        SelLeftHandle.Visibility = Visibility.Visible;
        SelRightHandle.Visibility = Visibility.Visible;

        bool isZoomed = (viewEndMs - viewStartMs) < (totalMaxMs - totalMinMs - 0.1);
        ClearSelectionButton.Visibility = isZoomed ? Visibility.Visible : Visibility.Collapsed;

        double xLeft = Math.Clamp((viewStartMs - totalMinMs) / totalDuration * width, 0, width);
        double xRight = Math.Clamp((viewEndMs - totalMinMs) / totalDuration * width, 0, width);
        if (xRight < xLeft) (xLeft, xRight) = (xRight, xLeft);

        Canvas.SetLeft(SelDimLeft, 0);
        SelDimLeft.Width = xLeft;

        Canvas.SetLeft(SelWindow, xLeft);
        SelWindow.Width = Math.Max(2, xRight - xLeft);

        Canvas.SetLeft(SelLeftHandle, Math.Max(0, xLeft - 4));
        Canvas.SetLeft(SelRightHandle, Math.Min(width - 8, xRight - 4));

        Canvas.SetLeft(SelDimRight, xRight);
        SelDimRight.Width = Math.Max(0, width - xRight);
    }

    private void Overview_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var width = OverviewContainer.ActualWidth;
        if (width <= 0 || allActivities.Count == 0) return;

        var pt = e.GetCurrentPoint(OverviewContainer);
        if (!pt.Properties.IsLeftButtonPressed) return;

        Focus(FocusState.Programmatic);
        HideHover();
        overviewDragStartX = pt.Position.X;
        overviewDragStartViewStart = viewStartMs;
        overviewDragStartViewEnd = viewEndMs;
        double totalDuration = Math.Max(0.001, totalMaxMs - totalMinMs);

        double xLeft = Math.Clamp((viewStartMs - totalMinMs) / totalDuration * width, 0, width);
        double xRight = Math.Clamp((viewEndMs - totalMinMs) / totalDuration * width, 0, width);
        if (xRight < xLeft) (xLeft, xRight) = (xRight, xLeft);

        if (Math.Abs(pt.Position.X - xLeft) <= 10)
        {
            overviewDrag = OverviewDragMode.LeftHandle;
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        }
        else if (Math.Abs(pt.Position.X - xRight) <= 10)
        {
            overviewDrag = OverviewDragMode.RightHandle;
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        }
        else if (pt.Position.X > xLeft && pt.Position.X < xRight)
        {
            overviewDrag = OverviewDragMode.WindowBody;
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        }
        else
        {
            overviewDrag = OverviewDragMode.NewRange;
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
        }

        OverviewContainer.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Overview_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var width = OverviewContainer.ActualWidth;
        if (width <= 0 || allActivities.Count == 0) return;

        var pt = e.GetCurrentPoint(OverviewContainer);
        double totalDuration = Math.Max(0.001, totalMaxMs - totalMinMs);

        if (overviewDrag == OverviewDragMode.None)
        {
            double xLeft = Math.Clamp((viewStartMs - totalMinMs) / totalDuration * width, 0, width);
            double xRight = Math.Clamp((viewEndMs - totalMinMs) / totalDuration * width, 0, width);
            if (xRight < xLeft) (xLeft, xRight) = (xRight, xLeft);

            if (Math.Abs(pt.Position.X - xLeft) <= 10 || Math.Abs(pt.Position.X - xRight) <= 10)
            {
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
            }
            else if (pt.Position.X > xLeft && pt.Position.X < xRight)
            {
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
            }
            else
            {
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
            }
            return;
        }

        if (overviewDrag == OverviewDragMode.NewRange)
        {
            double x1 = overviewDragStartX;
            double x2 = pt.Position.X;
            double minX = Math.Min(x1, x2);
            double maxX = Math.Max(x1, x2);

            double t1 = totalMinMs + Math.Clamp(minX / width, 0, 1) * totalDuration;
            double t2 = totalMinMs + Math.Clamp(maxX / width, 0, 1) * totalDuration;

            if (maxX - minX >= 4)
            {
                viewStartMs = t1;
                viewEndMs = Math.Max(t1 + 0.1, t2);
                RequestRender();
            }
        }
        else if (overviewDrag == OverviewDragMode.LeftHandle)
        {
            double deltaX = pt.Position.X - overviewDragStartX;
            double deltaMs = (deltaX / width) * totalDuration;
            double newStart = Math.Clamp(overviewDragStartViewStart + deltaMs, totalMinMs, viewEndMs - 0.1);
            viewStartMs = newStart;
            RequestRender();
        }
        else if (overviewDrag == OverviewDragMode.RightHandle)
        {
            double deltaX = pt.Position.X - overviewDragStartX;
            double deltaMs = (deltaX / width) * totalDuration;
            double newEnd = Math.Clamp(overviewDragStartViewEnd + deltaMs, viewStartMs + 0.1, totalMaxMs);
            viewEndMs = newEnd;
            RequestRender();
        }
        else if (overviewDrag == OverviewDragMode.WindowBody)
        {
            double deltaX = pt.Position.X - overviewDragStartX;
            double deltaMs = (deltaX / width) * totalDuration;
            double span = overviewDragStartViewEnd - overviewDragStartViewStart;
            double newStart = overviewDragStartViewStart + deltaMs;
            if (newStart < totalMinMs) newStart = totalMinMs;
            if (newStart + span > totalMaxMs) newStart = totalMaxMs - span;
            viewStartMs = newStart;
            viewEndMs = newStart + span;
            RequestRender();
        }

        e.Handled = true;
    }

    private void Overview_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (overviewDrag != OverviewDragMode.None)
        {
            var mode = overviewDrag;
            overviewDrag = OverviewDragMode.None;
            OverviewContainer.ReleasePointerCapture(e.Pointer);

            var pt = e.GetCurrentPoint(OverviewContainer);
            var width = OverviewContainer.ActualWidth;

            if (mode == OverviewDragMode.NewRange && Math.Abs(pt.Position.X - overviewDragStartX) < 4 && width > 0)
            {
                double totalDuration = Math.Max(0.001, totalMaxMs - totalMinMs);
                double clickMs = totalMinMs + Math.Clamp(pt.Position.X / width, 0, 1) * totalDuration;
                double winSpan = totalDuration * 0.2;
                viewStartMs = Math.Max(totalMinMs, clickMs - winSpan / 2);
                viewEndMs = Math.Min(totalMaxMs, clickMs + winSpan / 2);
                RequestRender();
            }

            ProtectedCursor = null;
            e.Handled = true;
        }
    }

    private void Overview_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        overviewDrag = OverviewDragMode.None;
        ProtectedCursor = null;
    }

    private void Overview_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (overviewDrag == OverviewDragMode.None)
        {
            ProtectedCursor = null;
        }
    }

    private void Overview_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(OverviewContainer);
        int delta = pt.Properties.MouseWheelDelta;
        if (delta == 0) return;

        bool isShift = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) || IsShiftPressed();
        bool isHorizontal = pt.Properties.IsHorizontalMouseWheel || isShift;

        if (isHorizontal)
        {
            double duration = viewEndMs - viewStartMs;
            double fraction = (delta / 120.0) * 0.10;
            PanByDeltaMs(duration * fraction);
            e.Handled = true;
            return;
        }

        double mouseX = pt.Position.X;
        double width = OverviewContainer.ActualWidth;
        if (width <= 0) width = ActualWidth;
        if (width <= 0) return;

        double factor = delta > 0 ? 0.75 : 1.333;
        ZoomAroundPoint(mouseX, width, factor);
        e.Handled = true;
    }

    private void Overview_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        ResetToFullExtent();
        e.Handled = true;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e) => ResetToFullExtent();

    private void ResetToFullExtent()
    {
        HideHover();
        viewStartMs = totalMinMs;
        viewEndMs = totalMaxMs;
        UpdateOverviewSelection();
        RenderAll();
    }

    #endregion

    #region Ruler Rendering

    private void RulerCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderRuler();

    private void Ruler_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(RulerCanvas);
        int delta = pt.Properties.MouseWheelDelta;
        if (delta == 0) return;

        bool isShift = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) || IsShiftPressed();
        bool isHorizontal = pt.Properties.IsHorizontalMouseWheel || isShift;

        if (isHorizontal)
        {
            double duration = viewEndMs - viewStartMs;
            double fraction = (delta / 120.0) * 0.10;
            PanByDeltaMs(duration * fraction);
            e.Handled = true;
            return;
        }

        double mouseX = pt.Position.X;
        double width = RulerCanvas.ActualWidth;
        if (width <= 0) width = ActualWidth;
        if (width <= 0) return;

        double factor = delta > 0 ? 0.75 : 1.333;
        ZoomAroundPoint(mouseX, width, factor);
        e.Handled = true;
    }

    private void RenderRuler()
    {
        RulerCanvas.Children.Clear();
        var width = RulerCanvas.ActualWidth;
        var height = RulerCanvas.ActualHeight;
        if (width <= 0 || height <= 0 || allActivities.Count == 0) return;

        double duration = Math.Max(0.001, viewEndMs - viewStartMs);
        double targetTickSpacingPx = 80;
        double approxTicks = Math.Max(1, width / targetTickSpacingPx);
        double rawInterval = duration / approxTicks;
        double niceInterval = CalculateNiceInterval(rawInterval);
        if (niceInterval <= 1e-6 || double.IsNaN(niceInterval) || double.IsInfinity(niceInterval))
        {
            niceInterval = 1.0;
        }

        double firstTick = Math.Ceiling(viewStartMs / niceInterval) * niceInterval;
        int tickCount = 0;

        for (double t = firstTick; t <= viewEndMs && tickCount < 100; t += niceInterval, tickCount++)
        {
            double x = (t - viewStartMs) / duration * width;
            if (x < 0 || x > width) continue;

            // Tick mark
            var tickPath = new Line
            {
                X1 = x,
                Y1 = height - 8,
                X2 = x,
                Y2 = height,
                Stroke = TextSecBrush,
                StrokeThickness = 1
            };
            RulerCanvas.Children.Add(tickPath);

            // Tick text
            var tb = new TextBlock
            {
                Text = FormatTime(t),
                FontSize = 10,
                Foreground = TextSecBrush
            };
            Canvas.SetLeft(tb, x + 3);
            Canvas.SetTop(tb, 2);
            RulerCanvas.Children.Add(tb);
        }
    }

    private static double CalculateNiceInterval(double raw)
    {
        if (raw <= 1e-9 || double.IsNaN(raw) || double.IsInfinity(raw)) return 1.0;
        double exponent = Math.Floor(Math.Log10(raw));
        double fraction = raw / Math.Pow(10, exponent);
        double niceFraction = fraction switch
        {
            < 1.5 => 1,
            < 3 => 2,
            < 7 => 5,
            _ => 10
        };
        double val = niceFraction * Math.Pow(10, exponent);
        return val <= 0 || double.IsNaN(val) ? 1.0 : val;
    }

    private static string FormatTime(double ms)
    {
        if (Math.Abs(ms) >= 1000) return $"{(ms / 1000):F2} s";
        if (Math.Abs(ms) >= 1) return $"{ms:F1} ms";
        return $"{ms:F3} ms";
    }

    #endregion

    #region Tracks Rendering

    private void TrackCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RequestRender();
    }

    private void RequestRender()
    {
        UpdateToolbarStatus();
        UpdateOverviewSelection();

        if (renderPending) return;
        renderPending = true;

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            renderPending = false;
            RenderAll();
        });
    }

    private void RenderAll()
    {
        try
        {
            renderPending = false;
            UpdateToolbarStatus();
            RenderRuler();
            RenderTrack(XamlCanvas, xamlActivities, maxXamlDuration, "Xaml", XamlBrush, XamlFill);
            RenderTrack(CompositionCanvas, compActivities, maxCompDuration, "Composition", CompBrush, CompFill);
            RenderTrack(OtherCanvas, otherActivities, maxOtherDuration, "Other", OtherBrush, OtherFill);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"[TimelineView.RenderAll] {ex}");
        }
    }

    private static int FindFirstStartIndex(IReadOnlyList<TimelineActivity> list, double minStartMs)
    {
        int low = 0;
        int high = list.Count - 1;
        int result = list.Count;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            if (list[mid].StartMs >= minStartMs)
            {
                result = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return result;
    }

    private int CountVisible(IReadOnlyList<TimelineActivity> list, double maxDuration)
    {
        if (list.Count == 0) return 0;
        if (viewStartMs <= totalMinMs && viewEndMs >= totalMaxMs) return list.Count;

        int startIdx = FindFirstStartIndex(list, viewStartMs - maxDuration);
        int count = 0;
        for (int i = startIdx; i < list.Count; i++)
        {
            var act = list[i];
            if (act.StartMs > viewEndMs) break;
            if (act.EndMs >= viewStartMs) count++;
        }
        return count;
    }

    private void UpdateToolbarStatus()
    {
        double duration = Math.Max(0, viewEndMs - viewStartMs);
        VisibleRangeText.Text = $"Visible: {viewStartMs:N1} ms – {viewEndMs:N1} ms ({duration:N1} ms)";

        int visibleXaml = CountVisible(xamlActivities, maxXamlDuration);
        int visibleComp = CountVisible(compActivities, maxCompDuration);
        int visibleOther = CountVisible(otherActivities, maxOtherDuration);
        int totalVisible = visibleXaml + visibleComp + visibleOther;

        EventCountsSummary.Text = $"Showing {totalVisible:N0} events (Xaml: {visibleXaml:N0}, Comp: {visibleComp:N0}, Other: {visibleOther:N0})";
        XamlCountText.Text = $"({visibleXaml:N0})";
        CompCountText.Text = $"({visibleComp:N0})";
        OtherCountText.Text = $"({visibleOther:N0})";
    }

    private void RenderTrack(Canvas canvas, IReadOnlyList<TimelineActivity> activities, double maxDuration, string bucket, Brush strokeBrush, Brush fillBrush)
    {
        canvas.Children.Clear();
        var visualItems = ReferenceEquals(canvas, XamlCanvas) ? xamlVisualItems
            : ReferenceEquals(canvas, CompositionCanvas) ? compVisualItems : otherVisualItems;
        visualItems.Clear();

        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (width <= 0 || height <= 0 || activities.Count == 0) return;

        double duration = Math.Max(0.001, viewEndMs - viewStartMs);
        double pxPerMs = width / duration;

        // Viewport culling using binary search in O(log N)
        int startIdx = FindFirstStartIndex(activities, viewStartMs - maxDuration);
        if (startIdx >= activities.Count) return;

        // Group visible activities by lane depth
        var laneDurationMap = new Dictionary<int, List<TimelineActivity>>();
        var laneInstantMap = new Dictionary<int, List<TimelineActivity>>();

        for (int i = startIdx; i < activities.Count; i++)
        {
            var act = activities[i];
            if (act.StartMs > viewEndMs) break;
            if (act.EndMs < viewStartMs) continue;

            if (act.IsDuration)
            {
                if (!laneDurationMap.TryGetValue(act.Depth, out var dList))
                {
                    dList = new List<TimelineActivity>();
                    laneDurationMap[act.Depth] = dList;
                }
                dList.Add(act);
            }
            else
            {
                if (!laneInstantMap.TryGetValue(act.Depth, out var iList))
                {
                    iList = new List<TimelineActivity>();
                    laneInstantMap[act.Depth] = iList;
                }
                iList.Add(act);
            }
        }

        if (laneDurationMap.Count == 0 && laneInstantMap.Count == 0) return;

        // Batched geometries for high-performance rendering (60fps with 50,000+ events)
        var durationBlocksGeometry = new GeometryGroup();
        var pointTicksGeometry = new GeometryGroup();

        var durationPath = new ShapePath
        {
            Data = durationBlocksGeometry,
            Fill = fillBrush,
            Stroke = strokeBrush,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        canvas.Children.Add(durationPath);

        var ticksPath = new ShapePath
        {
            Data = pointTicksGeometry,
            Fill = strokeBrush,
            Stroke = strokeBrush,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        canvas.Children.Add(ticksPath);

        int textCount = 0;
        const int maxTextElements = 75;

        // Process duration events lane by lane with pixel-level clustering (LOD)
        foreach (var (depth, laneActs) in laneDurationMap)
        {
            double y = depth * (LaneHeight + LaneSpacing) + TrackTopPadding;

            double curClusterX1 = double.NaN;
            double curClusterX2 = double.NaN;
            double curClusterStartMs = 0;
            double curClusterEndMs = 0;
            TimelineActivity? dominant = null;
            double maxDur = 0;
            int clusterCount = 0;

            void FlushCluster()
            {
                if (double.IsNaN(curClusterX1)) return;

                double w = Math.Max(2.0, curClusterX2 - curClusterX1);
                durationBlocksGeometry.Children.Add(new RectangleGeometry
                {
                    Rect = new Rect(curClusterX1, y, w, LaneHeight)
                });

                visualItems.Add(new VisualHitItem
                {
                    Lane = depth,
                    X1 = curClusterX1,
                    X2 = curClusterX2,
                    Y = y,
                    StartMs = curClusterStartMs,
                    EndMs = curClusterEndMs,
                    ClusterCount = clusterCount,
                    Dominant = dominant!,
                    IsDuration = true
                });

                if (w >= 36 && textCount < maxTextElements && curClusterX2 > 0 && curClusterX1 < width)
                {
                    double labelX = Math.Max(curClusterX1 + 3, 2);
                    double visibleRight = Math.Min(curClusterX2 - 3, width - 2);
                    double labelMaxWidth = Math.Max(0, visibleRight - labelX);

                    if (labelMaxWidth >= 20)
                    {
                        string labelText = clusterCount > 1
                            ? $"{dominant!.Name} (+{clusterCount - 1})"
                            : $"{dominant!.Name} ({dominant.DurationMs:N1} ms)";

                        var label = new TextBlock
                        {
                            Text = labelText,
                            FontSize = 10,
                            Foreground = strokeBrush,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            IsHitTestVisible = false,
                            MaxWidth = labelMaxWidth
                        };
                        Canvas.SetLeft(label, labelX);
                        Canvas.SetTop(label, y + 1);
                        canvas.Children.Add(label);
                        textCount++;
                    }
                }
            }

            foreach (var act in laneActs)
            {
                double rawX1 = (act.StartMs - viewStartMs) * pxPerMs;
                double rawX2 = (act.EndMs - viewStartMs) * pxPerMs;
                if (double.IsNaN(rawX1) || double.IsInfinity(rawX1) || double.IsNaN(rawX2) || double.IsInfinity(rawX2)) continue;

                double x1 = Math.Clamp(rawX1, -20, width + 20);
                double x2 = Math.Clamp(rawX2, -20, width + 20);
                double w = Math.Max(2.0, x2 - x1);
                x2 = x1 + w;

                if (double.IsNaN(curClusterX1))
                {
                    curClusterX1 = x1;
                    curClusterX2 = x2;
                    curClusterStartMs = act.StartMs;
                    curClusterEndMs = act.EndMs;
                    dominant = act;
                    maxDur = act.DurationMs;
                    clusterCount = 1;
                }
                else if (x1 <= curClusterX2 + 1.5) // Merge touching / overlapping pixels
                {
                    curClusterX2 = Math.Max(curClusterX2, x2);
                    if (act.EndMs > curClusterEndMs) curClusterEndMs = act.EndMs;
                    if (act.StartMs < curClusterStartMs) curClusterStartMs = act.StartMs;
                    clusterCount++;
                    if (act.DurationMs > maxDur)
                    {
                        dominant = act;
                        maxDur = act.DurationMs;
                    }
                }
                else
                {
                    FlushCluster();
                    curClusterX1 = x1;
                    curClusterX2 = x2;
                    curClusterStartMs = act.StartMs;
                    curClusterEndMs = act.EndMs;
                    dominant = act;
                    maxDur = act.DurationMs;
                    clusterCount = 1;
                }
            }
            FlushCluster();
        }

        // Process instant events lane by lane with sub-pixel coalescing
        foreach (var (depth, laneActs) in laneInstantMap)
        {
            double y = depth * (LaneHeight + LaneSpacing) + TrackTopPadding;

            double lastTickX = double.NegativeInfinity;

            foreach (var act in laneActs)
            {
                double x = (act.StartMs - viewStartMs) * pxPerMs;
                if (double.IsNaN(x) || double.IsInfinity(x)) continue;
                if (x < -5 || x > width + 5) continue;

                if (x - lastTickX < 1.5) continue; // Skip duplicate subpixel tick
                lastTickX = x;

                pointTicksGeometry.Children.Add(new RectangleGeometry
                {
                    Rect = new Rect(x - 1, y, 2, LaneHeight)
                });

                visualItems.Add(new VisualHitItem
                {
                    Lane = depth,
                    X1 = x - 1,
                    X2 = x + 1,
                    Y = y,
                    StartMs = act.StartMs,
                    EndMs = act.EndMs,
                    ClusterCount = 1,
                    Dominant = act,
                    IsDuration = false
                });
            }
        }

        // Highlight selected activity if visible on this track
        if (selectedActivity is not null && selectedActivity.Bucket == bucket)
        {
            var selItem = visualItems.FirstOrDefault(v => ReferenceEquals(v.Dominant, selectedActivity)
                || (selectedActivity.EndMs >= v.StartMs && selectedActivity.StartMs <= v.EndMs && selectedActivity.Depth == v.Lane));

            if (selItem is not null)
            {
                var highlight = new Border
                {
                    Width = Math.Clamp(selItem.X2 - selItem.X1 + 4, 4, width + 100),
                    Height = LaneHeight + 4,
                    BorderBrush = AttentionBrush,
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(3),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(highlight, selItem.X1 - 2);
                Canvas.SetTop(highlight, selItem.Y - 2);
                canvas.Children.Add(highlight);
            }
        }
    }

    #endregion

    #region Mouse & Keyboard Navigation

    private void Tracks_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        double deltaX = e.Delta.Translation.X;
        if (Math.Abs(deltaX) > 0.001)
        {
            HideHover();
            double width = TracksScrollViewer.ActualWidth;
            if (width <= 0) width = ActualWidth;
            if (width <= 0) width = 800;

            double duration = viewEndMs - viewStartMs;
            // Swiping fingers left (deltaX < 0) advances timeline forward (increases time).
            // Swiping fingers right (deltaX > 0) moves timeline backward (decreases time).
            double deltaMs = -(deltaX / width) * duration;
            PanByDeltaMs(deltaMs);
            e.Handled = true;
        }
    }

    private void Tracks_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(TracksScrollViewer);
        int delta = pt.Properties.MouseWheelDelta;
        if (delta == 0) return;

        bool isShift = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) || IsShiftPressed();
        bool isHorizontal = pt.Properties.IsHorizontalMouseWheel || isShift;

        if (isHorizontal)
        {
            isHandlingHorizontalPan = true;
            lockedVerticalOffset = TracksScrollViewer.VerticalOffset;

            double duration = viewEndMs - viewStartMs;
            // Horizontal wheel / tilt: delta > 0 (tilted right) advances timeline forward
            // Shift + vertical wheel: delta < 0 (rolled down/back) advances timeline forward
            double sign = pt.Properties.IsHorizontalMouseWheel ? 1.0 : -1.0;
            double fraction = sign * (delta / 120.0) * 0.10;
            PanByDeltaMs(duration * fraction);
            e.Handled = true;

            DispatcherQueue.TryEnqueue(() => isHandlingHorizontalPan = false);
            return;
        }

        bool isCtrl = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control) || IsControlPressed();
        if (isCtrl)
        {
            double mouseX = pt.Position.X;
            double width = TracksScrollViewer.ActualWidth;
            if (width <= 0) width = ActualWidth;
            if (width <= 0) return;

            double factor = delta > 0 ? 0.75 : 1.333;
            ZoomAroundPoint(mouseX, width, factor);
            e.Handled = true;
        }
    }

    private void Track_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var canvas = (Canvas)sender;
        var pt = e.GetCurrentPoint(canvas);

        Focus(FocusState.Programmatic);
        HideHover();

        if (pt.Properties.IsLeftButtonPressed)
        {
            isPanning = true;
            panStartPoint = pt.Position;
            panStartViewStart = viewStartMs;
            panStartViewEnd = viewEndMs;
            canvas.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void Track_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var canvas = (Canvas)sender;
        if (isPanning)
        {
            HideHover();
            var pt = e.GetCurrentPoint(canvas);
            double deltaX = pt.Position.X - panStartPoint.X;
            double width = canvas.ActualWidth;
            if (width <= 0) return;

            double duration = panStartViewEnd - panStartViewStart;
            double deltaMs = -(deltaX / width) * duration;

            double minBound = totalMinMs;
            double maxBound = totalMaxMs;

            double newStart = panStartViewStart + deltaMs;
            double newEnd = panStartViewEnd + deltaMs;

            if (newStart < minBound)
            {
                newStart = minBound;
                newEnd = minBound + duration;
            }
            if (newEnd > maxBound)
            {
                newEnd = maxBound;
                newStart = Math.Max(minBound, maxBound - duration);
            }

            viewStartMs = newStart;
            viewEndMs = newEnd;
            RequestRender();
            e.Handled = true;
            return;
        }

        UpdateHover(canvas, e);
    }

    private void Track_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var canvas = (Canvas)sender;
        if (isPanning)
        {
            isPanning = false;
            canvas.ReleasePointerCapture(e.Pointer);

            var pt = e.GetCurrentPoint(canvas);
            // If mouse didn't drag far, treat as click to select activity
            if (Math.Abs(pt.Position.X - panStartPoint.X) < 4 && Math.Abs(pt.Position.Y - panStartPoint.Y) < 4)
            {
                HitTestActivity(canvas, pt.Position);
            }
            e.Handled = true;
        }
        else
        {
            var pt = e.GetCurrentPoint(canvas);
            HitTestActivity(canvas, pt.Position);
        }
    }

    private void Track_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        isPanning = false;
        HideHover();
    }

    private void Track_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        HideHover();
    }

    private VisualHitItem? FindVisualItemAtPoint(Canvas canvas, Point clickPos)
    {
        var items = ReferenceEquals(canvas, XamlCanvas) ? xamlVisualItems
            : ReferenceEquals(canvas, CompositionCanvas) ? compVisualItems : otherVisualItems;

        if (items.Count == 0) return null;

        double mouseX = clickPos.X;
        double mouseY = clickPos.Y;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            // Strict vertical test: cursor must be within the item's lane bounds
            if (mouseY < item.Y || mouseY > item.Y + LaneHeight)
            {
                continue;
            }

            // Strict horizontal test: cursor must be directly on top of the block
            // For duration blocks: strictly between X1 and X2
            // For instant ticks: on the tick stroke
            double left = item.IsDuration ? item.X1 : item.X1 - 1;
            double right = item.IsDuration ? item.X2 : item.X2 + 1;

            if (mouseX >= left && mouseX <= right)
            {
                return item;
            }
        }

        return null;
    }

    private void UpdateHover(Canvas canvas, PointerRoutedEventArgs e)
    {
        if (isPanning || allActivities.Count == 0)
        {
            HideHover();
            return;
        }

        var pt = e.GetCurrentPoint(canvas);
        var item = FindVisualItemAtPoint(canvas, pt.Position);

        if (item is null)
        {
            HideHover();
            return;
        }

        ShowHover(canvas, item, e);
    }

    private void ShowHover(Canvas canvas, VisualHitItem item, PointerRoutedEventArgs e)
    {
        try
        {
            var transform = canvas.TransformToVisual(HoverOverlayCanvas);
            var overlayTopLeft = transform.TransformPoint(new Point(item.X1, item.Y));

            double w = Math.Max(item.IsDuration ? 4.0 : 3.0, item.X2 - item.X1);
            Canvas.SetLeft(HoverHighlight, overlayTopLeft.X - 1);
            Canvas.SetTop(HoverHighlight, overlayTopLeft.Y - 1);
            HoverHighlight.Width = w + 2;
            HoverHighlight.Height = LaneHeight + 2;
            HoverHighlight.Visibility = Visibility.Visible;

            var act = item.Dominant;
            HoverTitle.Text = act.IsDuration ? CleanSpanEventName(act.Name) : act.Name;
            HoverBucketText.Text = act.Bucket;
            HoverBucketBadge.Background = act.Bucket switch
            {
                "Xaml" => XamlBrush,
                "Composition" => CompBrush,
                _ => OtherBrush
            };

            if (item.IsDuration)
            {
                if (item.ClusterCount > 1)
                {
                    double totalSpanMs = Math.Max(0.001, item.EndMs - item.StartMs);
                    HoverDuration.Text = $"{totalSpanMs:N2} ms (cluster span)";
                    HoverTimeSpan.Text = $"{item.StartMs:N2} ms → {item.EndMs:N2} ms (+{item.ClusterCount - 1} other events; zoom in for details)";
                }
                else
                {
                    HoverDuration.Text = $"{act.DurationMs:N3} ms";
                    HoverTimeSpan.Text = $"{act.StartMs:N2} ms → {act.EndMs:N2} ms";
                }
            }
            else
            {
                HoverDuration.Text = "Instant event";
                HoverTimeSpan.Text = $"@ {act.StartMs:N2} ms";
            }

            HoverMetaThread.Text = $"Thread: {act.ThreadId}";
            HoverMetaProvider.Text = act.Provider.ToString();

            var pointerInOverlay = e.GetCurrentPoint(HoverOverlayCanvas).Position;
            double cardX = pointerInOverlay.X + 16;
            double cardY = pointerInOverlay.Y + 16;

            double overlayW = HoverOverlayCanvas.ActualWidth;
            double overlayH = HoverOverlayCanvas.ActualHeight;

            const double estCardW = 320;
            const double estCardH = 80;

            double rightLimit = overlayW - 8;
            if (InspectorBorder is not null && InspectorBorder.Visibility == Visibility.Visible && InspectorBorder.ActualWidth > 0)
            {
                rightLimit = Math.Max(8, overlayW - InspectorBorder.ActualWidth - 12);
            }

            if (overlayW > 0 && cardX + estCardW > rightLimit)
            {
                cardX = Math.Max(8, pointerInOverlay.X - estCardW - 12);
            }
            if (overlayH > 0 && cardY + estCardH > overlayH - 8)
            {
                cardY = Math.Max(8, pointerInOverlay.Y - estCardH - 12);
            }

            Canvas.SetLeft(HoverCard, cardX);
            Canvas.SetTop(HoverCard, cardY);
            HoverCard.Visibility = Visibility.Visible;

            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        }
        catch
        {
            HideHover();
        }
    }

    private void HideHover()
    {
        if (HoverCard is not null && HoverCard.Visibility != Visibility.Collapsed)
        {
            HoverCard.Visibility = Visibility.Collapsed;
        }
        if (HoverHighlight is not null && HoverHighlight.Visibility != Visibility.Collapsed)
        {
            HoverHighlight.Visibility = Visibility.Collapsed;
        }
        if (!isPanning && overviewDrag == OverviewDragMode.None)
        {
            ProtectedCursor = null;
        }
    }

    private void HitTestActivity(Canvas canvas, Point clickPos)
    {
        var item = FindVisualItemAtPoint(canvas, clickPos);
        if (item is not null)
        {
            selectedActivity = item.Dominant;
            ShowActivityDetails(item.Dominant);
            RenderAll();
        }
    }

    private void TimelineView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Add || (int)e.Key == 187) // Plus / = / Shift+=
        {
            ZoomByFactor(0.75);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Subtract || (int)e.Key == 189) // Minus / _
        {
            ZoomByFactor(1.333);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Left) // Left Arrow
        {
            PanByFraction(-0.1);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Right) // Right Arrow
        {
            PanByFraction(0.1);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Home) // Home
        {
            ResetToFullExtent();
            e.Handled = true;
        }
    }

    private void ZoomByFactor(double factor)
    {
        var width = XamlCanvas.ActualWidth;
        if (width <= 0) width = TracksScrollViewer.ActualWidth;
        if (width <= 0) width = ActualWidth;
        if (width <= 0) width = 800;
        ZoomAroundPoint(width / 2, width, factor);
    }

    private void ZoomAroundPoint(double mouseX, double width, double factor)
    {
        if (width <= 0 || allActivities.Count == 0) return;
        HideHover();
        double minBound = totalMinMs;
        double maxBound = totalMaxMs;
        double allowedSpan = Math.Max(0.1, maxBound - minBound);

        double currentDuration = Math.Max(0.1, viewEndMs - viewStartMs);
        double newDuration = Math.Clamp(currentDuration * factor, 0.1, allowedSpan);

        double ratio = Math.Clamp(mouseX / width, 0, 1);
        double pointerTime = viewStartMs + ratio * currentDuration;

        double newStart = pointerTime - ratio * newDuration;
        double newEnd = pointerTime + (1 - ratio) * newDuration;

        if (newStart < minBound)
        {
            newStart = minBound;
            newEnd = Math.Min(maxBound, newStart + newDuration);
        }
        if (newEnd > maxBound)
        {
            newEnd = maxBound;
            newStart = Math.Max(minBound, newEnd - newDuration);
        }

        viewStartMs = newStart;
        viewEndMs = Math.Max(newStart + 0.1, newEnd);
        RequestRender();
    }

    private void PanByFraction(double fraction)
    {
        double duration = viewEndMs - viewStartMs;
        PanByDeltaMs(duration * fraction);
    }

    private void PanByDeltaMs(double delta)
    {
        HideHover();
        double duration = viewEndMs - viewStartMs;
        double minBound = totalMinMs;
        double maxBound = totalMaxMs;

        double newStart = viewStartMs + delta;
        double newEnd = viewEndMs + delta;

        if (newStart < minBound)
        {
            newStart = minBound;
            newEnd = minBound + duration;
        }
        if (newEnd > maxBound)
        {
            newEnd = maxBound;
            newStart = Math.Max(minBound, maxBound - duration);
        }

        viewStartMs = newStart;
        viewEndMs = newEnd;
        RequestRender();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomByFactor(0.75);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomByFactor(1.333);

    private void FitAll_Click(object sender, RoutedEventArgs e) => ResetToFullExtent();

    #endregion

    #region Track Heights (Full Tree Depth)

    private void UpdateTrackHeights()
    {
        SetTrackHeight(XamlTrackBorder, xamlActivities);
        SetTrackHeight(CompTrackBorder, compActivities);
        SetTrackHeight(OtherTrackBorder, otherActivities);
    }

    private static void SetTrackHeight(Border border, IReadOnlyList<TimelineActivity> activities)
    {
        int maxDepth = activities.Count > 0 ? activities.Max(a => a.Depth) : 0;
        double neededHeight = (maxDepth + 1) * (LaneHeight + LaneSpacing) + TrackTopPadding + 8;
        border.Height = Math.Max(MinTrackHeight, neededHeight);
    }

    #endregion

    #region Event Details Inspector

    private static string CleanSpanEventName(string name)
    {
        foreach (var suffix in new[] { "/Start", "_Start", "Start", "/Begin", "_Begin", "Begin" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length].TrimEnd();
            }
        }
        return name;
    }

    private void ShowActivityDetails(TimelineActivity activity)
    {
        HideHover();
        InspectorBorder.Visibility = Visibility.Visible;

        DetailBucketText.Text = activity.Bucket;
        DetailBucketBadge.Background = activity.Bucket switch
        {
            "Xaml" => XamlBrush,
            "Composition" => CompBrush,
            _ => OtherBrush
        };

        var displayName = activity.IsDuration ? CleanSpanEventName(activity.Name) : activity.Name;
        DetailTitleText.Text = displayName;
        DetailDurationText.Text = activity.IsDuration ? $"Duration: {activity.DurationMs:N3} ms" : "Instant Event";
        DetailMetaText.Text = $"Start: {activity.StartMs:N3} ms | Thread: {activity.ThreadId} | Provider: {activity.Provider}";

        DetailFieldsText.Text = "Loading details...";
        DetailObjectsList.ItemsSource = null;

        var path = loadedPath;
        if (path is null) return;

        Task.Run(() => SessionFile.ReadSingleEvent(path, activity.StartSeq))
            .ContinueWith(t =>
            {
                if (t.IsFaulted || t.Result is null)
                {
                    DetailFieldsText.Text = $"{displayName}\nNo additional field details found.";
                    return;
                }

                var (profilerEvent, bucket, mentions) = t.Result.Value;
                var row = new FrameEventRow(profilerEvent, bucket, mentions);
                string detailsText = row.Details;
                if (activity.IsDuration)
                {
                    string rawName = profilerEvent.Name;
                    string clean = CleanSpanEventName(rawName);
                    if (detailsText.StartsWith(rawName, StringComparison.Ordinal))
                    {
                        detailsText = clean + detailsText[rawName.Length..];
                    }
                }
                DetailFieldsText.Text = detailsText;
                DetailObjectsList.ItemsSource = row.Objects;
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void InspectorClose_Click(object sender, RoutedEventArgs e)
    {
        selectedActivity = null;
        InspectorBorder.Visibility = Visibility.Collapsed;
        RenderAll();
    }

    private void ObjectLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton { Tag: ObjectMention mention })
        {
            ObjectRequested?.Invoke(this, mention);
        }
    }

    #endregion
}
