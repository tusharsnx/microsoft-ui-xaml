using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using Microsoft.UI.Xaml.Media;

namespace InputTimingSample;

[EventSource(Name = "Microsoft-WinUI-InputTimingSample")]
public sealed class InputTimingEventSource : EventSource
{
    public static readonly InputTimingEventSource Log = new InputTimingEventSource();

    [Event(1, Level = EventLevel.Informational, Message = "Input Window Received: {0}")]
    public void WindowInputReceived(string inputType, long qpcTimestamp)
    {
        if (IsEnabled()) WriteEvent(1, inputType, qpcTimestamp);
    }

    [Event(2, Level = EventLevel.Informational, Message = "TextBox Processing Started: {0}")]
    public void TextBoxProcessingStarted(string inputType, long qpcTimestamp)
    {
        if (IsEnabled()) WriteEvent(2, inputType, qpcTimestamp);
    }

    [Event(3, Level = EventLevel.Informational, Message = "TextBox Processing Finished: {0}")]
    public void TextBoxProcessingFinished(string inputType, long qpcTimestamp, double processingMs)
    {
        if (IsEnabled()) WriteEvent(3, inputType, qpcTimestamp, processingMs);
    }

    [Event(4, Level = EventLevel.Informational, Message = "Frame Render Started: Total Latency {0} ms")]
    public void FrameRenderStarted(double totalLatencyMs, double renderWaitMs)
    {
        if (IsEnabled()) WriteEvent(4, totalLatencyMs, renderWaitMs);
    }

    [Event(5, Level = EventLevel.Informational, Message = "TextBox Render Event: {0} ({1})")]
    public void TextBoxRenderEvent(string eventName, string details)
    {
        if (IsEnabled()) WriteEvent(5, eventName, details);
    }
}

public class TimingRecord
{
    public int Id { get; set; }
    public int FrameId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    
    // Precise occurrence timestamps
    public DateTime OccurredAt { get; set; } = DateTime.Now;
    public string FormattedTime => OccurredAt.ToString("HH:mm:ss.fff");
    public long QpcTimestamp { get; set; }
    public double ElapsedSeconds { get; set; }
    public string FormattedElapsed => $"+{ElapsedSeconds:F3}s";

    // Formatted strings for display in UI
    public string DispatchDelay { get; set; } = string.Empty;
    public string TextBoxProcessing { get; set; } = string.Empty;
    public string RenderDelay { get; set; } = string.Empty;
    public string TotalLatency { get; set; } = string.Empty;
    public string FrameDuration { get; set; } = string.Empty;

    // Raw numbers (in milliseconds)
    public double DispatchDelayMs { get; set; }
    public double TextBoxProcessingMs { get; set; }
    public double RenderDelayMs { get; set; }
    public double TotalLatencyMs { get; set; }
    public double FrameDurationMs { get; set; }
    public ulong HardwareTimestampUs { get; set; }
}

public class FrameRecord
{
    public int FrameId { get; set; }
    public DateTime RenderedAt { get; set; } = DateTime.Now;
    public string FormattedTime => RenderedAt.ToString("HH:mm:ss.fff");
    public double ElapsedSeconds { get; set; }
    public string FormattedElapsed => $"+{ElapsedSeconds:F3}s";
    public double FrameDurationMs { get; set; }
    public string FrameDuration => $"{FrameDurationMs:F3} ms";
    public int EventCount { get; set; }
    public string EventSummary { get; set; } = string.Empty;
    public List<int> EventIds { get; set; } = new();
}

public class RenderTimingRecord
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty; // "Composition", "Layout", "Viewport", "Content", "VisualState"
    public string EventName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.Now;
    public string FormattedTime => OccurredAt.ToString("HH:mm:ss.fff");
    public long QpcTimestamp { get; set; }
    public double ElapsedSeconds { get; set; }
    public string FormattedElapsed => $"+{ElapsedSeconds:F3}s";
}

