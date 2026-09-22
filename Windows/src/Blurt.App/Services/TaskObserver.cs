using Blurt.Engine.Diagnostics;

namespace Blurt.App.Services;

/// <summary>Logs the fault of a task nobody awaits, so a failure surfaces at once rather than at finalization.</summary>
public static class TaskObserver
{
    /// <summary>Logs <paramref name="task"/>'s exception under <paramref name="category"/> if it faults; returns immediately.</summary>
    public static void Observe(Task task, string category, string operation) =>
        task.ContinueWith(
            faulted => BlurtLog.Error(category, $"{operation} failed: {faulted.Exception?.GetBaseException()}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
