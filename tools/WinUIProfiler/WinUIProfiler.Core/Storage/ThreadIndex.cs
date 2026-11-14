// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Data.Sqlite;
using WinUIProfiler.Engine.Events;

namespace WinUIProfiler.Engine.Storage;

// Tracks process threads and their roles (Main UI Thread, Render Thread, Worker Thread).
// Persisted in the Threads table.
internal sealed class ThreadIndex : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly SqliteCommand upsertThread;
    private readonly Dictionary<int, (string Role, string Name)> threads = [];
    private bool hasMainThread;

    internal ThreadIndex(SqliteConnection connection)
    {
        this.connection = connection;
        upsertThread = Sql.Prepare(connection, """
            INSERT INTO Threads (ThreadId, Role, Name) VALUES ($id, $role, $name)
            ON CONFLICT(ThreadId) DO UPDATE SET Role = excluded.Role, Name = excluded.Name
            """, ("$id", SqliteType.Integer), ("$role", SqliteType.Text), ("$name", SqliteType.Text));
    }

    internal void Add(ProfilerEvent profilerEvent, SqliteTransaction transaction)
    {
        upsertThread.Transaction = transaction;
        int threadId = profilerEvent.ThreadId;
        string eventName = profilerEvent.Name;

        bool isMain = eventName == "CoreServices_Frame";
        bool isRender = eventName.Contains("RenderThreadWaitForWork", StringComparison.OrdinalIgnoreCase) ||
                        eventName.Contains("Scheduling_UIThreadRequest", StringComparison.OrdinalIgnoreCase) ||
                        eventName.Contains("CompositorLock", StringComparison.OrdinalIgnoreCase) ||
                        eventName.Contains("WaitForCompositorClock", StringComparison.OrdinalIgnoreCase);

        if (!threads.TryGetValue(threadId, out var existing))
        {
            string role = isMain ? "Main" : (isRender ? "Render" : "Worker");
            string name = isMain ? (!hasMainThread ? "Main UI Thread" : $"UI Thread ({threadId})")
                                 : (isRender ? "Compositor Scheduler" : $"Worker Thread ({threadId})");
            if (isMain) hasMainThread = true;

            threads[threadId] = (role, name);
            SaveThread(threadId, role, name);
        }
        else if (existing.Role == "Worker")
        {
            if (isMain)
            {
                string role = "Main";
                string name = !hasMainThread ? "Main UI Thread" : $"UI Thread ({threadId})";
                hasMainThread = true;
                threads[threadId] = (role, name);
                SaveThread(threadId, role, name);
            }
            else if (isRender)
            {
                string role = "Render";
                string name = "Compositor Scheduler";
                threads[threadId] = (role, name);
                SaveThread(threadId, role, name);
            }
        }
    }

    private void SaveThread(int threadId, string role, string name)
    {
        upsertThread.Parameters["$id"].Value = threadId;
        upsertThread.Parameters["$role"].Value = role;
        upsertThread.Parameters["$name"].Value = name;
        upsertThread.ExecuteNonQuery();
    }

    public void Dispose()
    {
        upsertThread.Dispose();
    }
}
