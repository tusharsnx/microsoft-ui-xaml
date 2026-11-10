using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace InputTimingSample;

public sealed partial class MainWindow : Window
{
    // Win32 Input Message IDs
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_CHAR = 0x0102;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_POINTERUPDATE = 0x0245;
    private const uint WM_POINTERDOWN = 0x0246;
    private const uint WM_POINTERUP = 0x0247;

    private readonly SubclassProc _subclassDelegate;
    private readonly IntPtr _hwnd;

    // Recording session state
    private bool _isRecording;
    private DateTime _recordingStartTime;
    private readonly object _lock = new();
    private readonly List<TimingRecord> _recordedTimingRecords = new();
    private readonly List<FrameRecord> _recordedFrameRecords = new();
    private readonly List<RenderTimingRecord> _recordedRenderRecords = new();

    // Master collection for unified tabular event log
    private List<UnifiedTimingRecord>? _allUnifiedRecords;

    public MainWindow()
    {
        this.InitializeComponent();

        // 1. Get native window handle for this WinUI 3 Window
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // 2. Install Win32 window subclass hook to intercept messages as soon as CoreMessaging pumps them
        _subclassDelegate = new SubclassProc(WindowSubclassCallback);
        SetWindowSubclass(_hwnd, _subclassDelegate, 1001, IntPtr.Zero);
        this.Closed += (s, e) =>
        {
            try { RemoveWindowSubclass(_hwnd, _subclassDelegate, 1001); } catch { }
        };

        // 3. Connect listeners to our instrumented TextBox
        TestTextBox.TimingRecorded += OnTimingRecorded;
        TestTextBox.FrameRendered += OnFrameRendered;
        TestTextBox.RenderEventOccurred += OnRenderEventOccurred;
    }

    private void OnTimingRecorded(TimingRecord record)
    {
        if (!_isRecording) return;
        lock (_lock)
        {
            _recordedTimingRecords.Add(record);
        }
    }

    private void OnFrameRendered(FrameRecord frame)
    {
        if (!_isRecording) return;
        lock (_lock)
        {
            _recordedFrameRecords.Add(frame);
        }
    }

    private void OnRenderEventOccurred(RenderTimingRecord record)
    {
        if (!_isRecording) return;
        lock (_lock)
        {
            _recordedRenderRecords.Add(record);
        }
    }

    private void OnRecordToggleClicked(object sender, RoutedEventArgs e)
    {
        if (!_isRecording)
        {
            StartRecording();
        }
        else
        {
            StopRecordingAndDisplay();
        }
    }