public class UnifiedTimingRecord
{
    public int Id { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.Now;
    public string FormattedTime => OccurredAt.ToString("HH:mm:ss.fff");
    public double ElapsedSeconds { get; set; }
    public string FormattedElapsed => $"+{ElapsedSeconds:F3}s";

    public string Category { get; set; } = string.Empty; // "Pointer", "Key/Char", "Layout", "Viewport", "Content", "VisualState", "Frame"
    public string EventType { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;

    // Latency metrics
    public string DispatchDelay { get; set; } = "-";
    public string ProcessingDelay { get; set; } = "-";
    public string RenderDelay { get; set; } = "-";
    public string TotalLatency { get; set; } = "-";

    // Visual Brushes for badges and styling
    public Brush? CategoryBrush { get; set; }
    public Brush? CategoryBackgroundBrush { get; set; }
}

public static class CategoryBrushes
{
    private static Brush? s_frames;
    private static Brush? s_framesBg;
    private static Brush? s_layout;
    private static Brush? s_layoutBg;
    private static Brush? s_viewport;
    private static Brush? s_viewportBg;
    private static Brush? s_content;
    private static Brush? s_contentBg;
    private static Brush? s_keyChar;
    private static Brush? s_keyCharBg;
    private static Brush? s_pointer;
    private static Brush? s_pointerBg;
    private static Brush? s_visualState;
    private static Brush? s_visualStateBg;
    private static Brush? s_default;
    private static Brush? s_defaultBg;

    public static Brush Frames => s_frames ??= new SolidColorBrush(Windows.UI.Color.FromArgb(255, 168, 85, 247)); // #A855F7
    public static Brush FramesBg => s_framesBg ??= new SolidColorBrush(Windows.UI.Color.FromArgb(45, 168, 85, 247));

    public static Brush Layout => s_layout ??= new SolidColorBrush(Windows.UI.Color.FromArgb(255, 56, 189, 248)); // #38BDF8
    public static Brush LayoutBg => s_layoutBg ??= new SolidColorBrush(Windows.UI.Color.FromArgb(45, 56, 189, 248));

    public static Brush Viewport => s_viewport ??= new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 212, 191)); // #2DD4BF
    public static Brush ViewportBg => s_viewportBg ??= new SolidColorBrush(Windows.UI.Color.FromArgb(45, 45, 212, 191));

    public static Brush Content => s_content ??= new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)); // #34D399
    public static Brush ContentBg => s_contentBg ??= new SolidColorBrush(Windows.UI.Color.FromArgb(45, 52, 211, 153));

    public static Brush KeyChar => s_keyChar ??= new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36)); // #FBBF24
    public static Brush KeyCharBg => s_keyCharBg ??= new SolidColorBrush(Windows.UI.Color.FromArgb(45, 251, 191, 36));

    public static Brush Pointer => s_pointer ??= new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 165, 250)); // #60A5FA
    public static Brush PointerBg => s_pointerBg ??= new SolidColorBrush(Windows.UI.Color.FromArgb(45, 96, 165, 250));

    public static Brush VisualState => s_visualState ??= new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)); // #F87171
    public static Brush VisualStateBg => s_visualStateBg ??= new SolidColorBrush(Windows.UI.Color.FromArgb(45, 248, 113, 113));

    public static Brush Default => s_default ??= new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175)); // #9CA3AF
    public static Brush DefaultBg => s_defaultBg ??= new SolidColorBrush(Windows.UI.Color.FromArgb(45, 156, 163, 175));

    public static Brush GetBrush(string category) => category switch
    {
        "Frame" => Frames,
        "Layout" => Layout,
        "Viewport" => Viewport,
        "Content" => Content,
        "Key/Char" => KeyChar,
        "Pointer" => Pointer,
        "VisualState" => VisualState,
        _ => Default
    };

    public static Brush GetBackgroundBrush(string category) => category switch
    {
        "Frame" => FramesBg,
        "Layout" => LayoutBg,
        "Viewport" => ViewportBg,
        "Content" => ContentBg,
        "Key/Char" => KeyCharBg,
        "Pointer" => PointerBg,
        "VisualState" => VisualStateBg,
        _ => DefaultBg
    };
}
