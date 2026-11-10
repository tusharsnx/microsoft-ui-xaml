using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace InputTimingSample;

public partial class InstrumentedTextBox : TextBox
{
    private static int s_eventCounter;
    private static int s_frameCounter;
    private static int s_renderEventCounter;
    private static long s_sessionStartTimestamp;

    // State for tracking a single active CompositionTarget.Rendered callback
    private static bool s_isRenderedHooked;
    private static bool s_isRenderingHooked;
    private static readonly List<PendingTiming> s_pendingEvents = new();

    // Precise per-message window arrival timestamps (T1) set by MainWindow's HWND subclass proc
    public static long LastPointerDownTimestamp { get; set; }
    public static long LastPointerUpdateTimestamp { get; set; }
    public static long LastPointerUpTimestamp { get; set; }
    public static long LastKeyDownTimestamp { get; set; }
    public static long LastCharTimestamp { get; set; }

    public static void ResetSession()
    {
        s_eventCounter = 0;
        s_frameCounter = 0;
        s_renderEventCounter = 0;
        s_sessionStartTimestamp = 0;
        lock (s_pendingEvents)
        {
            s_pendingEvents.Clear();
        }
        LastPointerDownTimestamp = 0;
        LastPointerUpdateTimestamp = 0;
        LastPointerUpTimestamp = 0;
        LastKeyDownTimestamp = 0;
        LastCharTimestamp = 0;
    }

    public bool IsRecording { get; set; } = false;
    public bool TrackPointerMoves { get; set; } = true;

    public event Action<TimingRecord>? TimingRecorded;
    public event Action<FrameRecord>? FrameRendered;
    public event Action<RenderTimingRecord>? RenderEventOccurred;

    public InstrumentedTextBox()
    {
        // 1. Content Mutation Events
        this.TextChanging += (s, e) => EmitRenderEvent("Content", "TextChanging", $"Length: {this.Text.Length}");
        this.TextChanged += (s, e) => EmitRenderEvent("Content", "TextChanged", $"Length: {this.Text.Length}");
        this.SelectionChanging += (s, e) => EmitRenderEvent("Content", "SelectionChanging", $"Start: {e.SelectionStart}, Length: {e.SelectionLength}");
        this.SelectionChanged += (s, e) => EmitRenderEvent("Content", "SelectionChanged", $"Start: {this.SelectionStart}, Length: {this.SelectionLength}");

        // 2. Viewport & Scrolling Events
        this.BringIntoViewRequested += (s, e) => EmitRenderEvent("Viewport", "BringIntoViewRequested", 
            $"TargetRect: (X:{e.TargetRect.X:F0}, Y:{e.TargetRect.Y:F0}, {e.TargetRect.Width:F0}x{e.TargetRect.Height:F0}), Anim: {e.AnimationDesired}");
        this.EffectiveViewportChanged += (s, e) => EmitRenderEvent("Viewport", "EffectiveViewportChanged", 
            $"Viewport: {e.EffectiveViewport.Width:F0}x{e.EffectiveViewport.Height:F0}");

        // 3. Layout Pass Events
        this.SizeChanged += (s, e) => EmitRenderEvent("Layout", "SizeChanged", 
            $"NewSize: {e.NewSize.Width:F1}x{e.NewSize.Height:F1} (Prev: {e.PreviousSize.Width:F1}x{e.PreviousSize.Height:F1})");
        this.LayoutUpdated += (s, e) => EmitRenderEvent("Layout", "LayoutUpdated", 
            $"Tree Layout Done ({this.ActualWidth:F1}x{this.ActualHeight:F1})");

        // 4. Visual State Events
        this.GotFocus += (s, e) => EmitRenderEvent("VisualState", "GotFocus", $"FocusState: {this.FocusState}");
        this.LostFocus += (s, e) => EmitRenderEvent("VisualState", "LostFocus", $"FocusState: {this.FocusState}");
    }

