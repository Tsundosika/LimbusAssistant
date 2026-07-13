namespace Tsundosika.LimbusAssistant.Vision;

public sealed class SanityTracker
{
    public const long StaleMilliseconds = 10000;
    public const long MaxResolveAgeMilliseconds = 90000;

    readonly object _gate = new();
    readonly Dictionary<string, SanityEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public void Report(string key, int value, SanitySource source, long timestamp)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing)
                && source < existing.Source
                && timestamp - existing.Timestamp <= StaleMilliseconds)
            {
                return;
            }
            _entries[key] = new SanityEntry(value, source, timestamp);
        }
    }

    public SanityEntry? Resolve(string key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                return null;
            }
            if (entry.Source != SanitySource.ManualTeam
                && Environment.TickCount64 - entry.Timestamp > MaxResolveAgeMilliseconds)
            {
                return null;
            }
            return entry;
        }
    }

    public IReadOnlyList<(string Key, SanityEntry Entry)> Snapshot()
    {
        lock (_gate)
        {
            return _entries
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => (pair.Key, pair.Value))
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    public static string Label(SanitySource source) => source switch
    {
        SanitySource.ManualTeam => "team",
        SanitySource.DockSlot => "dock slot",
        SanitySource.DockRank => "dock rank",
        _ => "field",
    };
}
