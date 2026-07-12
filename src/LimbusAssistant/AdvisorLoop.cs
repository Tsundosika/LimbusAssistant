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
    readonly TurnSolver _solver = new();
    readonly GameData _data;
    readonly int _targetFrameIntervalMs;
    readonly IReadOnlyList<(string Normalized, SkillData Skill, string Identity)> _skillIndex;
    readonly IReadOnlyList<(string Normalized, SkillData Skill, EnemyData Owner)> _enemySkillIndex;
    const int CaptureGraceMilliseconds = 2000;

    const int DockScanIntervalMilliseconds = 1000;

    volatile EnemyData? _liveEnemy;
    volatile IReadOnlyList<(string Name, int Sanity)>? _team;
    readonly Dictionary<string, int> _dockSanities = new();
    readonly List<string> _observedIdentities = [];
    EnemyData? _autoEnemy;
    long _autoEnemyTimestamp;
    long _lastCaptureTimestamp;
    long _lastDockScanTimestamp;
    IReadOnlyList<int> _cachedSanities = [];
    const int RibbonEvidenceMilliseconds = 2500;
    const int HideStreak = 3;
    const int ClearStreak = 10;

    PlanningHint? _stickyPlanning;
    long _stickyPlanningTimestamp;
    long _lastRibbonEvidenceTimestamp;
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

    public void SetTeam(IReadOnlyList<(string Name, int Sanity)> members) => _team = members;

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
        var ribbonEvidence = now - _lastRibbonEvidenceTimestamp < RibbonEvidenceMilliseconds;
        if (PlanningIndicator.IsPlanningVisible(frame, content) || ribbonEvidence)
        {
            _nonPlanningStreak = 0;
        }
        else
        {
            _nonPlanningStreak++;
        }
        if (_nonPlanningStreak >= HideStreak)
        {
            if (_nonPlanningStreak >= ClearStreak)
            {
                _stickyPlanning = null;
                _autoEnemy = null;
            }
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
                var slots = dock
                    .OrderBy(slot => slot.Rect.X)
                    .Select(slot => slot.Reading.Value is >= -45 and <= 45 && slot.Reading.Confidence >= 0.4
                        ? slot.Reading.Value
                        : null)
                    .ToList();
                var values = slots.Where(value => value is not null).Select(value => value!.Value).ToList();
                if (values.Count > 0)
                {
                    _cachedSanities = values;
                }
                var team = _team;
                if (team is { Count: > 0 })
                {
                    for (var i = 0; i < Math.Min(team.Count, slots.Count); i++)
                    {
                        if (slots[i] is { } slotValue)
                        {
                            _dockSanities[team[i].Name] = slotValue;
                        }
                    }
                }
            }
            RefreshStickyMatchups();
            _lastPlanning = _stickyPlanning;
            _lastLiveClash = null;
            _lastFrame = frame;
            _lastReading = EmptyReadingFor(frame, content);
            Publish(BuildSnapshot(CaptureStatus.Ok, window, _stickyPlanning is not null, true, CurrentMetrics(_reader.ConsumeOcrCallCount())));
            return;
        }
        _lastReading = await _pipeline.ReadAsync(frame, content);
        if (_lastReading.Text(RegionNames.DragSkillName).Text.Count(char.IsLetter) >= 3)
        {
            _lastRibbonEvidenceTimestamp = now;
        }
        var fresh = await BuildPlanningHintAsync(frame, content, _lastReading);
        if (fresh?.Skill is not null)
        {
            _stickyPlanning = fresh;
            _stickyPlanningTimestamp = now;
            if (fresh is { IsEnemySkill: false, IdentityName: { } observedIdentity }
                && !_observedIdentities.Contains(observedIdentity))
            {
                _observedIdentities.Add(observedIdentity);
            }
        }
        RefreshStickyMatchups();
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
        if (name.Confidence < 0.45 || name.Text.Length < 3 || BannerWords.IsNonSkillBanner(name.Text))
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
        var bestSubsequence = 0.0;
        SkillData? bestSubsequenceSkill = null;
        EnemyData? bestSubsequenceOwner = null;
        var cap = Math.Max(2, normalized.Length / 3);
        foreach (var (skill, owner) in candidates)
        {
            var candidate = Normalize(skill.Name);
            if (candidate.Length < 3)
            {
                continue;
            }
            var distance = candidate.Length >= 5
                && (normalized.Contains(candidate, StringComparison.Ordinal)
                    || candidate.Contains(normalized, StringComparison.Ordinal) && normalized.Length >= 5)
                ? 0
                : EditDistance(normalized, candidate, cap);
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
            var subsequence = SubsequenceScore(normalized, candidate);
            if (subsequence > bestSubsequence)
            {
                bestSubsequence = subsequence;
                bestSubsequenceSkill = skill;
                bestSubsequenceOwner = owner;
            }
        }
        if (bestSkill is not null && bestOwner is not null && bestDistance <= cap)
        {
            return (bestSkill, bestOwner);
        }
        if (bestSubsequenceSkill is not null && bestSubsequenceOwner is not null && bestSubsequence >= 0.62)
        {
            return (bestSubsequenceSkill, bestSubsequenceOwner);
        }
        return null;
    }

    async Task<(int? Sanity, string? Source)> ResolveSanityAsync(CaptureFrame frame, PixelRect content, string? identityName)
    {
        if (identityName is not null && _dockSanities.TryGetValue(identityName, out var dockSanity))
        {
            return (dockSanity, "dock slot");
        }
        if (identityName is not null
            && _team is { } team
            && team.FirstOrDefault(member => member.Name == identityName) is { Name: not null } manual)
        {
            return (manual.Sanity, "team");
        }
        var field = await _pipeline.ReadDraggerSanityAsync(frame, content);
        if (field?.Reading.Value is >= -45 and <= 45 && field.Value.Reading.Confidence >= 0.4)
        {
            return (field.Value.Reading.Value, "field");
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
        if (enemy is null)
        {
            return null;
        }
        var roster = RosterNames();
        if (roster.Count == 0)
        {
            return null;
        }
        var threat = new EnemyThreat(enemy, enemySkill);
        var answers = new List<MatchupOdds>();
        foreach (var identityName in roster)
        {
            var identity = _data.Identities.FirstOrDefault(candidate => candidate.Name == identityName);
            if (identity is null || identity.Skills.Count == 0)
            {
                continue;
            }
            var unit = new TurnUnit(identity, RosterSanity(identityName));
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

    void RefreshStickyMatchups()
    {
        if (_stickyPlanning is not { Skill: not null, IsEnemySkill: false, Matchups: null } sticky)
        {
            return;
        }
        if (EffectiveEnemy() is null)
        {
            return;
        }
        var (enemyName, matchups) = BuildMatchups(sticky.Skill, sticky.IdentityName, sticky.Sanity);
        if (matchups is not null)
        {
            _stickyPlanning = sticky with { EnemyName = enemyName, Matchups = matchups };
        }
    }

    IReadOnlyList<string> RosterNames()
    {
        if (_team is { Count: > 0 } team)
        {
            return team.Select(member => member.Name).ToList();
        }
        return _observedIdentities;
    }

    int RosterSanity(string identityName)
    {
        if (_dockSanities.TryGetValue(identityName, out var dockSanity))
        {
            return dockSanity;
        }
        if (_team is { } team && team.FirstOrDefault(member => member.Name == identityName) is { Name: not null } manual)
        {
            return manual.Sanity;
        }
        return CachedSanity() ?? 0;
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

    EnemyData? EffectiveEnemy() => _autoEnemy ?? _liveEnemy;

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
        var limit = Math.Max(2, normalized.Length / 3);
        SkillData? bestSkill = null;
        string? bestIdentity = null;
        var bestDistance = int.MaxValue;
        var bestSubsequence = 0.0;
        SkillData? bestSubsequenceSkill = null;
        string? bestSubsequenceIdentity = null;
        foreach (var (candidate, skill, identity) in _skillIndex)
        {
            var distance = candidate.Length >= 5
                && (normalized.Contains(candidate, StringComparison.Ordinal)
                    || candidate.Contains(normalized, StringComparison.Ordinal) && normalized.Length >= 5)
                ? 0
                : EditDistance(normalized, candidate, limit);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSkill = skill;
                bestIdentity = identity;
            }
            if (distance == 0)
            {
                break;
            }
            var subsequence = SubsequenceScore(normalized, candidate);
            if (subsequence > bestSubsequence)
            {
                bestSubsequence = subsequence;
                bestSubsequenceSkill = skill;
                bestSubsequenceIdentity = identity;
            }
        }
        if (bestDistance <= limit)
        {
            return (bestSkill, bestIdentity);
        }
        if (bestSubsequence >= 0.62)
        {
            return (bestSubsequenceSkill, bestSubsequenceIdentity);
        }
        return (null, null);
    }

    static double SubsequenceScore(string read, string candidate)
    {
        if (candidate.Length < 4)
        {
            return 0;
        }
        var matched = 0;
        var index = 0;
        foreach (var character in read)
        {
            var found = candidate.IndexOf(character, index);
            if (found >= 0)
            {
                matched++;
                index = found + 1;
            }
        }
        if (matched < Math.Max(4, candidate.Length / 2))
        {
            return 0;
        }
        var coverage = matched / (double)candidate.Length;
        var precision = matched / (double)Math.Max(1, read.Length);
        return Math.Min(coverage, 1.0) * 0.7 + Math.Min(precision, 1.0) * 0.3;
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
            || snapshot.Planning?.RawSkillName != _lastPublished.Planning?.RawSkillName
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