    private void EmitRenderEvent(string category, string eventName, string details)
    {
        if (!IsRecording) return;

        long now = Stopwatch.GetTimestamp();
        double freq = Stopwatch.Frequency;
        if (s_sessionStartTimestamp == 0)
        {
            s_sessionStartTimestamp = now;
        }

        double elapsed = (now - s_sessionStartTimestamp) / freq;
        if (elapsed < 0) elapsed = 0;

        int id = ++s_renderEventCounter;
        var record = new RenderTimingRecord
        {
            Id = id,
            Category = category,
            EventName = eventName,
            Details = details,
            OccurredAt = DateTime.Now,
            QpcTimestamp = now,
            ElapsedSeconds = elapsed
        };

        InputTimingEventSource.Log.TextBoxRenderEvent(eventName, details);
        RenderEventOccurred?.Invoke(record);
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (!IsRecording)
        {
            base.OnPointerPressed(e);
            return;
        }

        DateTime occurredAt = DateTime.Now;
        int eventId = ++s_eventCounter;
        long t1_window = LastPointerDownTimestamp;
        ulong hwTimestampUs = e.GetCurrentPoint(this).Timestamp;
        var pt = e.GetCurrentPoint(this).Position;

        // T2: TextBox begins processing PointerPressed (focus, capture, cursor placement, selection)
        long t2_start = Stopwatch.GetTimestamp();
        InputTimingEventSource.Log.TextBoxProcessingStarted("PointerPressed", t2_start);

        base.OnPointerPressed(e);

        // T3: TextBox finishes processing PointerPressed
        long t3_finish = Stopwatch.GetTimestamp();
        RecordAndHookRender(eventId, "PointerPressed", $"{e.Pointer.PointerDeviceType} (X:{pt.X:F0}, Y:{pt.Y:F0})", t1_window, t2_start, t3_finish, hwTimestampUs, occurredAt);
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        if (!IsRecording || !TrackPointerMoves)
        {
            base.OnPointerMoved(e);
            return;
        }

        DateTime occurredAt = DateTime.Now;
        int eventId = ++s_eventCounter;
        long t1_window = LastPointerUpdateTimestamp;
        ulong hwTimestampUs = e.GetCurrentPoint(this).Timestamp;
        var pt = e.GetCurrentPoint(this).Position;

        // T2: TextBox begins processing PointerMoved (cursor shape update, drag-selection if button held)
        long t2_start = Stopwatch.GetTimestamp();
        InputTimingEventSource.Log.TextBoxProcessingStarted("PointerMoved", t2_start);

        base.OnPointerMoved(e);

        // T3: TextBox finishes processing PointerMoved
        long t3_finish = Stopwatch.GetTimestamp();
        RecordAndHookRender(eventId, "PointerMoved", $"{e.Pointer.PointerDeviceType} (X:{pt.X:F0}, Y:{pt.Y:F0})", t1_window, t2_start, t3_finish, hwTimestampUs, occurredAt);
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (!IsRecording)
        {
            base.OnPointerReleased(e);
            return;
        }

        DateTime occurredAt = DateTime.Now;
        int eventId = ++s_eventCounter;
        long t1_window = LastPointerUpTimestamp;
        ulong hwTimestampUs = e.GetCurrentPoint(this).Timestamp;
        var pt = e.GetCurrentPoint(this).Position;

        // T2: TextBox begins processing PointerReleased (finalizing selection, releasing pointer capture)
        long t2_start = Stopwatch.GetTimestamp();
        InputTimingEventSource.Log.TextBoxProcessingStarted("PointerReleased", t2_start);

        base.OnPointerReleased(e);

        // T3: TextBox finishes processing PointerReleased
        long t3_finish = Stopwatch.GetTimestamp();
        RecordAndHookRender(eventId, "PointerReleased", $"{e.Pointer.PointerDeviceType} (X:{pt.X:F0}, Y:{pt.Y:F0})", t1_window, t2_start, t3_finish, hwTimestampUs, occurredAt);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (!IsRecording)
        {
            base.OnKeyDown(e);
            return;
        }

        DateTime occurredAt = DateTime.Now;
        int eventId = ++s_eventCounter;
        long t1_window = LastKeyDownTimestamp;

        // T2: TextBox begins processing KeyDown (navigation keys, backspace, enter, delete, shortcuts)
        long t2_start = Stopwatch.GetTimestamp();
        InputTimingEventSource.Log.TextBoxProcessingStarted($"KeyDown_{e.Key}", t2_start);

        base.OnKeyDown(e);

        // T3: TextBox finishes processing KeyDown
        long t3_finish = Stopwatch.GetTimestamp();
        RecordAndHookRender(eventId, "KeyDown", $"{e.Key} (VK: {(int)e.Key})", t1_window, t2_start, t3_finish, 0, occurredAt);
    }

