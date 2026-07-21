using System.Collections.Concurrent;

namespace BratnavaFC.Application.Diagnostics;

public static class DependencyStatusMonitor
{
    private const int MaxWarningsPerDependency = 20;
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<DependencyWarning>> Warnings = new(StringComparer.OrdinalIgnoreCase);

    public static void RecordWarning(string dependency, string message, Exception? exception = null)
    {
        var queue = Warnings.GetOrAdd(dependency, _ => new ConcurrentQueue<DependencyWarning>());
        queue.Enqueue(new DependencyWarning(
            dependency,
            message,
            exception?.ToString(),
            DateTimeOffset.UtcNow));

        while (queue.Count > MaxWarningsPerDependency && queue.TryDequeue(out _))
        {
        }
    }

    public static IReadOnlyList<DependencyWarning> GetWarnings(string dependency)
    {
        return Warnings.TryGetValue(dependency, out var queue)
            ? queue.Reverse().ToArray()
            : [];
    }

    public static IReadOnlyList<DependencyWarning> GetAllWarnings()
    {
        return Warnings.Values
            .SelectMany(q => q)
            .OrderByDescending(w => w.OccurredAt)
            .ToArray();
    }
}

public sealed record DependencyWarning(
    string Dependency,
    string Message,
    string? Error,
    DateTimeOffset OccurredAt);
