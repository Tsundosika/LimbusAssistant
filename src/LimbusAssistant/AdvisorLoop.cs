using System.Diagnostics;
using Tsundosika.LimbusAssistant.Engine;
using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant;

public sealed class AdvisorLoop : IDisposable
{
    const int MetricsWindow = 32;
    const int LocateIntervalMilliseconds = 1000;
    const int HeartbeatMilliseconds = 250;

    readonly AppSettings _settings;
    readonly VisionPipeline _pipeline;
    readonly INumberReader _reader;
    readonly int _targetFrameIntervalMs;
    readonly IReadOnlyList<(string Normalized, SkillData Skill, string Identity)> _skillIndex;
    readonly CancellationTokenSource _cancellation = new();
    readonly double[] _tickDurations = new double[MetricsWindow];
    int _tickCount;
    IFrameSource? _source;
    GameWindow? _window;
    volatile string? _targetTitle;
    long _lastLocateTimestamp;
    ulong _lastFrameHash;
    AdvisorSnapshot? _lastPublished;
    long _lastPublishTimestamp;
    VisionReading _lastReading = VisionReading.Empty;
    PlanningHint? _lastPlanning;
    LiveClashEstimate? _lastLiveClash;
    CaptureFrame? _lastFrame;

    public event Action<AdvisorSnapshot>? SnapshotPublished;

    public AdvisorLoop(AppSettings settings, GameData data, VisionPipeline pipeline, INumberReader reader)
    {
        _settings = settings;
        _pipeline = pipeline;
        _reader = reader;
        _targetFrameIntervalMs = Math.Clamp(settings.CaptureIntervalMilliseconds, 33, 1000);
        _targetTitle = string.IsNullOrWhiteSpace(settings.WindowTitle) ? null : settings.WindowTitle;
        _skillIndex = data.Identities
            .SelectMany(identity => identity.Skills.Select(skill => (Normalize(skill.Name), skill, identity.Name)))
            .ToList();
    }

    public void Start() => Task.Run(() => RunAsync(_cancellation.Token));

    public void SetTargetWindow(string? title)
    {
        _targetTitle = string.IsNullOrWhiteSpace(title) ? null : title;
        _window = null;
    }