    protected override void OnCharacterReceived(CharacterReceivedRoutedEventArgs e)
    {
        if (!IsRecording)
        {
            base.OnCharacterReceived(e);
            return;
        }

        DateTime occurredAt = DateTime.Now;
        int eventId = ++s_eventCounter;
        long t1_window = LastCharTimestamp;

        // T2: TextBox begins processing CharacterReceived (RichEdit text insertion, metrics, TextChanged event)
        long t2_start = Stopwatch.GetTimestamp();
        InputTimingEventSource.Log.TextBoxProcessingStarted($"Char_{(int)e.Character}", t2_start);

        base.OnCharacterReceived(e);

        // T3: TextBox finishes processing CharacterReceived
        long t3_finish = Stopwatch.GetTimestamp();
        string charDisplay = char.IsControl(e.Character) ? $"0x{(int)e.Character:X2}" : $"'{e.Character}'";
        RecordAndHookRender(eventId, "CharacterReceived", charDisplay, t1_window, t2_start, t3_finish, 0, occurredAt);
    }

    private void RecordAndHookRender(int eventId, string eventType, string details, long t1_window, long t2_start, long t3_finish, ulong hwTimestampUs, DateTime occurredAt)
    {
        double freq = Stopwatch.Frequency;
        if (s_sessionStartTimestamp == 0)
        {
            s_sessionStartTimestamp = t1_window > 0 ? t1_window : t2_start;
        }

        double elapsedSeconds = (t2_start - s_sessionStartTimestamp) / freq;
        if (elapsedSeconds < 0) elapsedSeconds = 0;

        double processingMs = (t3_finish - t2_start) * 1000.0 / freq;
        InputTimingEventSource.Log.TextBoxProcessingFinished(eventType, t3_finish, processingMs);

        // Queue event for the upcoming frame render
        lock (s_pendingEvents)
        {
            s_pendingEvents.Add(new PendingTiming(eventId, eventType, details, t1_window, t2_start, t3_finish, hwTimestampUs, occurredAt, elapsedSeconds, processingMs));
        }

        // Hook Rendering start tick if not already hooked
        if (!s_isRenderingHooked)
        {
            s_isRenderingHooked = true;
            void OnRenderingTick(object? s, object args)
            {
                CompositionTarget.Rendering -= OnRenderingTick;
                s_isRenderingHooked = false;
                EmitRenderEvent("Composition", "Rendering", "Frame Tick Started");
            }
            CompositionTarget.Rendering += OnRenderingTick;
        }

        // Only register a single Rendered callback if one is not already queued
        if (!s_isRenderedHooked)
        {
            s_isRenderedHooked = true;
            CompositionTarget.Rendered += OnFrameRendered;
        }
    }