    private void StartRecording()
    {
        // 1. Clear in-memory buffers
        lock (_lock)
        {
            _recordedTimingRecords.Clear();
            _recordedFrameRecords.Clear();
            _recordedRenderRecords.Clear();
        }

        _allUnifiedRecords = null;
        if (UnifiedEventsListView != null) UnifiedEventsListView.ItemsSource = null;
        if (TableRecordCountTextBlock != null) TableRecordCountTextBlock.Text = "Recording in progress...";
        InstrumentedTextBox.ResetSession();

        // 2. Clear UI views
        TimingTimeline?.UpdateData(Array.Empty<TimingRecord>(), Array.Empty<FrameRecord>(), Array.Empty<RenderTimingRecord>());
        TimingChart?.UpdateData(Array.Empty<TimingRecord>());

        DispatchDelaySummary.Text = "-- ms";
        DispatchDelaySubtitle.Text = "Recording in progress...";
        TextBoxProcessingSummary.Text = "-- ms";
        TextBoxProcessingSubtitle.Text = "Recording in progress...";
        RenderDelaySummary.Text = "-- ms";
        RenderDelaySubtitle.Text = "Recording in progress...";
        TotalLatencySummary.Text = "-- ms";
        TotalLatencySubtitle.Text = "Recording in progress...";

        // 3. Set active recording state
        _recordingStartTime = DateTime.Now;
        _isRecording = true;
        TestTextBox.IsRecording = true;

        // 4. Update UI controls (Red indicator for recording state)
        RecordToggleButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)); // Red (#EF4444)
        RecordToggleText.Text = "⏹ Stop Recording";

        RecordingStatusTextBlock.Text = "● RECORDING IN PROGRESS — All UI updates paused for zero profiling overhead";
        RecordingStatusTextBlock.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68));

        TestTextBox.Focus(FocusState.Programmatic);
    }

    private void StopRecordingAndDisplay()
    {
        // 1. Stop recording immediately to prevent further captures
        _isRecording = false;
        TestTextBox.IsRecording = false;

        double sessionDurationSec = (DateTime.Now - _recordingStartTime).TotalSeconds;

        // 2. Revert button style (Green for start state)
        RecordToggleButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129)); // Green (#10B981)
        RecordToggleText.Text = "▶ Start Recording";

        List<TimingRecord> timingList;
        List<FrameRecord> frameList;
        List<RenderTimingRecord> renderList;

        lock (_lock)
        {
            timingList = new List<TimingRecord>(_recordedTimingRecords);
            frameList = new List<FrameRecord>(_recordedFrameRecords);
            renderList = new List<RenderTimingRecord>(_recordedRenderRecords);
        }

        RecordingStatusTextBlock.Text = $"✓ Recording Complete: {timingList.Count} input events, {frameList.Count} compositor frames, {renderList.Count} render events ({sessionDurationSec:F2}s total). Displayed below.";
        RecordingStatusTextBlock.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));

        // 3. Display the recording of the ENTIRETY at once
        // A. Populate Unified Tabular Event Log
        var allUnified = new List<UnifiedTimingRecord>(timingList.Count + frameList.Count + renderList.Count);

        foreach (var r in timingList)
        {
            bool isKey = r.EventType.Contains("Key", StringComparison.OrdinalIgnoreCase) || 
                         r.EventType.Contains("Char", StringComparison.OrdinalIgnoreCase);
            string cat = isKey ? "Key/Char" : "Pointer";

            allUnified.Add(new UnifiedTimingRecord
            {
                OccurredAt = r.OccurredAt,
                ElapsedSeconds = r.ElapsedSeconds,
                Category = cat,
                CategoryBrush = isKey ? CategoryBrushes.KeyChar : CategoryBrushes.Pointer,
                CategoryBackgroundBrush = isKey ? CategoryBrushes.KeyCharBg : CategoryBrushes.PointerBg,
                EventType = r.EventType,
                Details = r.Details,
                DispatchDelay = r.DispatchDelay,
                ProcessingDelay = r.TextBoxProcessing,
                RenderDelay = r.RenderDelay,
                TotalLatency = r.TotalLatency
            });
        }

        foreach (var r in renderList)
        {
            allUnified.Add(new UnifiedTimingRecord
            {
                OccurredAt = r.OccurredAt,
                ElapsedSeconds = r.ElapsedSeconds,
                Category = r.Category,
                CategoryBrush = CategoryBrushes.GetBrush(r.Category),
                CategoryBackgroundBrush = CategoryBrushes.GetBackgroundBrush(r.Category),
                EventType = r.EventName,
                Details = r.Details,
                DispatchDelay = "-",
                ProcessingDelay = "-",
                RenderDelay = "-",
                TotalLatency = "-"
            });
        }

        foreach (var f in frameList)
        {
            string summary = string.IsNullOrEmpty(f.EventSummary) ? $"{f.EventCount} events batched" : f.EventSummary;
            allUnified.Add(new UnifiedTimingRecord
            {
                OccurredAt = f.RenderedAt,
                ElapsedSeconds = f.ElapsedSeconds,
                Category = "Frame",
                CategoryBrush = CategoryBrushes.Frames,
                CategoryBackgroundBrush = CategoryBrushes.FramesBg,
                EventType = $"Compositor Frame #{f.FrameId}",
                Details = summary,
                DispatchDelay = "-",
                ProcessingDelay = "-",
                RenderDelay = "-",
                TotalLatency = $"{f.FrameDurationMs:F2} ms"
            });
        }

        // Chronological sort
        allUnified.Sort((a, b) => a.ElapsedSeconds.CompareTo(b.ElapsedSeconds));
        for (int i = 0; i < allUnified.Count; i++)
        {
            allUnified[i].Id = i + 1;
        }

        _allUnifiedRecords = allUnified;
        ApplyTableFilter();

        // B. Populate Timelines & Graph with the full recording
        TimingTimeline?.UpdateData(timingList, frameList, renderList);
        TimingChart?.UpdateData(timingList);

        // C. Calculate and display complete session metrics
        if (timingList.Count > 0)
        {
            double avgDispatch = timingList.Average(r => r.DispatchDelayMs);
            double minDispatch = timingList.Min(r => r.DispatchDelayMs);
            double maxDispatch = timingList.Max(r => r.DispatchDelayMs);

            double avgProc = timingList.Average(r => r.TextBoxProcessingMs);
            double minProc = timingList.Min(r => r.TextBoxProcessingMs);
            double maxProc = timingList.Max(r => r.TextBoxProcessingMs);

            double avgRender = timingList.Average(r => r.RenderDelayMs);
            double minRender = timingList.Min(r => r.RenderDelayMs);
            double maxRender = timingList.Max(r => r.RenderDelayMs);

            double avgTotal = timingList.Average(r => r.TotalLatencyMs);
            double minTotal = timingList.Min(r => r.TotalLatencyMs);
            double maxTotal = timingList.Max(r => r.TotalLatencyMs);

            DispatchDelaySummary.Text = $"{avgDispatch:F2} ms";
            DispatchDelaySubtitle.Text = $"Avg (Min: {minDispatch:F2} | Max: {maxDispatch:F2} ms)";

            TextBoxProcessingSummary.Text = $"{avgProc:F2} ms";
            TextBoxProcessingSubtitle.Text = $"Avg (Min: {minProc:F2} | Max: {maxProc:F2} ms)";

            RenderDelaySummary.Text = $"{avgRender:F2} ms";
            RenderDelaySubtitle.Text = $"Avg (Min: {minRender:F2} | Max: {maxRender:F2} ms)";

            TotalLatencySummary.Text = $"{avgTotal:F2} ms";
            TotalLatencySubtitle.Text = $"Avg (Min: {minTotal:F2} | Max: {maxTotal:F2} ms)";
        }
        else
        {
            DispatchDelaySummary.Text = "-- ms";
            DispatchDelaySubtitle.Text = "No events recorded";
            TextBoxProcessingSummary.Text = "-- ms";
            TextBoxProcessingSubtitle.Text = "No events recorded";
            RenderDelaySummary.Text = "-- ms";
            RenderDelaySubtitle.Text = "No events recorded";
            TotalLatencySummary.Text = "-- ms";
            TotalLatencySubtitle.Text = "No events recorded";
        }
    }

    private IntPtr WindowSubclassCallback(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        try
        {
            if (_isRecording)
            {
                long t1 = Stopwatch.GetTimestamp();

                switch (uMsg)
                {
                    case WM_POINTERDOWN:
                    case WM_LBUTTONDOWN:
                        InstrumentedTextBox.LastPointerDownTimestamp = t1;
                        InputTimingEventSource.Log.WindowInputReceived("PointerDown", t1);
                        break;

                    case WM_POINTERUPDATE:
                    case WM_MOUSEMOVE:
                        InstrumentedTextBox.LastPointerUpdateTimestamp = t1;
                        InputTimingEventSource.Log.WindowInputReceived("PointerUpdate", t1);
                        break;

                    case WM_POINTERUP:
                    case WM_LBUTTONUP:
                        InstrumentedTextBox.LastPointerUpTimestamp = t1;
                        InputTimingEventSource.Log.WindowInputReceived("PointerUp", t1);
                        break;

                    case WM_KEYDOWN:
                        InstrumentedTextBox.LastKeyDownTimestamp = t1;
                        InputTimingEventSource.Log.WindowInputReceived("KeyDown", t1);
                        break;

                    case WM_CHAR:
                        InstrumentedTextBox.LastCharTimestamp = t1;
                        InputTimingEventSource.Log.WindowInputReceived("Char", t1);
                        break;
                }
            }
        }
        catch { }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void OnTrackMovesToggled(object sender, RoutedEventArgs e)
    {
        if (TestTextBox != null && TrackMovesCheckBox != null)
        {
            TestTextBox.TrackPointerMoves = TrackMovesCheckBox.IsChecked ?? true;
        }
    }

    private void OnPivotSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRecording) return;

        if (ViewPivot?.SelectedIndex == 0)
        {
            TimingTimeline?.UpdateData(_recordedTimingRecords, _recordedFrameRecords, _recordedRenderRecords);
        }
        else if (ViewPivot?.SelectedIndex == 1)
        {
            TimingChart?.UpdateData(_recordedTimingRecords);
        }
    }

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        _isRecording = false;
        TestTextBox.IsRecording = false;

        RecordToggleButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
        RecordToggleText.Text = "▶ Start Recording";
        RecordingStatusTextBlock.Text = "Status: Idle (Click '▶ Start Recording' to begin)";
        RecordingStatusTextBlock.ClearValue(TextBlock.ForegroundProperty);

        lock (_lock)
        {
            _recordedTimingRecords.Clear();
            _recordedFrameRecords.Clear();
            _recordedRenderRecords.Clear();
        }

        _allUnifiedRecords = null;
        if (UnifiedEventsListView != null) UnifiedEventsListView.ItemsSource = null;
        if (TableRecordCountTextBlock != null) TableRecordCountTextBlock.Text = "0 events recorded";
        InstrumentedTextBox.ResetSession();

        TimingTimeline?.UpdateData(Array.Empty<TimingRecord>(), Array.Empty<FrameRecord>(), Array.Empty<RenderTimingRecord>());
        TimingChart?.UpdateData(Array.Empty<TimingRecord>());

        DispatchDelaySummary.Text = "-- ms";
        DispatchDelaySubtitle.Text = "CoreMessaging -> Control";
        TextBoxProcessingSummary.Text = "-- ms";
        TextBoxProcessingSubtitle.Text = "RichEdit / caret / selection";
        RenderDelaySummary.Text = "-- ms";
        RenderDelaySubtitle.Text = "Layout, draw & frame commit";
        TotalLatencySummary.Text = "-- ms";
        TotalLatencySubtitle.Text = "Input Arrival -> Frame Rendered";
    }

    private void OnTableFilterChanged(object sender, RoutedEventArgs e)
    {
        ApplyTableFilter();
    }

    private void OnTableSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyTableFilter();
    }

    private void ApplyTableFilter()
    {
        if (UnifiedEventsListView == null || _allUnifiedRecords == null) return;

        string filterCat = (TableCategoryFilter?.SelectedItem as ComboBoxItem)?.Content as string ?? "All Categories";
        bool showPointerMoves = TableShowMovesCheckBox?.IsChecked ?? true;
        string search = TableSearchBox?.Text?.Trim() ?? string.Empty;

        IEnumerable<UnifiedTimingRecord> query = _allUnifiedRecords;

        if (!showPointerMoves)
        {
            query = query.Where(r => r.EventType != "PointerUpdate");
        }

        if (filterCat.StartsWith("Inputs", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => r.Category == "Pointer" || r.Category == "Key/Char");
        }
        else if (filterCat.StartsWith("Render", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => r.Category == "Layout" || r.Category == "Viewport" || r.Category == "Content" || r.Category == "VisualState");
        }
        else if (filterCat.StartsWith("Compositor", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => r.Category == "Frame");
        }
        else if (filterCat.StartsWith("Layout", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => r.Category == "Layout");
        }
        else if (filterCat.StartsWith("Viewport", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => r.Category == "Viewport");
        }
        else if (filterCat.StartsWith("Content", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => r.Category == "Content");
        }
        else if (filterCat.StartsWith("Visual", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => r.Category == "VisualState");
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(r => 
                r.EventType.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.Details.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.Category.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var filteredList = query.ToList();
        UnifiedEventsListView.ItemsSource = filteredList;
        if (TableRecordCountTextBlock != null)
        {
            TableRecordCountTextBlock.Text = $"Showing {filteredList.Count:N0} of {_allUnifiedRecords.Count:N0} events";
        }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== UNIFIED EVENT LOG (CHRONOLOGICAL) ===");
        sb.AppendLine("Id,Time,Elapsed,Category,EventType,Details,DispatchDelay,ProcessingDelay,RenderDelay,TotalLatency");
        if (_allUnifiedRecords != null)
        {
            foreach (var r in _allUnifiedRecords)
            {
                sb.AppendLine($"{r.Id},{r.FormattedTime},{r.FormattedElapsed},{r.Category},{r.EventType},\"{r.Details.Replace("\"", "\"\"")}\",{r.DispatchDelay},{r.ProcessingDelay},{r.RenderDelay},{r.TotalLatency}");
            }
        }

        var dp = new DataPackage();
        dp.SetText(sb.ToString());
        Clipboard.SetContent(dp);
    }

    // Win32 Subclass P/Invokes
    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}
