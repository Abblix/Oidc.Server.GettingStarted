using System.Collections.Concurrent;

namespace McpServerApp;

/// <summary>
/// The company's task tracker, in memory: each user has a list of their own, started with the same seed tasks the
/// first time that user is seen, and gone when the server stops.
/// </summary>
public sealed class TaskTracker(IReadOnlyList<string> seedTitles)
{
    private readonly ConcurrentDictionary<string, List<TrackedTask>> _tasks = new(StringComparer.Ordinal);

    public IReadOnlyList<TrackedTask> ListFor(string subject)
    {
        var tasks = TasksOf(subject);
        lock (tasks)
            return tasks.ToArray();
    }

    public TrackedTask Create(string subject, string title)
    {
        var tasks = TasksOf(subject);
        lock (tasks)
        {
            var task = new TrackedTask(tasks.Count + 1, title, Closed: false);
            tasks.Add(task);
            return task;
        }
    }

    /// <summary>
    /// Closes the task <paramref name="id"/> of <paramref name="subject"/>, or returns null when that user has no such
    /// task: another user's task is not found, rather than refused, so its existence is not disclosed.
    /// </summary>
    public TrackedTask? Close(string subject, int id)
    {
        var tasks = TasksOf(subject);
        lock (tasks)
        {
            var index = tasks.FindIndex(task => task.Id == id);
            if (index < 0)
                return null;

            return tasks[index] = tasks[index] with { Closed = true };
        }
    }

    private List<TrackedTask> TasksOf(string subject)
        => _tasks.GetOrAdd(subject, _ => seedTitles.Select((title, i) => new TrackedTask(i + 1, title, false)).ToList());
}