    private void OnFrameRendered(object? sender, RenderedEventArgs args)
    {
        // Unhook immediately so only one callback is active at any time
        CompositionTarget.Rendered -= OnFrameRendered;
        s_isRenderedHooked = false;

        long t4_rendered = Stopwatch.GetTimestamp();
        double freq = Stopwatch.Frequency;
        double frameDurationMs = args.FrameDuration.TotalMilliseconds;

        List<PendingTiming> batch;
        lock (s_pendingEvents)
        {
            if (s_pendingEvents.Count == 0) return;
            batch = new List<PendingTiming>(s_pendingEvents);
            s_pendingEvents.Clear();
        }

        int frameId = ++s_frameCounter;
        DateTime frameTime = DateTime.Now;
        if (s_sessionStartTimestamp == 0) s_sessionStartTimestamp = t4_rendered;
        double frameElapsed = (t4_rendered - s_sessionStartTimestamp) / freq;
        if (frameElapsed < 0) frameElapsed = 0;

        var frameRecord = new FrameRecord
        {
            FrameId = frameId,
            RenderedAt = frameTime,
            ElapsedSeconds = frameElapsed,
            FrameDurationMs = frameDurationMs,
            EventCount = batch.Count,
            EventSummary = string.Join(", ", batch.GroupBy(e => e.EventType).Select(g => $"{g.Count()}x {g.Key}")),
            EventIds = batch.Select(e => e.EventId).ToList()
        };

        foreach (var p in batch)
        {
            double dispatchDelayMs = p.T1Window > 0 ? Math.Max(0, (p.T2Start - p.T1Window) * 1000.0 / freq) : 0;
            double renderDelayMs = Math.Max(0, (t4_rendered - p.T3Finish) * 1000.0 / freq);
            double totalLatencyMs = p.T1Window > 0 ? Math.Max(0, (t4_rendered - p.T1Window) * 1000.0 / freq) : (t4_rendered - p.T2Start) * 1000.0 / freq;

            InputTimingEventSource.Log.FrameRenderStarted(totalLatencyMs, renderDelayMs);

            var record = new TimingRecord
            {
                Id = p.EventId,
                FrameId = frameId,
                EventType = p.EventType,
                Details = p.Details,
                OccurredAt = p.OccurredAt,
                QpcTimestamp = p.T2Start,
                ElapsedSeconds = p.ElapsedSeconds,
                DispatchDelayMs = dispatchDelayMs,
                TextBoxProcessingMs = p.ProcessingMs,
                RenderDelayMs = renderDelayMs,
                TotalLatencyMs = totalLatencyMs,
                FrameDurationMs = frameDurationMs,
                DispatchDelay = $"{dispatchDelayMs:F3} ms",
                TextBoxProcessing = $"{p.ProcessingMs:F3} ms",
                RenderDelay = $"{renderDelayMs:F3} ms",
                TotalLatency = $"{totalLatencyMs:F3} ms",
                FrameDuration = $"{frameDurationMs:F3} ms",
                HardwareTimestampUs = p.HwTimestampUs
            };

            TimingRecorded?.Invoke(record);
        }

        FrameRendered?.Invoke(frameRecord);
        EmitRenderEvent("Composition", "Rendered", $"Frame #{frameId} Committed | Engine Layout/Draw: {frameDurationMs:F3} ms | {batch.Count} event(s)");
    }

    private readonly struct PendingTiming
    {
        public readonly int EventId;
        public readonly string EventType;
        public readonly string Details;
        public readonly long T1Window;
        public readonly long T2Start;
        public readonly long T3Finish;
        public readonly ulong HwTimestampUs;
        public readonly DateTime OccurredAt;
        public readonly double ElapsedSeconds;
        public readonly double ProcessingMs;

        public PendingTiming(int eventId, string eventType, string details, long t1Window, long t2Start, long t3Finish, ulong hwTimestampUs, DateTime occurredAt, double elapsedSeconds, double processingMs)
        {
            EventId = eventId;
            EventType = eventType;
            Details = details;
            T1Window = t1Window;
            T2Start = t2Start;
            T3Finish = t3Finish;
            HwTimestampUs = hwTimestampUs;
            OccurredAt = occurredAt;
            ElapsedSeconds = elapsedSeconds;
            ProcessingMs = processingMs;
        }
    }
}
