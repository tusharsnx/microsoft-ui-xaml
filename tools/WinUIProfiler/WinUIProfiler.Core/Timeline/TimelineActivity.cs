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
    string Bucket,
    int IsStartVal = -1);

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
    public double? FrameLatencyMs { get; set; }
    public double? FrameRequestMs { get; set; }
    public double? FrameCommitMs { get; set; }

    public bool IsFrameRequest => FrameMarkers.IsFrameRequest(Name);
    public bool IsFrameCommit => FrameMarkers.IsFrameCommit(Name);
    public bool IsDelayedFrameCommit => IsFrameCommit && ((FrameLatencyMs.HasValue && FrameLatencyMs.Value > 16.66) || (IsDuration && DurationMs > 16.66));
    public bool IsSchedulerWait => FrameMarkers.IsSchedulerWait(Name);
    public bool IsLock => FrameMarkers.IsLock(Name);
    public bool IsLockBegin => FrameMarkers.IsLockBegin(Name);
    public bool IsLockEnd => FrameMarkers.IsLockEnd(Name);

    public override string ToString() =>
        IsDuration
            ? $"[{Bucket}] {Name} ({DurationMs:F2} ms) [{StartMs:F2} - {EndMs:F2} ms] Depth={Depth}"
            : $"[{Bucket}] {Name} @ {StartMs:F2} ms Depth={Depth}";
}

public sealed class TimelineLockRegion
{
    public int ThreadId { get; set; }
    public double StartMs { get; set; }
    public double EndMs { get; set; }
    public double DurationMs => Math.Max(0.001, EndMs - StartMs);
    public string Name { get; set; } = "CompositorLock";
    public long StartSeq { get; set; }
    public long EndSeq { get; set; }
    public XamlProvider Provider { get; set; }
}