    async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                await TickAsync();
            }
            catch (Exception)
            {
                ReleaseSource();
            }
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            RecordTick(elapsed);
            var delay = Math.Max(0, _targetFrameIntervalMs - (int)elapsed);
            try
            {
                await Task.Delay(delay, token);
            }
            catch (TaskCanceledException)
            {
            }
        }
    }

    async Task TickAsync()
    {
        var window = ResolveWindow();
        if (window is null)
        {
            ReleaseSource();
            _window = null;
            Publish(AdvisorSnapshot.NotFound() with { Metrics = CurrentMetrics(0) });
            return;
        }
        if (_source is null || _window is null || _window.Handle != window.Handle)
        {
            ReleaseSource();
            _source = FrameSourceFactory.Create(window.Handle);
        }
        _window = window;
        var raw = _source.TryCapture();
        if (raw is null)
        {
            Publish(BuildSnapshot(CaptureStatus.WaitingForFrame, window, false, CurrentMetrics(0)));
            return;
        }
        var frame = FrameCropper.CropToClient(raw, window.Handle);
        var hash = FrameHash.SampleFrame(frame);
        if (hash == _lastFrameHash && _lastPublished is not null)
        {
            Publish(BuildSnapshot(CaptureStatus.Ok, window, _lastPublished.ClashGateOpen, CurrentMetrics(0)));
            return;
        }
        _lastFrameHash = hash;
        var content = LetterboxDetector.DetectContent(frame);
        if (!ClashGate.IsClashLikely(frame, content))
        {
            _lastPlanning = null;
            _lastLiveClash = null;
            _lastFrame = frame;
            _lastReading = VisionReading.Empty;
            Publish(BuildSnapshot(CaptureStatus.Ok, window, false, CurrentMetrics(_reader.ConsumeOcrCallCount())));
            return;
        }
        _lastReading = await _pipeline.ReadAsync(frame, content);
        _lastPlanning = BuildPlanningHint(_lastReading);
        _lastLiveClash = null;
        _lastFrame = frame;
        Publish(BuildSnapshot(CaptureStatus.Ok, window, true, CurrentMetrics(_reader.ConsumeOcrCallCount())));
    }

    GameWindow? ResolveWindow()
    {
        var now = Environment.TickCount64;
        if (_window is not null)
        {
            var refreshed = GameWindowLocator.FromHandle(_window.Handle);
            if (refreshed is not null)
            {
                return refreshed;
            }
        }
        if (_window is null || now - _lastLocateTimestamp >= LocateIntervalMilliseconds)
        {
            _lastLocateTimestamp = now;
            var target = _targetTitle;
            return target is null ? GameWindowLocator.FindAuto() : GameWindowLocator.FindByTitle(target);
        }
        return null;
    }

    AdvisorSnapshot BuildSnapshot(CaptureStatus status, GameWindow window, bool gateOpen, TickMetrics metrics) => new(
        status,
        window.ClientBounds,
        _lastReading,
        _lastLiveClash,
        _lastPlanning,
        gateOpen,
        _lastReading.OverallConfidence,
        _lastFrame,
        metrics,
        DateTimeOffset.Now);

    PlanningHint? BuildPlanningHint(VisionReading reading)
    {
        var name = reading.Text(RegionNames.DragSkillName);
        if (name.Confidence < 0.5 || name.Text.Length < 3)
        {
            return null;
        }
        var sanity = ReadBestSanity(reading);
        var (skill, identity) = MatchSkill(name.Text);
        return new PlanningHint(name.Text, skill, identity, sanity, name.Confidence);
    }

    int? ReadBestSanity(VisionReading reading)
    {
        var best = default(int?);
        var bestConfidence = 0.0;
        foreach (var region in new[] { RegionNames.SanitySlot1, RegionNames.SanitySlot2, RegionNames.SanitySlot3 })
        {
            var value = reading.Number(region);
            if (value.Value is >= -45 and <= 45 && value.Confidence > bestConfidence)
            {
                best = value.Value;
                bestConfidence = value.Confidence;
            }
        }
        return best;
    }

    (SkillData? Skill, string? Identity) MatchSkill(string rawName)
    {
        var normalized = Normalize(rawName);
        if (normalized.Length < 3)
        {
            return (null, null);
        }
        SkillData? bestSkill = null;
        string? bestIdentity = null;
        var bestDistance = int.MaxValue;
        foreach (var (candidate, skill, identity) in _skillIndex)
        {
            var distance = EditDistance(normalized, candidate, Math.Max(2, normalized.Length / 4));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSkill = skill;
                bestIdentity = identity;
            }
        }
        var limit = Math.Max(2, normalized.Length / 4);
        return bestDistance <= limit ? (bestSkill, bestIdentity) : (null, null);
    }

    static string Normalize(string text) =>
        new(text.ToLowerInvariant().Where(char.IsAsciiLetter).ToArray());

    static int EditDistance(string a, string b, int cap)
    {
        if (Math.Abs(a.Length - b.Length) > cap)
        {
            return cap + 1;
        }
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
                rowMin = Math.Min(rowMin, current[j]);
            }
            if (rowMin > cap)
            {
                return cap + 1;
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    void RecordTick(double milliseconds)
    {
        _tickDurations[_tickCount % MetricsWindow] = milliseconds;
        _tickCount++;
    }

    TickMetrics CurrentMetrics(int ocrCalls)
    {
        var samples = Math.Min(_tickCount, MetricsWindow);
        var average = samples == 0 ? 0 : _tickDurations.Take(samples).Average();
        var last = samples == 0 ? 0 : _tickDurations[(_tickCount - 1 + MetricsWindow) % MetricsWindow];
        return new TickMetrics(average, last, ocrCalls);
    }

    void Publish(AdvisorSnapshot snapshot)
    {
        var now = Environment.TickCount64;
        if (!ShouldPublish(snapshot, now))
        {
            return;
        }
        _lastPublished = snapshot;
        _lastPublishTimestamp = now;
        SnapshotPublished?.Invoke(snapshot);
    }

    bool ShouldPublish(AdvisorSnapshot snapshot, long now)
    {
        if (_lastPublished is null)
        {
            return true;
        }
        if (snapshot.Status != _lastPublished.Status
            || snapshot.ClashGateOpen != _lastPublished.ClashGateOpen
            || !Equals(snapshot.GameBounds, _lastPublished.GameBounds)
            || !Equals(snapshot.LiveClash, _lastPublished.LiveClash)
            || snapshot.Planning?.Skill?.Id != _lastPublished.Planning?.Skill?.Id
            || snapshot.Planning?.Sanity != _lastPublished.Planning?.Sanity)
        {
            return true;
        }
        return now - _lastPublishTimestamp >= HeartbeatMilliseconds;
    }

    void ReleaseSource()
    {
        _source?.Dispose();
        _source = null;
        _lastFrameHash = 0;
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        ReleaseSource();
    }
}
