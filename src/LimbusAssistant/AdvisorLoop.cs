using System.Diagnostics;
using Tsundosika.LimbusAssistant.Engine;
using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant;

public sealed class AdvisorLoop : IDisposable
{
    const int MetricsWindow = 32;
    const int LocateIntervalMilliseconds = 1000;
    const int HeartbeatMilliseconds = 250;

    const int HintGraceMilliseconds = 1500;

    readonly AppSettings _settings;
    readonly VisionPipeline _pipeline;
    readonly INumberReader _reader;
    readonly TurnSolver _solver = new();
    readonly GameData _data;
    readonly int _targetFrameIntervalMs;
    readonly IReadOnlyList<(string Normalized, SkillData Skill, string Identity)> _skillIndex;
    readonly IReadOnlyList<(string Normalized, SkillData Skill, EnemyData Owner)> _enemySkillIndex;
    const int CaptureGraceMilliseconds = 2000;
    const int AutoEnemyGraceMilliseconds = 15000;

    const int DockScanIntervalMilliseconds = 1000;

    volatile EnemyData? _liveEnemy;
    volatile IReadOnlyDictionary<string, int>? _teamSanities;
    EnemyData? _autoEnemy;
    long _autoEnemyTimestamp;
    long _lastCaptureTimestamp;
    long _lastDockScanTimestamp;
    IReadOnlyList<int> _cachedSanities = [];
    PlanningHint? _stickyPlanning;
    long _stickyPlanningTimestamp;
    int _nonPlanningStreak;
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
        _data = data;
        _targetFrameIntervalMs = Math.Clamp(settings.CaptureIntervalMilliseconds, 33, 1000);
        _targetTitle = string.IsNullOrWhiteSpace(settings.WindowTitle) ? null : settings.WindowTitle;
        _skillIndex = data.Identities
            .SelectMany(identity => identity.Skills.Select(skill => (Normalize(skill.Name), skill, identity.Name)))
            .ToList();
        _enemySkillIndex = data.Enemies
            .SelectMany(enemy => enemy.Skills.Select(skill => (Normalize(skill.Name), skill, enemy)))
            .ToList();
    }

    public void Start() => Task.Run(() => RunAsync(_cancellation.Token));

    public void SetTargetWindow(string? title)
    {
        _targetTitle = string.IsNullOrWhiteSpace(title) ? null : title;
        _window = null;
    }

    public void SetLiveEnemy(EnemyData? enemy) => _liveEnemy = enemy;

    public void SetTeamSanities(IReadOnlyDictionary<string, int> sanities) => _teamSanities = sanities;

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
            if (Environment.TickCount64 - _lastCaptureTimestamp < CaptureGraceMilliseconds && _lastPublished is not null)
            {
                Publish(_lastPublished with { GameBounds = window.ClientBounds, Timestamp = DateTimeOffset.Now });
                return;
            }
            Publish(BuildSnapshot(CaptureStatus.WaitingForFrame, window, false, false, CurrentMetrics(0)));
            return;
        }
        _lastCaptureTimestamp = Environment.TickCount64;
        var frame = FrameCropper.CropToClient(raw, window.Handle);
        var hash = FrameHash.SampleFrame(frame);
        if (hash == _lastFrameHash && _lastPublished is not null)
        {
            Publish(BuildSnapshot(CaptureStatus.Ok, window, _lastPublished.ClashGateOpen, _lastPublished.PlanningPhase, CurrentMetrics(0)));
            return;
        }
        _lastFrameHash = hash;
        var content = LetterboxDetector.DetectContent(frame);
        var now = Environment.TickCount64;
        if (PlanningIndicator.IsPlanningVisible(frame, content))
        {
            _nonPlanningStreak = 0;
        }
        else
        {
            _nonPlanningStreak++;
        }
        if (_nonPlanningStreak >= 3)
        {
            _stickyPlanning = null;
            _lastPlanning = null;
            _lastLiveClash = null;
            _lastFrame = frame;
            _lastReading = EmptyReadingFor(frame, content);
            Publish(BuildSnapshot(CaptureStatus.Ok, window, false, false, CurrentMetrics(_reader.ConsumeOcrCallCount())));
            return;
        }
        if (!ClashGate.IsClashLikely(frame, content))
        {
            if (now - _lastDockScanTimestamp >= DockScanIntervalMilliseconds)
            {
                _lastDockScanTimestamp = now;
                var dock = await _pipeline.ReadDockSanityAsync(frame, content);
                var values = dock
                    .Where(slot => slot.Reading.Value is >= -45 and <= 45 && slot.Reading.Confidence >= 0.4)
                    .Select(slot => slot.Reading.Value!.Value)
                    .ToList();
                if (values.Count > 0)
                {
                    _cachedSanities = values;
                }
            }
            var withinGrace = _stickyPlanning is not null && now - _stickyPlanningTimestamp < HintGraceMilliseconds;
            _lastPlanning = withinGrace ? _stickyPlanning : null;
            if (!withinGrace)
            {
                _stickyPlanning = null;
            }
            _lastLiveClash = null;
            _lastFrame = frame;
            _lastReading = EmptyReadingFor(frame, content);
            Publish(BuildSnapshot(CaptureStatus.Ok, window, withinGrace, true, CurrentMetrics(_reader.ConsumeOcrCallCount())));
            return;
        }
        _lastReading = await _pipeline.ReadAsync(frame, content);
        var fresh = await BuildPlanningHintAsync(frame, content, _lastReading);
        if (fresh?.Skill is not null)
        {
            _stickyPlanning = fresh;
            _stickyPlanningTimestamp = now;
        }
        else if (_stickyPlanning is not null)
        {
            _stickyPlanningTimestamp = now;
        }
        _lastPlanning = fresh?.Skill is not null ? fresh : _stickyPlanning ?? fresh;
        _lastLiveClash = null;
        _lastFrame = frame;
        Publish(BuildSnapshot(CaptureStatus.Ok, window, true, true, CurrentMetrics(_reader.ConsumeOcrCallCount())));
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

    AdvisorSnapshot BuildSnapshot(
        CaptureStatus status,
        GameWindow window,
        bool gateOpen,
        bool planningPhase,
        TickMetrics metrics) => new(
        status,
        window.ClientBounds,
        _lastReading,
        _lastLiveClash,
        _lastPlanning,
        gateOpen,
        planningPhase,
        _lastReading.OverallConfidence,
        _lastFrame,
        metrics,
        DateTimeOffset.Now);

    async Task<PlanningHint?> BuildPlanningHintAsync(CaptureFrame frame, PixelRect content, VisionReading reading)
    {
        UpdateAutoEnemy(reading);
        var name = reading.Text(RegionNames.DragSkillName);
        if (name.Confidence < 0.45 || name.Text.Length < 3)
        {
            return null;
        }
        var (skill, identity) = MatchSkillWithCandidates(name.Text, reading);
        if (skill is null && MatchEnemySkill(name.Text) is { } hoveredEnemySkill)
        {
            var owner = EffectiveEnemy();
            return new PlanningHint(
                name.Text,
                hoveredEnemySkill,
                owner?.Name,
                null,
                name.Confidence,
                owner?.Name,
                BestAnswers(owner, hoveredEnemySkill),
                true);
        }
        int? sanity;
        string? source;
        if (_stickyPlanning is { } sticky && sticky.Skill?.Id == skill?.Id && sticky.Sanity is not null)
        {
            sanity = sticky.Sanity;
            source = sticky.SanitySource;
        }
        else
        {
            (sanity, source) = await ResolveSanityAsync(frame, content, identity);
        }
        var exact = MatchExactEnemySkill(reading);
        if (skill is not null && identity is not null && exact is { } exactMatch)
        {
            var identityData = _data.Identities.FirstOrDefault(candidate => candidate.Name == identity);
            if (identityData is not null)
            {
                var unit = new TurnUnit(identityData, sanity ?? 0);
                var result = _solver.EvaluateClash(unit, skill, new EnemyThreat(exactMatch.Owner, exactMatch.Skill));
                var exactOdds = new MatchupOdds(
                    exactMatch.Skill.Name,
                    result.WinProbability,
                    result.ExpectedDamageDealt,
                    result.ExpectedDamageTaken);
                _autoEnemy = exactMatch.Owner;
                _autoEnemyTimestamp = Environment.TickCount64;
                var (_, others) = BuildMatchups(skill, identity, sanity);
                return new PlanningHint(
                    name.Text,
                    skill,
                    identity,
                    sanity,
                    name.Confidence,
                    exactMatch.Owner.Name,
                    others,
                    false,
                    source,
                    exactOdds,
                    exactMatch.Skill.Name);
            }
        }
        var (enemyName, matchups) = BuildMatchups(skill, identity, sanity);
        return new PlanningHint(name.Text, skill, identity, sanity, name.Confidence, enemyName, matchups, false, source);
    }

    (SkillData? Skill, string? Identity) MatchSkillWithCandidates(string primaryText, VisionReading reading)
    {
        var (skill, identity) = MatchSkill(primaryText);
        if (skill is not null)
        {
            return (skill, identity);
        }
        foreach (var (key, text) in reading.Texts)
        {
            if (!key.StartsWith("ribbon.", StringComparison.Ordinal) || text.Text.Length < 3)
            {
                continue;
            }
            var (candidateSkill, candidateIdentity) = MatchSkill(text.Text);
            if (candidateSkill is not null)
            {
                return (candidateSkill, candidateIdentity);
            }
        }
        return (null, null);
    }

    (SkillData Skill, EnemyData Owner)? MatchExactEnemySkill(VisionReading reading)
    {
        var text = reading.Text(RegionNames.EnemySkillName);
        if (text.Confidence < 0.45 || text.Text.Length < 3)
        {
            return null;
        }
        var normalized = Normalize(text.Text);
        if (normalized.Length < 3)
        {
            return null;
        }
        var live = EffectiveEnemy();
        if (live is not null && MatchWithin(normalized, live.Skills.Select(skill => (skill, live))) is { } liveMatch)
        {
            return liveMatch;
        }
        return MatchWithin(normalized, _enemySkillIndex.Select(entry => (entry.Skill, entry.Owner)));
    }

    static (SkillData Skill, EnemyData Owner)? MatchWithin(
        string normalized,
        IEnumerable<(SkillData Skill, EnemyData Owner)> candidates)
    {
        SkillData? bestSkill = null;
        EnemyData? bestOwner = null;
        var bestDistance = int.MaxValue;
        var cap = Math.Max(2, normalized.Length / 4);
        foreach (var (skill, owner) in candidates)
        {
            var candidate = Normalize(skill.Name);
            if (candidate.Length < 3)
            {
                continue;
            }
            var distance = EditDistance(normalized, candidate, cap);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSkill = skill;
                bestOwner = owner;
            }
            if (distance == 0)
            {
                break;
            }
        }
        return bestSkill is not null && bestOwner is not null && bestDistance <= cap
            ? (bestSkill, bestOwner)
            : null;
    }

    async Task<(int? Sanity, string? Source)> ResolveSanityAsync(CaptureFrame frame, PixelRect content, string? identityName)
    {
        var field = await _pipeline.ReadDraggerSanityAsync(frame, content);
        if (field?.Reading.Value is >= -45 and <= 45 && field.Value.Reading.Confidence >= 0.4)
        {
            return (field.Value.Reading.Value, "field");
        }
        if (identityName is not null
            && _teamSanities is { } sanities
            && sanities.TryGetValue(identityName, out var teamSanity))
        {
            return (teamSanity, "team");
        }
        var cached = CachedSanity();
        return (cached, cached is null ? null : "dock");
    }

    SkillData? MatchEnemySkill(string rawName)
    {
        var normalized = Normalize(rawName);
        if (normalized.Length < 3)
        {
            return null;
        }
        var live = EffectiveEnemy();
        if (live is not null && MatchWithin(normalized, live.Skills.Select(skill => (skill, live))) is { } liveMatch)
        {
            return liveMatch.Skill;
        }
        if (MatchWithin(normalized, _enemySkillIndex.Select(entry => (entry.Skill, entry.Owner))) is { } globalMatch)
        {
            _autoEnemy = globalMatch.Owner;
            _autoEnemyTimestamp = Environment.TickCount64;
            return globalMatch.Skill;
        }
        return null;
    }

    IReadOnlyList<MatchupOdds>? BestAnswers(EnemyData? enemy, SkillData enemySkill)
    {
        if (enemy is null || _teamSanities is not { Count: > 0 } sanities)
        {
            return null;
        }
        var threat = new EnemyThreat(enemy, enemySkill);
        var answers = new List<MatchupOdds>();
        foreach (var (identityName, sanity) in sanities)
        {
            var identity = _data.Identities.FirstOrDefault(candidate => candidate.Name == identityName);
            if (identity is null || identity.Skills.Count == 0)
            {
                continue;
            }
            var unit = new TurnUnit(identity, sanity);
            var best = identity.Skills
                .Select(skill => (Skill: skill, Result: _solver.EvaluateClash(unit, skill, threat)))
                .MaxBy(pair => pair.Result.ExpectedValue);
            answers.Add(new MatchupOdds(
                $"{identity.Sinner}: {best.Skill.Name}",
                best.Result.WinProbability,
                best.Result.ExpectedDamageDealt,
                best.Result.ExpectedDamageTaken));
        }
        return answers.Count == 0 ? null : answers.OrderByDescending(answer => answer.WinProbability).ToList();
    }

    int? CachedSanity()
    {
        if (_cachedSanities.Count == 0)
        {
            return null;
        }
        var sorted = _cachedSanities.OrderBy(value => value).ToList();
        return sorted[sorted.Count / 2];
    }

    void UpdateAutoEnemy(VisionReading reading)
    {
        var target = reading.Text(RegionNames.TargetUnitName);
        if (target.Confidence < 0.45 || target.Text.Length < 4)
        {
            return;
        }
        var normalized = Normalize(target.Text);
        if (normalized.Length < 4)
        {
            return;
        }
        EnemyData? bestEnemy = null;
        var bestDistance = int.MaxValue;
        foreach (var enemy in _data.Enemies)
        {
            var candidate = Normalize(enemy.Name);
            if (candidate.Length < 4)
            {
                continue;
            }
            var cap = Math.Max(2, candidate.Length / 4);
            var distance = normalized.Contains(candidate, StringComparison.Ordinal)
                ? 0
                : EditDistance(normalized, candidate, cap);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestEnemy = enemy;
            }
            if (distance == 0)
            {
                break;
            }
        }
        if (bestEnemy is not null && bestDistance <= Math.Max(2, Normalize(bestEnemy.Name).Length / 4))
        {
            _autoEnemy = bestEnemy;
            _autoEnemyTimestamp = Environment.TickCount64;
        }
    }

    EnemyData? EffectiveEnemy()
    {
        if (_autoEnemy is not null && Environment.TickCount64 - _autoEnemyTimestamp < AutoEnemyGraceMilliseconds)
        {
            return _autoEnemy;
        }
        return _liveEnemy;
    }

    (string? EnemyName, IReadOnlyList<MatchupOdds>? Matchups) BuildMatchups(SkillData? skill, string? identityName, int? sanity)
    {
        var enemy = EffectiveEnemy();
        if (skill is null || enemy is null || enemy.Skills.Count == 0)
        {
            return (enemy?.Name, null);
        }
        var identity = _data.Identities.FirstOrDefault(candidate => candidate.Name == identityName);
        if (identity is null)
        {
            return (enemy.Name, null);
        }
        var unit = new TurnUnit(identity, sanity ?? 0);
        var matchups = enemy.Skills
            .Take(6)
            .Select(enemySkill =>
            {
                var assignment = _solver.EvaluateClash(unit, skill, new EnemyThreat(enemy, enemySkill));
                return new MatchupOdds(
                    enemySkill.Name,
                    assignment.WinProbability,
                    assignment.ExpectedDamageDealt,
                    assignment.ExpectedDamageTaken);
            })
            .OrderByDescending(matchup => matchup.WinProbability)
            .ToList();
        return (enemy.Name, matchups);
    }

    static VisionReading EmptyReadingFor(CaptureFrame frame, PixelRect content) => new(
        new Dictionary<string, NumberReading>(),
        new Dictionary<string, IconReading>(),
        new Dictionary<string, TextReading>(),
        new Dictionary<string, PixelRect>(),
        frame.Width,
        frame.Height,
        content,
        DateTimeOffset.Now);

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
            || snapshot.PlanningPhase != _lastPublished.PlanningPhase
            || snapshot.ClashGateOpen != _lastPublished.ClashGateOpen
            || !Equals(snapshot.GameBounds, _lastPublished.GameBounds)
            || !Equals(snapshot.LiveClash, _lastPublished.LiveClash)
            || snapshot.Planning?.Skill?.Id != _lastPublished.Planning?.Skill?.Id
            || snapshot.Planning?.Sanity != _lastPublished.Planning?.Sanity
            || snapshot.Planning?.EnemyName != _lastPublished.Planning?.EnemyName
            || (snapshot.Planning?.Matchups?.Count ?? -1) != (_lastPublished.Planning?.Matchups?.Count ?? -1))
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
