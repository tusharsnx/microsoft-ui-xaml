// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
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
    private static readonly SolidColorBrush XamlFill = new(Color.FromArgb(0xFF, 0x1F, 0x38, 0x4B));
    private static readonly SolidColorBrush CompBrush = new(Color.FromArgb(0xFF, 0x87, 0x64, 0xB8));
    private static readonly SolidColorBrush CompFill = new(Color.FromArgb(0xFF, 0x3B, 0x34, 0x45));
    private static readonly SolidColorBrush OtherBrush = new(Color.FromArgb(0xFF, 0xD8, 0x3B, 0x01));
    private static readonly SolidColorBrush OtherFill = new(Color.FromArgb(0xFF, 0x4C, 0x2B, 0x1F));
    private static readonly SolidColorBrush IdleWaitBrush = new(Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0));
    private static readonly SolidColorBrush IdleWaitFill = new(Color.FromArgb(0xFF, 0x2E, 0x2E, 0x2E));
    private static readonly SolidColorBrush LockSpanBrush = new(Color.FromArgb(0xFF, 0xB3, 0x88, 0xFF));
    private static readonly SolidColorBrush LockRegionFill = new(Color.FromArgb(0xFF, 0x2F, 0x2D, 0x34));
    private static readonly SolidColorBrush FrameRequestBrush = new(Color.FromArgb(0x70, 0xA0, 0xA0, 0xA0));
    private static readonly SolidColorBrush FrameCommitBrush = new(Color.FromArgb(0xFF, 0x27, 0xC3, 0x80));
    private static readonly SolidColorBrush FrameCommitDelayedBrush = new(Color.FromArgb(0xFF, 0xFF, 0x43, 0x43));
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
        public TimelineActivity? Dominant { get; set; }
        public TimelineLockRegion? LockRegion { get; set; }
        public bool IsDuration { get; set; }
    }

    private sealed class ThreadTrackInfo
    {
        public int ThreadId { get; init; }
        public string Role { get; init; } = "Worker";
        public string Name { get; init; } = "";
        public IReadOnlyList<TimelineActivity> Activities { get; init; } = [];
        public IReadOnlyList<TimelineLockRegion> LockRegions { get; init; } = [];
        public double MaxDuration { get; init; }
        public int MaxDepth { get; init; }
        public bool IsCollapsed { get; set; }

        public Grid Container { get; set; } = null!;
        public Border Border { get; set; } = null!;
        public Canvas Canvas { get; set; } = null!;
        public FontIcon ChevronIcon { get; set; } = null!;
        public TextBlock CountText { get; set; } = null!;
    }

    private readonly Dictionary<Canvas, List<VisualHitItem>> canvasVisualItems = new();
    private readonly List<ThreadTrackInfo> threadTracks = [];

    // Timeline data
    private IReadOnlyList<TimelineActivity> allActivities = [];
    private IReadOnlyList<TimelineLockRegion> allLockRegions = [];
    private IReadOnlyList<TimelineActivity> frameActivities = [];
    private IReadOnlyList<double> frameRequestTimes = [];
    private IReadOnlyList<double> frameCommitTimes = [];
    private IReadOnlyList<double> frameCommitDelayedTimes = [];
    private bool showLockRegions;
    private TimelineLockRegion? selectedLockRegion;
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

    private async void LoadSession()
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

        try
        {
            var (threads, activities, minMs, maxMs, lockRegions) = await Task.Run(() =>
            {
                var threads = SessionFile.ReadThreads(path);
                var rawEvents = SessionFile.ReadTimelineRawEvents(path, cts.Token);
                var (activities, minMs, maxMs, lockRegions) = TimelineActivityBuilder.Build(rawEvents);
                return (threads, activities, minMs, maxMs, lockRegions);
            }, cts.Token);

            if (cts.Token.IsCancellationRequested) return;

            if (activities.Count == 0)
            {
                SetEmptyState(true, "No events found in trace session.");
                return;
            }

            allActivities = activities;
            allLockRegions = lockRegions;
            frameActivities = activities.Where(a => a.IsFrameRequest || a.IsFrameCommit).OrderBy(a => a.StartMs).ToList();
            frameRequestTimes = activities
                .Where(a => a.IsFrameRequest)
                .Select(a => a.StartMs)
                .OrderBy(time => time)
                .ToList();
            frameCommitTimes = activities
                .Where(a => a.IsFrameCommit && !a.IsDelayedFrameCommit)
                .Select(a => a.IsDuration ? a.EndMs : a.StartMs)
                .OrderBy(time => time)
                .ToList();
            frameCommitDelayedTimes = activities
                .Where(a => a.IsDelayedFrameCommit)
                .Select(a => a.IsDuration ? a.EndMs : a.StartMs)
                .OrderBy(time => time)
                .ToList();

            var threadMap = new Dictionary<int, SessionThread>();
            foreach (var th in threads)
            {
                threadMap[th.ThreadId] = th;
            }

            threadTracks.Clear();
            canvasVisualItems.Clear();
            ThreadTracksPanel.Children.Clear();

            var grouped = activities
                .GroupBy(a => a.ThreadId)
                .Select(g =>
                {
                    int tid = g.Key;
                    threadMap.TryGetValue(tid, out var sThread);
                    string role = sThread?.Role ?? "Worker";
                    string name = sThread?.Name ?? $"Thread {tid}";
                    var actList = g.OrderBy(a => a.StartMs).ToList();
                    double maxDur = actList.Count > 0 ? actList.Max(a => a.DurationMs) : 0;
                    int maxDepth = actList.Count > 0 ? actList.Max(a => a.Depth) : 0;
                    var trackLocks = lockRegions.Where(l => l.ThreadId == tid).OrderBy(l => l.StartMs).ToList();
                    return new ThreadTrackInfo
                    {
                        ThreadId = tid,
                        Role = role,
                        Name = name,
                        Activities = actList,
                        LockRegions = trackLocks,
                        MaxDuration = maxDur,
                        MaxDepth = maxDepth,
                        IsCollapsed = false
                    };
                })
                .OrderBy(trk => trk.Role switch
                {
                    "Main" => 0,
                    "Render" => 1,
                    _ => 2
                })
                .ThenByDescending(trk => trk.Activities.Count)
                .ThenBy(trk => trk.ThreadId)
                .ToList();

            threadTracks.AddRange(grouped);

            foreach (var track in threadTracks)
            {
                CreateThreadTrackUI(track);
            }

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
        }
        catch (OperationCanceledException)
        {
            // Ignored, superseded by newer load request
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"[TimelineView.LoadSession] {ex}");
            SetEmptyState(true, $"Failed to load timeline: {ex.Message}");
        }
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

        if (empty)
        {
            OverviewXamlPath.Data = null;
            OverviewCompPath.Data = null;
            OverviewOtherPath.Data = null;
            OverviewFrameRequestsPath.Data = null;
            OverviewFrameCommitsPath.Data = null;
            OverviewFrameCommitsDelayedPath.Data = null;
            frameRequestTimes = [];
            frameCommitTimes = [];
            frameCommitDelayedTimes = [];
            frameActivities = [];
            threadTracks.Clear();
            canvasVisualItems.Clear();
            ThreadTracksPanel.Children.Clear();
            FramesCanvas.Children.Clear();
        }
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

        OverviewFrameRequestsPath.Data = CreateMarkerGeometry(frameRequestTimes, totalMinMs, totalMaxMs, width, height);
        OverviewFrameCommitsPath.Data = CreateMarkerGeometry(frameCommitTimes, totalMinMs, totalMaxMs, width, height);
        OverviewFrameCommitsDelayedPath.Data = CreateMarkerGeometry(frameCommitDelayedTimes, totalMinMs, totalMaxMs, width, height);

        double duration = Math.Max(0.001, totalMaxMs - totalMinMs);
        int bins = OverviewBins;
        var xamlCounts = new int[bins];
        var compCounts = new int[bins];
        var otherCounts = new int[bins];

        foreach (var act in allActivities)
        {
            if (act.IsLock) continue;
            int binStart = (int)Math.Clamp((act.StartMs - totalMinMs) / duration * bins, 0, bins - 1);
            int binEnd = (int)Math.Clamp((act.EndMs - totalMinMs) / duration * bins, 0, bins - 1);

            for (int b = binStart; b <= binEnd; b++)
            {
                if (act.Bucket == "Xaml") xamlCounts[b]++;
                else if (act.Bucket == "Composition") compCounts[b]++;
                else if (act.Bucket == "Other") otherCounts[b]++;
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

    private static PathGeometry CreateMarkerGeometry(IReadOnlyList<double> timestamps, double minMs, double maxMs, double width, double height)
    {
        var geometry = new PathGeometry();
        if (timestamps.Count == 0 || width <= 0 || height <= 0) return geometry;

        double duration = Math.Max(0.001, maxMs - minMs);
        var seenPixels = new HashSet<int>();

        foreach (var ms in timestamps)
        {
            if (ms < minMs || ms > maxMs) continue;

            double x = Math.Clamp((ms - minMs) / duration * width, 0, width);
            int px = (int)Math.Round(x);
            if (!seenPixels.Add(px)) continue;

            double lineX = px + 0.5;
            var figure = new PathFigure
            {
                StartPoint = new Point(lineX, 0),
                IsClosed = false
            };
            figure.Segments.Add(new LineSegment { Point = new Point(lineX, height) });
            geometry.Figures.Add(figure);
        }

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
            double newStart = Math.Clamp(overviewDragStartViewStart + deltaMs, totalMinMs, overviewDragStartViewEnd - 0.1);
            viewStartMs = newStart;
            RequestRender();
        }
        else if (overviewDrag == OverviewDragMode.RightHandle)
        {
            double deltaX = pt.Position.X - overviewDragStartX;
            double deltaMs = (deltaX / width) * totalDuration;
            double newEnd = Math.Clamp(overviewDragStartViewEnd + deltaMs, overviewDragStartViewStart + 0.1, totalMaxMs);
            viewEndMs = newEnd;
            RequestRender();
        }
        else if (overviewDrag == OverviewDragMode.WindowBody)
        {
            double deltaX = pt.Position.X - overviewDragStartX;
            double deltaMs = (deltaX / width) * totalDuration;
            double span = Math.Min(totalDuration, overviewDragStartViewEnd - overviewDragStartViewStart);
            double maxStart = Math.Max(totalMinMs, totalMaxMs - span);
            double newStart = Math.Clamp(overviewDragStartViewStart + deltaMs, totalMinMs, maxStart);
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
            RenderFramesTrack();
            foreach (var track in threadTracks)
            {
                if (!track.IsCollapsed)
                {
                    RenderThreadTrack(track);
                }
            }
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

    private static int FindFirstStartIndex(IReadOnlyList<TimelineLockRegion> list, double minStartMs)
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

    private void UpdateToolbarStatus()
    {
        double duration = Math.Max(0, viewEndMs - viewStartMs);
        VisibleRangeText.Text = $"Visible: {viewStartMs:N1} ms – {viewEndMs:N1} ms ({duration:N1} ms)";

        int visibleXaml = 0;
        int visibleComp = 0;
        int visibleOther = 0;
        int visibleFrames = 0;

        foreach (var act in frameActivities)
        {
            if (act.StartMs <= viewEndMs && act.EndMs >= viewStartMs)
            {
                visibleFrames++;
            }
        }

        foreach (var track in threadTracks)
        {
            int trackVisible = 0;
            if (track.Activities.Count > 0 && !(viewStartMs > totalMaxMs || viewEndMs < totalMinMs))
            {
                int startIdx = FindFirstStartIndex(track.Activities, viewStartMs - track.MaxDuration);
                for (int i = startIdx; i < track.Activities.Count; i++)
                {
                    var act = track.Activities[i];
                    if (act.StartMs > viewEndMs) break;
                    if (act.EndMs < viewStartMs) continue;

                    trackVisible++;
                    switch (act.Bucket)
                    {
                        case "Xaml": visibleXaml++; break;
                        case "Composition": visibleComp++; break;
                        case "Idle": break;
                        default: visibleOther++; break;
                    }
                }
            }

            if (track.CountText is not null)
            {
                track.CountText.Text = $"({trackVisible:N0} / {track.Activities.Count:N0})";
            }
        }

        int totalVisible = visibleXaml + visibleComp + visibleOther;
        EventCountsSummary.Text = $"Showing {totalVisible:N0} events (Xaml: {visibleXaml:N0}, Comp: {visibleComp:N0}, Other: {visibleOther:N0})";
        FramesCountText.Text = $"({visibleFrames:N0} / {frameActivities.Count:N0})";
    }

    private void RenderFramesTrack()
    {
        FramesCanvas.Children.Clear();
        if (!canvasVisualItems.TryGetValue(FramesCanvas, out var visualItems))
        {
            visualItems = new List<VisualHitItem>();
            canvasVisualItems[FramesCanvas] = visualItems;
        }
        visualItems.Clear();

        var width = FramesCanvas.ActualWidth;
        var height = FramesCanvas.ActualHeight > 0 ? FramesCanvas.ActualHeight : 34;
        if (width <= 0 || frameActivities.Count == 0) return;

        double duration = Math.Max(0.001, viewEndMs - viewStartMs);
        double pxPerMs = width / duration;

        int startIdx = FindFirstStartIndex(frameActivities, viewStartMs - 100);
        if (startIdx >= frameActivities.Count) return;

        var requestGeom = new GeometryGroup();
        var commitGeom = new GeometryGroup();
        var commitDelayedGeom = new GeometryGroup();

        var seenRequestX = new HashSet<int>();
        var seenCommitX = new HashSet<int>();
        var seenCommitDelayedX = new HashSet<int>();

        for (int i = startIdx; i < frameActivities.Count; i++)
        {
            var act = frameActivities[i];
            if (act.StartMs > viewEndMs) break;
            if (act.EndMs < viewStartMs) continue;

            double ms = act.IsFrameCommit && act.IsDuration ? act.EndMs : act.StartMs;
            double x = (ms - viewStartMs) * pxPerMs;
            if (double.IsNaN(x) || double.IsInfinity(x)) continue;
            if (x < -5 || x > width + 5) continue;

            int px = (int)Math.Round(x);

            if (act.IsFrameRequest)
            {
                if (seenRequestX.Add(px))
                {
                    requestGeom.Children.Add(new RectangleGeometry
                    {
                        Rect = new Rect(x - 0.75, 0, 1.5, height)
                    });
                }

                visualItems.Add(new VisualHitItem
                {
                    Lane = 0,
                    X1 = x - 0.75,
                    X2 = x + 0.75,
                    Y = 0,
                    StartMs = act.StartMs,
                    EndMs = act.EndMs,
                    ClusterCount = 1,
                    Dominant = act,
                    IsDuration = act.IsDuration
                });
            }
            else if (act.IsFrameCommit)
            {
                if (act.IsDelayedFrameCommit)
                {
                    if (seenCommitDelayedX.Add(px))
                    {
                        commitDelayedGeom.Children.Add(new RectangleGeometry
                        {
                            Rect = new Rect(x - 1, 0, 2, height)
                        });
                    }
                }
                else
                {
                    if (seenCommitX.Add(px))
                    {
                        commitGeom.Children.Add(new RectangleGeometry
                        {
                            Rect = new Rect(x - 1, 0, 2, height)
                        });
                    }
                }

                visualItems.Add(new VisualHitItem
                {
                    Lane = 0,
                    X1 = x - 1,
                    X2 = x + 1,
                    Y = 0,
                    StartMs = act.StartMs,
                    EndMs = act.EndMs,
                    ClusterCount = 1,
                    Dominant = act,
                    IsDuration = act.IsDuration
                });
            }
        }

        if (requestGeom.Children.Count > 0)
        {
            var requestPath = new ShapePath
            {
                Data = requestGeom,
                Fill = FrameRequestBrush,
                Opacity = 0.5,
                IsHitTestVisible = false
            };
            FramesCanvas.Children.Add(requestPath);
        }

        if (commitGeom.Children.Count > 0)
        {
            var commitPath = new ShapePath
            {
                Data = commitGeom,
                Fill = FrameCommitBrush,
                IsHitTestVisible = false
            };
            FramesCanvas.Children.Add(commitPath);
        }

        if (commitDelayedGeom.Children.Count > 0)
        {
            var commitDelayedPath = new ShapePath
            {
                Data = commitDelayedGeom,
                Fill = FrameCommitDelayedBrush,
                IsHitTestVisible = false
            };
            FramesCanvas.Children.Add(commitDelayedPath);
        }

        if (selectedActivity is not null && (selectedActivity.IsFrameRequest || selectedActivity.IsFrameCommit))
        {
            var selItem = visualItems.FirstOrDefault(v => ReferenceEquals(v.Dominant, selectedActivity)
                || (selectedActivity.EndMs >= v.StartMs && selectedActivity.StartMs <= v.EndMs));
            if (selItem is not null)
            {
                double markerW = selItem.X2 - selItem.X1;
                var highlight = new Border
                {
                    Width = markerW,
                    Height = height,
                    Background = AttentionBrush,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(highlight, selItem.X1);
                Canvas.SetTop(highlight, 0);
                FramesCanvas.Children.Add(highlight);
            }
        }
    }

    private void CreateThreadTrackUI(ThreadTrackInfo track)
    {
        var container = new Grid();
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid
        {
            Margin = new Thickness(4, 4, 4, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var chevron = new FontIcon
        {
            Glyph = "\uE70D",
            FontSize = 10,
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = TextSecBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(chevron, 0);

        var roleBadge = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = track.Role switch
            {
                "Main" => XamlBrush,
                "Render" => CompBrush,
                _ => OtherBrush
            }
        };
        var roleText = new TextBlock
        {
            Text = track.Role switch
            {
                "Main" => "UI Thread",
                "Render" => "Compositor Scheduler",
                _ => "Worker"
            },
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        };
        roleBadge.Child = roleText;
        Grid.SetColumn(roleBadge, 1);

        string displayName = (track.Name == "Render Thread" || track.Role == "Render") ? "Compositor Scheduler" : track.Name;
        var titleText = new TextBlock
        {
            Text = $"{displayName} (TID {track.ThreadId})",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 2);

        var countText = new TextBlock
        {
            FontSize = 11,
            Foreground = TextSecBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(countText, 3);

        header.Children.Add(chevron);
        header.Children.Add(roleBadge);
        header.Children.Add(titleText);
        header.Children.Add(countText);

        double neededHeight = (track.MaxDepth + 1) * (LaneHeight + LaneSpacing) + TrackTopPadding + 8;
        var border = new Border
        {
            Height = Math.Max(MinTrackHeight, neededHeight),
            Background = FramesTrackBorder.Background,
            BorderBrush = FramesTrackBorder.BorderBrush,
            BorderThickness = FramesTrackBorder.BorderThickness,
            CornerRadius = FramesTrackBorder.CornerRadius
        };
        Grid.SetRow(border, 1);

        var canvas = new Canvas
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };
        canvas.PointerWheelChanged += Tracks_PointerWheelChanged;
        canvas.SizeChanged += TrackCanvas_SizeChanged;
        canvas.PointerPressed += Track_PointerPressed;
        canvas.PointerMoved += Track_PointerMoved;
        canvas.PointerReleased += Track_PointerReleased;
        canvas.PointerCaptureLost += Track_PointerCaptureLost;
        canvas.PointerExited += Track_PointerExited;

        border.Child = canvas;
        container.Children.Add(header);
        container.Children.Add(border);

        track.Container = container;
        track.Border = border;
        track.Canvas = canvas;
        track.ChevronIcon = chevron;
        track.CountText = countText;

        canvasVisualItems[canvas] = new List<VisualHitItem>();

        header.PointerPressed += (s, e) =>
        {
            track.IsCollapsed = !track.IsCollapsed;
            chevron.Glyph = track.IsCollapsed ? "\uE76C" : "\uE70D";
            border.Visibility = track.IsCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (!track.IsCollapsed)
            {
                RenderThreadTrack(track);
            }
            e.Handled = true;
        };

        ThreadTracksPanel.Children.Add(container);
    }

    private void RenderThreadTrack(ThreadTrackInfo track)
    {
        var canvas = track.Canvas;
        canvas.Children.Clear();
        if (!canvasVisualItems.TryGetValue(canvas, out var visualItems))
        {
            visualItems = new List<VisualHitItem>();
            canvasVisualItems[canvas] = visualItems;
        }
        visualItems.Clear();

        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        var activities = track.Activities;
        if (width <= 0 || height <= 0 || activities.Count == 0) return;

        double duration = Math.Max(0.001, viewEndMs - viewStartMs);
        double pxPerMs = width / duration;

        int startIdx = FindFirstStartIndex(activities, viewStartMs - track.MaxDuration);
        if (startIdx >= activities.Count) return;

        var laneDurationMap = new Dictionary<(int depth, string bucket), List<TimelineActivity>>();
        var laneInstantMap = new Dictionary<(int depth, string bucket), List<TimelineActivity>>();

        for (int i = startIdx; i < activities.Count; i++)
        {
            var act = activities[i];
            if (act.StartMs > viewEndMs) break;
            if (act.EndMs < viewStartMs) continue;

            var key = (act.Depth, act.Bucket);
            if (act.IsDuration)
            {
                if (!laneDurationMap.TryGetValue(key, out var dList))
                {
                    dList = new List<TimelineActivity>();
                    laneDurationMap[key] = dList;
                }
                dList.Add(act);
            }
            else
            {
                if (!laneInstantMap.TryGetValue(key, out var iList))
                {
                    iList = new List<TimelineActivity>();
                    laneInstantMap[key] = iList;
                }
                iList.Add(act);
            }
        }

        if (laneDurationMap.Count == 0 && laneInstantMap.Count == 0 && (!showLockRegions || track.LockRegions.Count == 0)) return;

        // 1. Shaded background lock regions (underneath canvas behind all foreground events)
        if (showLockRegions && track.LockRegions.Count > 0)
        {
            var lockRegionGeom = new GeometryGroup();
            int lockStartIdx = FindFirstStartIndex(track.LockRegions, viewStartMs - 1000);

            for (int li = lockStartIdx; li < track.LockRegions.Count; li++)
            {
                var lockReg = track.LockRegions[li];
                if (lockReg.StartMs > viewEndMs) break;
                if (lockReg.EndMs < viewStartMs) continue;

                double rawX1 = (lockReg.StartMs - viewStartMs) * pxPerMs;
                double rawX2 = (lockReg.EndMs - viewStartMs) * pxPerMs;
                if (rawX2 < -5 || rawX1 > width + 5) continue;

                double x1 = Math.Clamp(rawX1, 0, width);
                double x2 = Math.Clamp(rawX2, 0, width);
                double w = Math.Max(2.0, x2 - x1);

                lockRegionGeom.Children.Add(new RectangleGeometry
                {
                    Rect = new Rect(x1, 0, w, height)
                });

                visualItems.Add(new VisualHitItem
                {
                    Lane = -1,
                    X1 = x1,
                    X2 = x2,
                    Y = 0,
                    StartMs = lockReg.StartMs,
                    EndMs = lockReg.EndMs,
                    ClusterCount = 1,
                    LockRegion = lockReg,
                    IsDuration = true
                });
            }

            AddBatchShapePath(canvas, lockRegionGeom, LockRegionFill, null);
        }

        var xamlDurationGeom = new GeometryGroup();
        var xamlClusterGeom = new GeometryGroup();
        var xamlTicksGeom = new GeometryGroup();
        var compDurationGeom = new GeometryGroup();
        var compClusterGeom = new GeometryGroup();
        var compTicksGeom = new GeometryGroup();
        var otherDurationGeom = new GeometryGroup();
        var otherClusterGeom = new GeometryGroup();
        var otherTicksGeom = new GeometryGroup();
        var idleDurationGeom = new GeometryGroup();
        var idleClusterGeom = new GeometryGroup();
        var idleHatchGeom = new GeometryGroup();
        var lockSolidGeom = new GeometryGroup();
        var lockDashedGeom = new GeometryGroup();

        int textCount = 0;
        const int maxTextElements = 75;
        var pendingLabels = new List<(TextBlock Label, double Left, double Top)>();

        foreach (var ((depth, bucket), laneActs) in laneDurationMap)
        {
            double y = depth * (LaneHeight + LaneSpacing) + TrackTopPadding;
            var targetDurationGeom = bucket switch
            {
                "Xaml" => xamlDurationGeom,
                "Composition" => compDurationGeom,
                "Idle" => idleDurationGeom,
                _ => otherDurationGeom
            };
            var targetClusterGeom = bucket switch
            {
                "Xaml" => xamlClusterGeom,
                "Composition" => compClusterGeom,
                "Idle" => idleClusterGeom,
                _ => otherClusterGeom
            };
            var strokeBrush = bucket switch
            {
                "Xaml" => XamlBrush,
                "Composition" => CompBrush,
                "Idle" => IdleWaitBrush,
                _ => OtherBrush
            };

            var blocks = new List<VisualHitItem>();

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

                blocks.Add(new VisualHitItem
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

                curClusterX1 = double.NaN;
                clusterCount = 0;
                dominant = null;
                maxDur = 0;
            }

            foreach (var act in laneActs)
            {
                double rawX1 = (act.StartMs - viewStartMs) * pxPerMs;
                double rawX2 = (act.EndMs - viewStartMs) * pxPerMs;
                if (double.IsNaN(rawX1) || double.IsInfinity(rawX1) || double.IsNaN(rawX2) || double.IsInfinity(rawX2)) continue;

                double naturalW = rawX2 - rawX1;
                bool isLarge = naturalW >= 3.0;

                double x1 = Math.Clamp(rawX1, -20, width + 20);
                double x2 = Math.Clamp(rawX2, -20, width + 20);
                double w = Math.Max(2.0, x2 - x1);
                x2 = x1 + w;

                // Add visual hit item for this SPECIFIC activity
                visualItems.Add(new VisualHitItem
                {
                    Lane = depth,
                    X1 = x1,
                    X2 = x2,
                    Y = y,
                    StartMs = act.StartMs,
                    EndMs = act.EndMs,
                    ClusterCount = 1,
                    Dominant = act,
                    IsDuration = true
                });

                if (isLarge)
                {
                    FlushCluster();

                    blocks.Add(new VisualHitItem
                    {
                        Lane = depth,
                        X1 = x1,
                        X2 = x2,
                        Y = y,
                        StartMs = act.StartMs,
                        EndMs = act.EndMs,
                        ClusterCount = 1,
                        Dominant = act,
                        IsDuration = true
                    });
                }
                else
                {
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
                    else if (x1 <= curClusterX2 + 1.5)
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
            }
            FlushCluster();

            // Render blocks with a 1px background divider between adjacent blocks
            for (int bIdx = 0; bIdx < blocks.Count; bIdx++)
            {
                var block = blocks[bIdx];

                double bX1 = block.X1;
                double bX2 = block.X2;
                bool hasAdjacentNext = (bIdx + 1 < blocks.Count) && (blocks[bIdx + 1].X1 <= bX2 + 1.5);

                double renderW;
                if (hasAdjacentNext)
                {
                    double targetX2 = Math.Min(bX2, blocks[bIdx + 1].X1 - 1.0);
                    renderW = Math.Max(2.0, targetX2 - bX1);
                }
                else
                {
                    renderW = Math.Max(2.0, bX2 - bX1);
                }

                bool isCluster = block.ClusterCount > 1;

                if (isCluster)
                {
                    targetClusterGeom.Children.Add(new RectangleGeometry
                    {
                        Rect = new Rect(bX1, y, renderW, LaneHeight)
                    });
                }
                else
                {
                    targetDurationGeom.Children.Add(new RectangleGeometry
                    {
                        Rect = new Rect(bX1, y, renderW, LaneHeight)
                    });

                    if (bucket == "Idle")
                    {
                        double h = LaneHeight;
                        double step = 8.0;
                        double startP = bX1 - h;
                        double endP = bX1 + renderW;

                        for (double p = startP; p <= endP; p += step)
                        {
                            double xStart = p;
                            double yStart = y;
                            double xEnd = p + h;
                            double yEnd = y + h;

                            if (xStart < bX1)
                            {
                                yStart = y + (bX1 - xStart);
                                xStart = bX1;
                            }

                            if (xEnd > bX1 + renderW)
                            {
                                yEnd = y + h - (xEnd - (bX1 + renderW));
                                xEnd = bX1 + renderW;
                            }

                            if (xEnd > xStart && yEnd > yStart)
                            {
                                idleHatchGeom.Children.Add(new LineGeometry
                                {
                                    StartPoint = new Point(xStart, yStart),
                                    EndPoint = new Point(xEnd, yEnd)
                                });
                            }
                        }
                    }

                    if (renderW >= 36 && textCount < maxTextElements && bX2 > 0 && bX1 < width)
                    {
                        double labelX = Math.Max(bX1 + 3, 2);
                        double visibleRight = Math.Min(bX1 + renderW - 3, width - 2);
                        double labelMaxWidth = Math.Max(0, visibleRight - labelX);

                        if (labelMaxWidth >= 20 && block.Dominant is not null)
                        {
                            string labelText = $"{block.Dominant.Name} ({block.Dominant.DurationMs:N1} ms)";

                            var label = new TextBlock
                            {
                                Text = labelText,
                                FontSize = 10,
                                Foreground = strokeBrush,
                                TextTrimming = TextTrimming.CharacterEllipsis,
                                IsHitTestVisible = false,
                                MaxWidth = labelMaxWidth
                            };
                            pendingLabels.Add((label, labelX, y + 1));
                            textCount++;
                        }
                    }
                }
            }
        }

        foreach (var ((depth, bucket), laneActs) in laneInstantMap)
        {
            double y = depth * (LaneHeight + LaneSpacing) + TrackTopPadding;
            var targetTicksGeom = bucket switch
            {
                "Xaml" => xamlTicksGeom,
                "Composition" => compTicksGeom,
                _ => otherTicksGeom
            };

            double lastTickX = double.NegativeInfinity;

            foreach (var act in laneActs)
            {
                double x = (act.StartMs - viewStartMs) * pxPerMs;
                if (double.IsNaN(x) || double.IsInfinity(x)) continue;
                if (x < -5 || x > width + 5) continue;

                // Always add every instant event so each one can be individually hit-tested
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

                if (x - lastTickX < 1.5) continue;
                lastTickX = x;

                targetTicksGeom.Children.Add(new RectangleGeometry
                {
                    Rect = new Rect(x - 1, y, 2, LaneHeight)
                });
            }
        }

        AddBatchShapePath(canvas, xamlDurationGeom, XamlFill, XamlBrush);
        AddBatchShapePath(canvas, xamlClusterGeom, XamlBrush, XamlBrush);
        AddBatchShapePath(canvas, xamlTicksGeom, XamlBrush, XamlBrush);
        AddBatchShapePath(canvas, compDurationGeom, CompFill, CompBrush);
        AddBatchShapePath(canvas, compClusterGeom, CompBrush, CompBrush);
        AddBatchShapePath(canvas, compTicksGeom, CompBrush, CompBrush);
        AddBatchShapePath(canvas, otherDurationGeom, OtherFill, OtherBrush);
        AddBatchShapePath(canvas, otherClusterGeom, OtherBrush, OtherBrush);
        AddBatchShapePath(canvas, otherTicksGeom, OtherBrush, OtherBrush);
        AddBatchShapePath(canvas, idleDurationGeom, IdleWaitFill, IdleWaitBrush);
        AddBatchShapePath(canvas, idleClusterGeom, IdleWaitBrush, IdleWaitBrush);
        AddBatchShapePath(canvas, idleHatchGeom, null, IdleWaitBrush);

        foreach (var (lbl, left, top) in pendingLabels)
        {
            Canvas.SetLeft(lbl, left);
            Canvas.SetTop(lbl, top);
            Canvas.SetZIndex(lbl, 10);
            canvas.Children.Add(lbl);
        }

        if (selectedLockRegion is not null && selectedLockRegion.ThreadId == track.ThreadId)
        {
            double rawX1 = (selectedLockRegion.StartMs - viewStartMs) * pxPerMs;
            double rawX2 = (selectedLockRegion.EndMs - viewStartMs) * pxPerMs;
            if (rawX2 >= 0 && rawX1 <= width)
            {
                double x1 = Math.Clamp(rawX1, -5, width + 5);
                double x2 = Math.Clamp(rawX2, -5, width + 5);
                double w = Math.Max(2.0, x2 - x1);
                var highlight = new Border
                {
                    Width = Math.Clamp(w + 2, 2, width + 100),
                    Height = height,
                    BorderBrush = AttentionBrush,
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(highlight, x1 - 1);
                Canvas.SetTop(highlight, 0);
                canvas.Children.Add(highlight);
            }
        }
        else if (selectedActivity is not null && selectedActivity.ThreadId == track.ThreadId)
        {
            var selItem = visualItems.FirstOrDefault(v => ReferenceEquals(v.Dominant, selectedActivity))
                ?? visualItems.FirstOrDefault(v => selectedActivity.EndMs >= v.StartMs && selectedActivity.StartMs <= v.EndMs && selectedActivity.Depth == v.Lane);

            if (selItem is not null)
            {
                var highlight = new Border
                {
                    Width = Math.Clamp(selItem.X2 - selItem.X1 + 2, 2, width + 100),
                    Height = LaneHeight + 2,
                    BorderBrush = AttentionBrush,
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(highlight, selItem.X1 - 1);
                Canvas.SetTop(highlight, selItem.Y - 1);
                canvas.Children.Add(highlight);
            }
        }
    }

    private static void AddBatchShapePath(Canvas canvas, GeometryGroup geometryGroup, Brush? fill, Brush? stroke, DoubleCollection? strokeDashArray = null, double strokeThickness = 1.0)
    {
        if (geometryGroup.Children.Count == 0) return;
        var path = new ShapePath
        {
            Data = geometryGroup,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            StrokeDashArray = strokeDashArray,
            IsHitTestVisible = false
        };
        canvas.Children.Add(path);
    }

    #endregion

    #region Mouse & Keyboard Navigation

    private void Tracks_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (isPanning || overviewDrag != OverviewDragMode.None)
        {
            e.Handled = true;
            return;
        }

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

            double totalSpan = Math.Max(0.001, totalMaxMs - totalMinMs);
            double duration = Math.Min(totalSpan, panStartViewEnd - panStartViewStart);
            double deltaMs = -(deltaX / width) * duration;

            double maxStart = Math.Max(totalMinMs, totalMaxMs - duration);
            double newStart = Math.Clamp(panStartViewStart + deltaMs, totalMinMs, maxStart);

            viewStartMs = newStart;
            viewEndMs = newStart + duration;
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
        if (!canvasVisualItems.TryGetValue(canvas, out var items) || items.Count == 0) return null;

        double mouseX = clickPos.X;
        double mouseY = clickPos.Y;

        if (ReferenceEquals(canvas, FramesCanvas))
        {
            if (mouseY < 0 || mouseY > canvas.ActualHeight) return null;

            VisualHitItem? bestItem = null;
            double bestDist = double.MaxValue;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (mouseX >= item.X1 - 4 && mouseX <= item.X2 + 4)
                {
                    double midX = (item.X1 + item.X2) / 2.0;
                    double dist = Math.Abs(mouseX - midX);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestItem = item;
                    }
                }
            }
            return bestItem;
        }

        // Thread Track Canvas
        double width = canvas.ActualWidth;
        if (width <= 0) return null;

        double duration = Math.Max(0.001, viewEndMs - viewStartMs);
        double mouseMs = viewStartMs + (mouseX / width) * duration;

        int hoveredDepth = (int)((mouseY - TrackTopPadding) / (LaneHeight + LaneSpacing));
        VisualHitItem? bestExact = null;
        VisualHitItem? bestNearby = null;
        double bestDistPx = double.MaxValue;
        double bestTimeDist = double.MaxValue;

        if (hoveredDepth >= 0)
        {
            double laneTop = hoveredDepth * (LaneHeight + LaneSpacing) + TrackTopPadding;
            if (mouseY >= laneTop && mouseY <= laneTop + LaneHeight)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item.Lane != hoveredDepth) continue;

                    // Direct timestamp hit for duration events
                    if (item.IsDuration && mouseMs >= item.StartMs && mouseMs <= item.EndMs)
                    {
                        double span = item.EndMs - item.StartMs;
                        if (span < bestTimeDist)
                        {
                            bestTimeDist = span;
                            bestExact = item;
                        }
                        continue;
                    }

                    if (bestExact is not null) continue;

                    double left = item.IsDuration ? item.X1 : item.X1 - 2;
                    double right = item.IsDuration ? item.X2 : item.X2 + 2;

                    if (mouseX >= left && mouseX <= right)
                    {
                        double midX = (item.X1 + item.X2) / 2.0;
                        double dist = Math.Abs(mouseX - midX);
                        if (dist < bestDistPx)
                        {
                            bestDistPx = dist;
                            bestNearby = item;
                        }
                    }
                    else if (bestNearby is null && mouseX >= left - 2 && mouseX <= right + 2)
                    {
                        double midX = (item.X1 + item.X2) / 2.0;
                        double dist = Math.Abs(mouseX - midX);
                        if (dist < bestDistPx)
                        {
                            bestDistPx = dist;
                            bestNearby = item;
                        }
                    }
                }
            }
        }

        var result = bestExact ?? bestNearby;
        if (result is not null) return result;

        if (showLockRegions)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.LockRegion is not null && mouseMs >= it.StartMs && mouseMs <= it.EndMs)
                {
                    return it;
                }
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

            if (item.LockRegion is not null)
            {
                var overlayTop = transform.TransformPoint(new Point(item.X1, 0));
                double w = Math.Max(2.0, item.X2 - item.X1);
                double trackH = canvas.ActualHeight > 0 ? canvas.ActualHeight : 44;

                Canvas.SetLeft(HoverHighlight, overlayTop.X - 1);
                Canvas.SetTop(HoverHighlight, overlayTop.Y);
                HoverHighlight.Width = w + 2;
                HoverHighlight.Height = trackH;
                HoverHighlight.Background = null;
                HoverHighlight.BorderThickness = new Thickness(1);
            }
            else if (ReferenceEquals(canvas, FramesCanvas))
            {
                var overlayTop = transform.TransformPoint(new Point(item.X1, 0));
                double markerW = item.X2 - item.X1;
                double trackH = canvas.ActualHeight > 0 ? canvas.ActualHeight : 34;

                Canvas.SetLeft(HoverHighlight, overlayTop.X);
                Canvas.SetTop(HoverHighlight, overlayTop.Y);
                HoverHighlight.Width = markerW;
                HoverHighlight.Height = trackH;
                HoverHighlight.Background = HoverHighlight.BorderBrush;
                HoverHighlight.BorderThickness = new Thickness(0);
            }
            else
            {
                var overlayTopLeft = transform.TransformPoint(new Point(item.X1, item.Y));
                double w = Math.Max(2.0, item.X2 - item.X1);
                Canvas.SetLeft(HoverHighlight, overlayTopLeft.X - 1);
                Canvas.SetTop(HoverHighlight, overlayTopLeft.Y - 1);
                HoverHighlight.Width = w + 2;
                HoverHighlight.Height = LaneHeight + 2;
                HoverHighlight.Background = null;
                HoverHighlight.BorderThickness = new Thickness(1);
            }
            HoverHighlight.Visibility = Visibility.Visible;

            if (item.LockRegion is not null)
            {
                var lockReg = item.LockRegion;
                HoverTitle.Text = lockReg.Name;
                HoverBucketText.Text = "Lock Region";
                HoverBucketBadge.Background = LockSpanBrush;
                HoverDuration.Text = $"Lock Held: {lockReg.DurationMs:N3} ms";
                HoverTimeSpan.Text = $"{lockReg.StartMs:N2} ms → {lockReg.EndMs:N2} ms";
                HoverMetaThread.Text = $"Thread: {lockReg.ThreadId}";
                HoverMetaProvider.Text = lockReg.Provider.ToString();
            }
            else if (item.Dominant is not null)
            {
                var act = item.Dominant;
                HoverTitle.Text = act.IsDuration ? CleanSpanEventName(act.Name) : act.Name;
                if (act.IsFrameRequest)
                {
                    HoverBucketText.Text = "Frame Request";
                    HoverBucketBadge.Background = FrameRequestBrush;
                }
                else if (act.IsFrameCommit)
                {
                    if (act.IsDelayedFrameCommit)
                    {
                        HoverBucketText.Text = "Frame Commit (Delayed)";
                        HoverBucketBadge.Background = FrameCommitDelayedBrush;
                    }
                    else
                    {
                        HoverBucketText.Text = "Frame Commit";
                        HoverBucketBadge.Background = FrameCommitBrush;
                    }
                }
                else if (act.IsSchedulerWait || act.Bucket == "Idle")
                {
                    HoverBucketText.Text = "Idle / Wait";
                    HoverBucketBadge.Background = IdleWaitBrush;
                }
                else
                {
                    HoverBucketText.Text = act.Bucket;
                    HoverBucketBadge.Background = act.Bucket switch
                    {
                        "Xaml" => XamlBrush,
                        "Composition" => CompBrush,
                        _ => OtherBrush
                    };
                }

                if (act.IsFrameCommit)
                {
                    double commitTime = act.IsDuration ? act.EndMs : act.StartMs;
                    if (act.FrameLatencyMs.HasValue)
                    {
                        string delayedStatus = act.IsDelayedFrameCommit ? "Delayed (> 16.66 ms)" : "On Time";
                        HoverDuration.Text = $"Latency: {act.FrameLatencyMs.Value:N2} ms ({delayedStatus})" + (act.IsDuration ? $" (Commit: {act.DurationMs:N2} ms)" : "");
                        HoverTimeSpan.Text = $"Req: {act.FrameRequestMs!.Value:N2} ms → Commit: {commitTime:N2} ms";
                    }
                    else
                    {
                        HoverDuration.Text = act.IsDuration ? $"Commit: {act.DurationMs:N2} ms" : "Frame Commit";
                        HoverTimeSpan.Text = $"@ {commitTime:N2} ms";
                    }
                }
                else if (act.IsFrameRequest)
                {
                    if (act.FrameCommitMs.HasValue && act.FrameLatencyMs.HasValue)
                    {
                        HoverDuration.Text = $"Latency to Commit: {act.FrameLatencyMs.Value:N2} ms";
                        HoverTimeSpan.Text = $"Req: {act.StartMs:N2} ms → Commit: {act.FrameCommitMs.Value:N2} ms";
                    }
                    else
                    {
                        HoverDuration.Text = "Queued Frame Request";
                        HoverTimeSpan.Text = $"@ {act.StartMs:N2} ms";
                    }
                }
                else if (item.IsDuration)
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
            }

            var pointerInOverlay = e.GetCurrentPoint(HoverOverlayCanvas).Position;
            double overlayW = HoverOverlayCanvas.ActualWidth;
            double overlayH = HoverOverlayCanvas.ActualHeight;

            HoverCard.Measure(new Windows.Foundation.Size(360, double.PositiveInfinity));
            double cardW = Math.Max(HoverCard.DesiredSize.Width, HoverCard.ActualWidth);
            if (cardW <= 0) cardW = 280;
            double cardH = Math.Max(HoverCard.DesiredSize.Height, HoverCard.ActualHeight);
            if (cardH <= 0) cardH = 80;

            double cardX = pointerInOverlay.X + 16;
            double cardY = pointerInOverlay.Y + 16;

            double rightLimit = overlayW - 8;
            if (InspectorBorder is not null && InspectorBorder.Visibility == Visibility.Visible && InspectorBorder.ActualWidth > 0)
            {
                rightLimit = Math.Max(8, overlayW - InspectorBorder.ActualWidth - 12);
            }

            if (overlayW > 0 && cardX + cardW > rightLimit)
            {
                cardX = pointerInOverlay.X - cardW - 16;
            }
            if (cardX < 8)
            {
                cardX = 8;
            }

            if (overlayH > 0 && cardY + cardH > overlayH - 8)
            {
                cardY = pointerInOverlay.Y - cardH - 16;
            }
            if (cardY < 8)
            {
                cardY = 8;
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
            HoverHighlight.Background = null;
            HoverHighlight.BorderThickness = new Thickness(1);
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
            if (item.LockRegion is not null)
            {
                selectedActivity = null;
                selectedLockRegion = item.LockRegion;
                ShowLockRegionDetails(item.LockRegion);
                RenderAll();
                return;
            }

            selectedLockRegion = null;
            selectedActivity = item.Dominant;
            if (item.Dominant is not null)
            {
                ShowActivityDetails(item.Dominant);
            }
            RenderAll();
        }
    }

    private void ShowLockRegionDetails(TimelineLockRegion lockReg)
    {
        HideHover();
        InspectorBorder.Visibility = Visibility.Visible;
        DetailBucketText.Text = "Lock Region";
        DetailBucketBadge.Background = LockSpanBrush;
        DetailTitleText.Text = lockReg.Name;
        DetailDurationText.Text = $"Lock Held: {lockReg.DurationMs:N3} ms";
        DetailMetaText.Text = $"Start: {lockReg.StartMs:N3} ms | End: {lockReg.EndMs:N3} ms | Thread: {lockReg.ThreadId} | Provider: {lockReg.Provider}";
        DetailFieldsText.Text = $"Compositor Lock held for {lockReg.DurationMs:N3} ms on thread {lockReg.ThreadId}.\nStart Seq: {lockReg.StartSeq}\nEnd Seq: {lockReg.EndSeq}";
        DetailObjectsList.ItemsSource = null;
    }

    private void ShowLockRegionsToggle_Click(object sender, RoutedEventArgs e)
    {
        showLockRegions = ShowLockRegionsToggle.IsChecked;
        if (LockRegionLegendItem is not null)
        {
            LockRegionLegendItem.Opacity = showLockRegions ? 1.0 : 0.4;
        }
        RenderAll();
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
        var width = FramesCanvas.ActualWidth;
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
        double totalSpan = Math.Max(0.001, totalMaxMs - totalMinMs);
        double duration = Math.Min(totalSpan, viewEndMs - viewStartMs);
        double maxStart = Math.Max(totalMinMs, totalMaxMs - duration);

        double newStart = Math.Clamp(viewStartMs + delta, totalMinMs, maxStart);

        viewStartMs = newStart;
        viewEndMs = newStart + duration;
        RequestRender();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomByFactor(0.75);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomByFactor(1.333);

    private void FitAll_Click(object sender, RoutedEventArgs e) => ResetToFullExtent();

    #endregion

    #region Track Heights (Full Tree Depth)

    private void UpdateTrackHeights()
    {
        foreach (var track in threadTracks)
        {
            double neededHeight = (track.MaxDepth + 1) * (LaneHeight + LaneSpacing) + TrackTopPadding + 8;
            track.Border.Height = Math.Max(MinTrackHeight, neededHeight);
        }
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

    private async void ShowActivityDetails(TimelineActivity activity)
    {
        HideHover();
        InspectorBorder.Visibility = Visibility.Visible;

        if (activity.IsFrameRequest)
        {
            DetailBucketText.Text = "Frame Request";
            DetailBucketBadge.Background = FrameRequestBrush;
        }
        else if (activity.IsFrameCommit)
        {
            if (activity.IsDelayedFrameCommit)
            {
                DetailBucketText.Text = "Frame Commit (Delayed)";
                DetailBucketBadge.Background = FrameCommitDelayedBrush;
            }
            else
            {
                DetailBucketText.Text = "Frame Commit";
                DetailBucketBadge.Background = FrameCommitBrush;
            }
        }
        else if (activity.IsLock)
        {
            DetailBucketText.Text = "Lock";
            DetailBucketBadge.Background = LockSpanBrush;
        }
        else if (activity.IsSchedulerWait || activity.Bucket == "Idle")
        {
            DetailBucketText.Text = "Idle / Wait";
            DetailBucketBadge.Background = IdleWaitBrush;
        }
        else
        {
            DetailBucketText.Text = activity.Bucket;
            DetailBucketBadge.Background = activity.Bucket switch
            {
                "Xaml" => XamlBrush,
                "Composition" => CompBrush,
                _ => OtherBrush
            };
        }

        var displayName = activity.IsDuration ? CleanSpanEventName(activity.Name) : activity.Name;
        DetailTitleText.Text = displayName;

        if (activity.IsFrameCommit)
        {
            double commitTime = activity.IsDuration ? activity.EndMs : activity.StartMs;
            if (activity.FrameLatencyMs.HasValue)
            {
                string status = activity.IsDelayedFrameCommit ? "Delayed (> 16.66 ms)" : "On Time (<= 16.66 ms)";
                DetailDurationText.Text = $"Frame Latency: {activity.FrameLatencyMs.Value:N3} ms ({status})" + (activity.IsDuration ? $" (Commit Duration: {activity.DurationMs:N3} ms)" : "");
                DetailMetaText.Text = $"Request: {activity.FrameRequestMs!.Value:N3} ms | Commit: {commitTime:N3} ms | Thread: {activity.ThreadId} | Provider: {activity.Provider}";
            }
            else
            {
                DetailDurationText.Text = activity.IsDuration ? $"Commit Duration: {activity.DurationMs:N3} ms" : "Frame Commit (Instant)";
                DetailMetaText.Text = $"Commit: {commitTime:N3} ms | Thread: {activity.ThreadId} | Provider: {activity.Provider}";
            }
        }
        else if (activity.IsFrameRequest)
        {
            if (activity.FrameCommitMs.HasValue && activity.FrameLatencyMs.HasValue)
            {
                DetailDurationText.Text = $"Latency to Commit: {activity.FrameLatencyMs.Value:N3} ms";
                DetailMetaText.Text = $"Request: {activity.StartMs:N3} ms | Commit: {activity.FrameCommitMs.Value:N3} ms | Thread: {activity.ThreadId} | Provider: {activity.Provider}";
            }
            else
            {
                DetailDurationText.Text = "Queued Frame Request";
                DetailMetaText.Text = $"Request: {activity.StartMs:N3} ms | Thread: {activity.ThreadId} | Provider: {activity.Provider}";
            }
        }
        else if (activity.IsLock)
        {
            DetailDurationText.Text = activity.IsDuration ? $"Lock Held: {activity.DurationMs:N3} ms" : "Lock Event (Instant)";
            DetailMetaText.Text = $"Timestamp: {activity.StartMs:N3} ms | Thread: {activity.ThreadId} | Provider: {activity.Provider}";
        }
        else
        {
            DetailDurationText.Text = activity.IsDuration ? $"Duration: {activity.DurationMs:N3} ms" : "Instant Event";
            DetailMetaText.Text = $"Start: {activity.StartMs:N3} ms | Thread: {activity.ThreadId} | Provider: {activity.Provider}";
        }

        DetailFieldsText.Text = "Loading details...";
        DetailObjectsList.ItemsSource = null;

        var path = loadedPath;
        if (path is null) return;

        try
        {
            var result = await Task.Run(() => SessionFile.ReadSingleEvent(path, activity.StartSeq));
            if (result is null)
            {
                DetailFieldsText.Text = $"{displayName}\nNo additional field details found.";
                return;
            }

            var (profilerEvent, bucket, mentions) = result.Value;
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

            if (activity.IsFrameCommit && activity.FrameLatencyMs.HasValue)
            {
                double commitTime = activity.IsDuration ? activity.EndMs : activity.StartMs;
                string status = activity.IsDelayedFrameCommit ? "DELAYED (> 16.66 ms)" : "ON TIME (<= 16.66 ms)";
                detailsText = $"Frame Latency: {activity.FrameLatencyMs.Value:N3} ms [{status}] (Oldest Request: {activity.FrameRequestMs!.Value:N3} ms → Commit: {commitTime:N3} ms)\n\n" + detailsText;
            }
            else if (activity.IsFrameRequest)
            {
                if (activity.FrameCommitMs.HasValue && activity.FrameLatencyMs.HasValue)
                {
                    detailsText = $"Commit fulfillment in {activity.FrameLatencyMs.Value:N3} ms (Req: {activity.StartMs:N3} ms → Commit: {activity.FrameCommitMs.Value:N3} ms)\n\n" + detailsText;
                }
                else
                {
                    detailsText = "Frame Request queued (unfulfilled commit in trace)\n\n" + detailsText;
                }
            }

            DetailFieldsText.Text = detailsText;
            DetailObjectsList.ItemsSource = row.Objects;
        }
        catch (Exception ex)
        {
            DetailFieldsText.Text = $"{displayName}\nError loading details: {ex.Message}";
        }
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