public static class TimelineActivityBuilder
{
    public static (IReadOnlyList<TimelineActivity> Activities, double MinMs, double MaxMs, IReadOnlyList<TimelineLockRegion> LockRegions) Build(IReadOnlyList<TimelineRawEvent> rawEvents)
    {
        if (rawEvents.Count == 0)
        {
            return (Array.Empty<TimelineActivity>(), 0, 0, Array.Empty<TimelineLockRegion>());
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
        var openLocks = new Dictionary<int, TimelineRawEvent>();
        var lockRegions = new List<TimelineLockRegion>();

        foreach (var ev in rawEvents)
        {
            if (FrameMarkers.IsLockBegin(ev.Name))
            {
                openLocks[ev.ThreadId] = ev;
            }
            else if (FrameMarkers.IsLockEnd(ev.Name))
            {
                if (openLocks.TryGetValue(ev.ThreadId, out var lockStart))
                {
                    lockRegions.Add(new TimelineLockRegion
                    {
                        ThreadId = ev.ThreadId,
                        StartMs = lockStart.Ms,
                        EndMs = Math.Max(lockStart.Ms, ev.Ms),
                        StartSeq = lockStart.Seq,
                        EndSeq = ev.Seq,
                        Provider = lockStart.Provider,
                        Name = "CompositorLock"
                    });
                    openLocks.Remove(ev.ThreadId);
                }
            }

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
                        Bucket = FrameMarkers.IsSchedulerWait(baseName) ? "Idle" : start.Bucket,
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
                        Bucket = FrameMarkers.IsSchedulerWait(ev.Name) ? "Idle" : ev.Bucket,
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
                    Bucket = FrameMarkers.IsSchedulerWait(ev.Name) ? "Idle" : ev.Bucket,
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
                    Bucket = FrameMarkers.IsSchedulerWait(kvp.Key.BaseName) ? "Idle" : start.Bucket,
                    ThreadId = start.ThreadId,
                    Provider = start.Provider,
                    IsDuration = true
                });
            }
        }

        // Assign waterfall depths per thread
        AssignDepths(activities);

        // Calculate frame latency for committed frame events
        CalculateFrameLatencies(activities);

        return (activities, minMs, maxMs, lockRegions);
    }

    private static (bool IsStart, bool IsStop, string BaseName) AnalyzeEvent(TimelineRawEvent ev)
    {
        var name = ev.Name;

        // CompositorLock events are displayed as instant events
        if (name.Contains("CompositorLock", StringComparison.OrdinalIgnoreCase))
        {
            return (false, false, name);
        }

        // Certain telemetry/method-call events ending with Start/Stop are instant point events and must not form a span
        if ((name.Contains("DispatcherTimer", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("DisptacherTimer", StringComparison.OrdinalIgnoreCase)) &&
            (name.EndsWith("Start", StringComparison.OrdinalIgnoreCase) ||
             name.EndsWith("Stop", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Start", StringComparison.OrdinalIgnoreCase) && name.Contains("Timer", StringComparison.OrdinalIgnoreCase)))
        {
            return (false, false, name);
        }

        // Use isStart to create a span for Scheduling_RenderThreadWaitForWork and RefreshRateInfo_WaitForCompositorClock
        if (name.Equals("Scheduling_RenderThreadWaitForWork", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("RefreshRateInfo_WaitForCompositorClock", StringComparison.OrdinalIgnoreCase))
        {
            if (ev.IsStartVal == 1)
            {
                return (true, false, name);
            }
            if (ev.IsStartVal == 0)
            {
                return (false, true, name);
            }
            return (false, false, name);
        }

        // Opcode checks: 1 = Start, 2 = Stop
        if (ev.Opcode == 1)
        {
            var baseName = StripSuffix(name, "/Start", "_Start", "Start", "/Begin", "_Begin", "Begin");
            return (true, false, baseName);
        }
        if (ev.Opcode == 2)
        {
            var baseName = StripSuffix(name, "/Stop", "_Stop", "Stop", "/End", "_End", "End");
            return (false, true, baseName);
        }

        // Name suffix checks
        if (name.EndsWith("/Start", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_Start", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("/Begin", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_Begin", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Begin", StringComparison.OrdinalIgnoreCase))
        {
            var baseName = StripSuffix(name, "/Start", "_Start", "/Begin", "_Begin", "Begin");
            return (true, false, baseName);
        }

        if (name.EndsWith("/Stop", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_Stop", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("/End", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_End", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("End", StringComparison.OrdinalIgnoreCase))
        {
            var baseName = StripSuffix(name, "/Stop", "_Stop", "/End", "_End", "End");
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
        // Group activities per process thread
        var groups = activities.GroupBy(a => a.ThreadId);

        foreach (var group in groups)
        {
            // Sort by StartMs ascending, then duration descending (parents first)
            var sorted = group.OrderBy(a => a.StartMs).ThenByDescending(a => a.DurationMs).ToList();
            var laneEndTimes = new List<double>();

            foreach (var activity in sorted)
            {
                if (activity.IsLock)
                {
                    // Lock events are displayed as instant tick events and do not cause intermediate events to waterfall
                    activity.Depth = 0;
                    continue;
                }

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

    private static void CalculateFrameLatencies(List<TimelineActivity> activities)
    {
        // Get all frame requests and commits, ordered chronologically by effective timestamp
        var frameEvents = activities
            .Where(a => a.IsFrameRequest || a.IsFrameCommit)
            .OrderBy(a => a.IsFrameCommit && a.IsDuration ? a.EndMs : a.StartMs)
            .ThenBy(a => a.IsFrameRequest ? 0 : 1) // If same timestamp, request precedes commit
            .ToList();

        var unfulfilledRequests = new Queue<TimelineActivity>();

        foreach (var act in frameEvents)
        {
            if (act.IsFrameRequest)
            {
                unfulfilledRequests.Enqueue(act);
            }
            else if (act.IsFrameCommit)
            {
                double commitTime = act.IsDuration ? act.EndMs : act.StartMs;

                // The duration between the commit time and the oldest unfulfilled request time
                if (unfulfilledRequests.Count > 0 && unfulfilledRequests.Peek().StartMs <= commitTime)
                {
                    var oldestReq = unfulfilledRequests.Peek();
                    act.FrameRequestMs = oldestReq.StartMs;
                    act.FrameLatencyMs = Math.Max(0.0, commitTime - oldestReq.StartMs);

                    // All requests up to commit time are fulfilled by this commit
                    while (unfulfilledRequests.Count > 0 && unfulfilledRequests.Peek().StartMs <= commitTime)
                    {
                        var req = unfulfilledRequests.Dequeue();
                        req.FrameCommitMs = commitTime;
                        req.FrameLatencyMs = Math.Max(0.0, commitTime - req.StartMs);
                    }
                }
            }
        }
    }
}

public static class FrameMarkers
{
    public static bool IsFrameRequest(string name) =>
        name.Contains("Scheduling_QueueTick", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("QueueTick", StringComparison.OrdinalIgnoreCase);

    public static bool IsFrameCommit(string name) =>
        name.Contains("CommitMainDevice", StringComparison.OrdinalIgnoreCase) ||
        (name.Contains("Commit", StringComparison.OrdinalIgnoreCase) &&
         !name.Contains("Request", StringComparison.OrdinalIgnoreCase) &&
         !name.Contains("PrivateCommit", StringComparison.OrdinalIgnoreCase));

    public static bool IsSchedulerWait(string name) =>
        name.Contains("RenderThreadWaitForWork", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("WaitForCompositorClock", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SimulateWaitForVBlank", StringComparison.OrdinalIgnoreCase);

    public static bool IsLock(string name) =>
        name.Contains("CompositorLock", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Lock", StringComparison.OrdinalIgnoreCase);

    public static bool IsLockBegin(string name) =>
        name.Contains("CompositorLock", StringComparison.OrdinalIgnoreCase) &&
        (name.EndsWith("Begin", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith("Start", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith("/Begin", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith("/Start", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith("_Begin", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith("_Start", StringComparison.OrdinalIgnoreCase));

    public static bool IsLockEnd(string name) =>
        name.Contains("CompositorLock", StringComparison.OrdinalIgnoreCase) &&
        (name.EndsWith("End", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith("Stop", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith("/End", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith("/Stop", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith("_End", StringComparison.OrdinalIgnoreCase) ||
         name.EndsWith("_Stop", StringComparison.OrdinalIgnoreCase));
}
