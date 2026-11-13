// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using WinUIProfiler.Engine.Events;

namespace WinUIProfiler.Engine.Timeline;

public readonly record struct TimelineRawEvent(
    long Seq,
    double Ms,
    int ThreadId,
    string Name,
    XamlProvider Provider,
    int Opcode,
    string Bucket);

public sealed class TimelineActivity
{
    public long StartSeq { get; set; }
    public long? EndSeq { get; set; }
    public double StartMs { get; set; }
    public double EndMs { get; set; }
    public double DurationMs => Math.Max(0.001, EndMs - StartMs);
    public string Name { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public int ThreadId { get; set; }
    public XamlProvider Provider { get; set; }
    public int Depth { get; set; }
    public bool IsDuration { get; set; }

    public override string ToString() =>
        IsDuration
            ? $"[{Bucket}] {Name} ({DurationMs:F2} ms) [{StartMs:F2} - {EndMs:F2} ms] Depth={Depth}"
            : $"[{Bucket}] {Name} @ {StartMs:F2} ms Depth={Depth}";
}

public static class TimelineActivityBuilder
{
    public static (IReadOnlyList<TimelineActivity> Activities, double MinMs, double MaxMs) Build(IReadOnlyList<TimelineRawEvent> rawEvents)
    {
        if (rawEvents.Count == 0)
        {
            return (Array.Empty<TimelineActivity>(), 0, 0);
        }

        double minMs = double.MaxValue;
        double maxMs = double.MinValue;
        foreach (var ev in rawEvents)
        {
            if (ev.Ms < minMs) minMs = ev.Ms;
            if (ev.Ms > maxMs) maxMs = ev.Ms;
        }

        if (minMs == double.MaxValue) minMs = 0;
        if (maxMs == double.MinValue) maxMs = 0;

        // Pair Start and Stop events per thread and task name
        var openStarts = new Dictionary<(int ThreadId, string BaseName), Stack<TimelineRawEvent>>();
        var activities = new List<TimelineActivity>();

        foreach (var ev in rawEvents)
        {
            var (isStart, isStop, baseName) = AnalyzeEvent(ev);

            if (isStart)
            {
                var key = (ev.ThreadId, baseName);
                if (!openStarts.TryGetValue(key, out var stack))
                {
                    stack = new Stack<TimelineRawEvent>();
                    openStarts[key] = stack;
                }
                stack.Push(ev);
            }
            else if (isStop)
            {
                var key = (ev.ThreadId, baseName);
                if (openStarts.TryGetValue(key, out var stack) && stack.Count > 0)
                {
                    var start = stack.Pop();
                    activities.Add(new TimelineActivity
                    {
                        StartSeq = start.Seq,
                        EndSeq = ev.Seq,
                        StartMs = start.Ms,
                        EndMs = Math.Max(start.Ms, ev.Ms),
                        Name = baseName,
                        Bucket = start.Bucket,
                        ThreadId = start.ThreadId,
                        Provider = start.Provider,
                        IsDuration = true
                    });
                }
                else
                {
                    // Unmatched stop - keep as point event
                    activities.Add(new TimelineActivity
                    {
                        StartSeq = ev.Seq,
                        EndSeq = ev.Seq,
                        StartMs = ev.Ms,
                        EndMs = ev.Ms,
                        Name = ev.Name,
                        Bucket = ev.Bucket,
                        ThreadId = ev.ThreadId,
                        Provider = ev.Provider,
                        IsDuration = false
                    });
                }
            }
            else
            {
                // Regular point event
                activities.Add(new TimelineActivity
                {
                    StartSeq = ev.Seq,
                    EndSeq = ev.Seq,
                    StartMs = ev.Ms,
                    EndMs = ev.Ms,
                    Name = ev.Name,
                    Bucket = ev.Bucket,
                    ThreadId = ev.ThreadId,
                    Provider = ev.Provider,
                    IsDuration = false
                });
            }
        }

        // Close any dangling start events at maxMs
        foreach (var kvp in openStarts)
        {
            while (kvp.Value.Count > 0)
            {
                var start = kvp.Value.Pop();
                activities.Add(new TimelineActivity
                {
                    StartSeq = start.Seq,
                    EndSeq = null,
                    StartMs = start.Ms,
                    EndMs = Math.Max(start.Ms, maxMs),
                    Name = kvp.Key.BaseName,
                    Bucket = start.Bucket,
                    ThreadId = start.ThreadId,
                    Provider = start.Provider,
                    IsDuration = true
                });
            }
        }

        // Assign waterfall depths per bucket
        AssignDepths(activities);

        return (activities, minMs, maxMs);
    }

    private static (bool IsStart, bool IsStop, string BaseName) AnalyzeEvent(TimelineRawEvent ev)
    {
        var name = ev.Name;

        // Certain telemetry/method-call events ending with Start/Stop are instant point events and must not form a span
        if ((name.Contains("DispatcherTimer", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("DisptacherTimer", StringComparison.OrdinalIgnoreCase)) &&
            (name.EndsWith("Start", StringComparison.OrdinalIgnoreCase) ||
             name.EndsWith("Stop", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Start", StringComparison.OrdinalIgnoreCase) && name.Contains("Timer", StringComparison.OrdinalIgnoreCase)))
        {
            return (false, false, name);
        }

        // Opcode checks: 1 = Start, 2 = Stop
        if (ev.Opcode == 1)
        {
            var baseName = StripSuffix(name, "/Start", "_Start", "Start", "/Begin", "_Begin");
            return (true, false, baseName);
        }
        if (ev.Opcode == 2)
        {
            var baseName = StripSuffix(name, "/Stop", "_Stop", "Stop", "/End", "_End");
            return (false, true, baseName);
        }

        // Name suffix checks
        if (name.EndsWith("/Start", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_Start", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("/Begin", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_Begin", StringComparison.OrdinalIgnoreCase))
        {
            var baseName = StripSuffix(name, "/Start", "_Start", "/Begin", "_Begin");
            return (true, false, baseName);
        }

        if (name.EndsWith("/Stop", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_Stop", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("/End", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_End", StringComparison.OrdinalIgnoreCase))
        {
            var baseName = StripSuffix(name, "/Stop", "_Stop", "/End", "_End");
            return (false, true, baseName);
        }

        return (false, false, name);
    }

    private static string StripSuffix(string name, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length];
            }
        }
        return name;
    }

    private static void AssignDepths(List<TimelineActivity> activities)
    {
        // Group activities by bucket: Xaml, Composition, Other
        var groups = activities.GroupBy(a => a.Bucket);

        foreach (var group in groups)
        {
            // Sort by StartMs ascending, then duration descending (parents first)
            var sorted = group.OrderBy(a => a.StartMs).ThenByDescending(a => a.DurationMs).ToList();
            var laneEndTimes = new List<double>();

            foreach (var activity in sorted)
            {
                int assignedLane = -1;
                for (int i = 0; i < laneEndTimes.Count; i++)
                {
                    if (laneEndTimes[i] <= activity.StartMs)
                    {
                        assignedLane = i;
                        break;
                    }
                }

                if (assignedLane == -1)
                {
                    assignedLane = laneEndTimes.Count;
                    laneEndTimes.Add(activity.EndMs);
                }
                else
                {
                    laneEndTimes[assignedLane] = activity.EndMs;
                }

                activity.Depth = assignedLane;
            }
        }
    }
}
