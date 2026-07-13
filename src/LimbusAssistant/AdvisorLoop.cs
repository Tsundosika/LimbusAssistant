using System.Diagnostics;
using System.IO;
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
    readonly IReadOnlyDictionary<string, IReadOnlyList<EnemyData>> _enemiesByName;
    const int CaptureGraceMilliseconds = 2000;

    const int DockScanIntervalMilliseconds = 1000;

    volatile EnemyData? _liveEnemy;
    volatile IReadOnlyList<(string Name, int Sanity)>? _team;
    readonly SanityTracker _sanity = new();
    readonly List<string> _observedIdentities = [];
    EnemyData? _autoEnemy;
    long _autoEnemyTimestamp;
    long _lastCaptureTimestamp;
    long _lastDockScanTimestamp;
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
    BestMoveReport? _bestMoves;
    string? _bestMovesSignature;

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
        _enemiesByName = data.Enemies
            .GroupBy(enemy => Normalize(enemy.Name))
            .Where(group => group.Key.Length >= 3)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<EnemyData>)group.ToList());
    }

    IReadOnlyList<EnemyData> SiblingsOf(EnemyData enemy)
    {
        var key = Normalize(enemy.Name);
        return key.Length >= 3 && _enemiesByName.TryGetValue(key, out var group) ? group : [enemy];
    }

    (SkillData Skill, EnemyData Owner)? MatchGlobalEnemySkillStrict(string normalized)
    {
        if (normalized.Length < 5)
        {
            return null;
        }
        SkillData? bestSkill = null;
        EnemyData? bestOwner = null;
        var bestDistance = 3;
        foreach (var (candidate, skill, owner) in _enemySkillIndex)
        {
            if (candidate.Length < 5 || Math.Abs(candidate.Length - normalized.Length) > 2)
            {
                continue;
            }
            var distance = EditDistance(normalized, candidate, 2);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSkill = skill;
                bestOwner = owner;
                if (distance == 0)
                {
                    break;
                }
            }
        }
        return bestSkill is not null && bestOwner is not null ? (bestSkill, bestOwner) : null;
    }

    public void Start() => Task.Run(() => RunAsync(_cancellation.Token));

    public void SetTargetWindow(string? title)
    {
        _targetTitle = string.IsNullOrWhiteSpace(title) ? null : title;
        _window = null;
    }

    public void SetLiveEnemy(EnemyData? enemy) => _liveEnemy = enemy;

    public void SetTeam(IReadOnlyList<(string Name, int Sanity)> members)
    {
        _team = members;
        SeedTeamSanity();
    }

    void SeedTeamSanity()
    {
        if (_team is not { Count: > 0 } team)
        {
            return;
        }
        var now = Environment.TickCount64;
        foreach (var member in team)
        {
            _sanity.Report(member.Name, member.Sanity, SanitySource.ManualTeam, now);
        }
    }

    public string? DumpDebugSnapshot()
    {
        var frame = _lastFrame;
        if (frame is null)
        {
            return null;
        }
        var directory = Path.Combine("probe-out", "appdump", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);
        DebugDump.SaveFrame(frame, Path.Combine(directory, "frame.png"));
        var report = new System.Text.StringBuilder();
        var published = _lastPublished;
        report.AppendLine($"status {published?.Status} gate {published?.ClashGateOpen} planning {published?.PlanningPhase}");
        var reading = _lastReading;
        foreach (var (key, text) in reading.Texts.OrderBy(pair => pair.Key))
        {
            var rect = reading.Regions.GetValueOrDefault(key);
            report.AppendLine($"{key,-24} rect {rect.X},{rect.Y} {rect.Width}x{rect.Height}  \"{text.Text}\" conf {text.Confidence:F2}");
        }
        var planning = _lastPlanning;
        if (planning is not null)
        {
            report.AppendLine($"hint raw \"{planning.RawSkillName}\" skill {planning.Skill?.Name ?? "none"} identity {planning.IdentityName ?? "none"}");
            report.AppendLine($"  enemy {planning.EnemyName ?? "none"} exact {planning.ExactEnemySkillName ?? "none"} isEnemySkill {planning.IsEnemySkill}");
            report.AppendLine($"  sanity {planning.Sanity?.ToString() ?? "?"} ({planning.SanitySource ?? "none"}) matchups {planning.Matchups?.Count ?? 0}");
        }
        else
        {
            report.AppendLine("hint none");
        }
        var sticky = _stickyPlanning;
        report.AppendLine($"lock {sticky?.Skill?.Name ?? "none"}");
        report.AppendLine($"team {(_team is { Count: > 0 } team ? string.Join(", ", team.Select(member => $"{member.Name}={member.Sanity}")) : "empty")}");
        var trackerEntries = _sanity.Snapshot();
        var trackerNow = Environment.TickCount64;
        report.AppendLine(trackerEntries.Count > 0
            ? $"sanity tracker: {string.Join(", ", trackerEntries.Select(pair => $"{pair.Key}={pair.Entry.Value} ({SanityTracker.Label(pair.Entry.Source)}, {(trackerNow - pair.Entry.Timestamp) / 1000}s)"))}"
            : "sanity tracker: empty");
        try
        {
            var content = LetterboxDetector.DetectContent(frame);
            using var mat = FrameMat.ToMat(frame);
            var highlight = HighlightScanner.FindHighlightedUnit(mat, content);
            report.AppendLine(highlight is { } h
                ? $"highlight {h.X},{h.Y} {h.Width}x{h.Height}"
                : "highlight none");
            var circles = DockScanner.FindSanityCircles(mat, content, DockScanner.FieldBand);
            report.AppendLine($"field circles {circles.Count}: {string.Join(" ", circles.Select(circle => $"{circle.X},{circle.Y}"))}");
            var dragger = _pipeline.ReadDraggerSanityAsync(frame, content).GetAwaiter().GetResult();
            report.AppendLine(dragger is { } d
                ? $"dragger sanity {d.Reading.Value?.ToString() ?? "?"} conf {d.Reading.Confidence:F2} at {d.Rect.X},{d.Rect.Y}"
                : "dragger sanity none");
            var dock = _pipeline.ReadDockSanityAsync(frame, content).GetAwaiter().GetResult();
            report.AppendLine($"dock circles {dock.Count}: {string.Join(" ", dock.Select(slot => $"{slot.Reading.Value?.ToString() ?? "?"}@{slot.Rect.X}"))}");
        }
        catch (Exception exception)
        {
            report.AppendLine($"diagnostics failed: {exception.Message}");
        }
        File.WriteAllText(Path.Combine(directory, "state.txt"), report.ToString());
        return directory;
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
            if (_nonPlanningStreak == ClearStreak)
            {
                _stickyPlanning = null;
                _autoEnemy = null;
                _sanity.Clear();
                SeedTeamSanity();
            }
            _lastPlanning = null;
            _bestMoves = null;
            _bestMovesSignature = null;
            _lastLiveClash = null;
            _lastFrame = frame;
            _lastReading = EmptyReadingFor(frame, content);
            Publish(BuildSnapshot(CaptureStatus.Ok, window, false, false, CurrentMetrics(_reader.ConsumeOcrCallCount())));
            return;
        }
        if (now - _lastDockScanTimestamp >= DockScanIntervalMilliseconds)
        {
            _lastDockScanTimestamp = now;
            var teamNames = _team is { Count: > 0 } team
                ? team.Select(member => member.Name).ToList()
                : null;
            await SanityFrameIngest.IngestDockAsync(_sanity, _pipeline, frame, content, teamNames, now);
            await SanityFrameIngest.IngestFieldAsync(_sanity, _pipeline, frame, content, now);
            await SanityFrameIngest.IngestActingAsync(_sanity, _pipeline, frame, content, _stickyPlanning?.IdentityName, now);
        }
        if (!ClashGate.IsClashLikely(frame, content))
        {
            RefreshStickyMatchups();
            UpdateBestMoves();
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
        UpdateBestMoves();
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
        DateTimeOffset.Now,
        _bestMoves);

    async Task<PlanningHint?> BuildPlanningHintAsync(CaptureFrame frame, PixelRect content, VisionReading reading)
    {
        UpdateAutoEnemy(reading);
        var name = reading.Text(RegionNames.DragSkillName);
        if (name.Confidence < 0.45 || name.Text.Length < 3 || BannerWords.IsNonSkillBanner(name.Text))
        {
            return null;
        }
        var (skill, identity) = MatchSkillWithCandidates(name.Text, reading);
        var enemySideText = reading.Text(RegionNames.EnemySkillName);
        if (skill is null && enemySideText.Text.Length >= 3 && !BannerWords.IsNonSkillBanner(enemySideText.Text))
        {
            var (rightSkill, rightIdentity) = MatchSkill(enemySideText.Text);
            if (rightSkill is not null)
            {
                skill = rightSkill;
                identity = rightIdentity;
                enemySideText = name;
            }
        }
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
        var now = Environment.TickCount64;
        await SanityFrameIngest.IngestActingAsync(_sanity, _pipeline, frame, content, identity, now);
        var entry = identity is null ? null : _sanity.Resolve(identity);
        var sanity = entry?.Value;
        var source = entry is null ? null : SanityTracker.Label(entry.Source);
        var sanityAge = entry is null ? (int?)null : (int)((now - entry.Timestamp) / 1000);
        var exact = MatchExactEnemySkill(enemySideText);
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
                    exactMatch.Skill.Name,
                    sanityAge);
            }
        }
        var (enemyName, matchups) = BuildMatchups(skill, identity, sanity);
        return new PlanningHint(
            name.Text,
            skill,
            identity,
            sanity,
            name.Confidence,
            enemyName,
            matchups,
            false,
            source,
            SanityAgeSeconds: sanityAge);
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

    (SkillData Skill, EnemyData Owner)? MatchExactEnemySkill(TextReading text)
    {
        if (text.Confidence < 0.45 || text.Text.Length < 3 || BannerWords.IsNonSkillBanner(text.Text))
        {
            return null;
        }
        var normalized = Normalize(text.Text);
        if (normalized.Length < 3)
        {
            return null;
        }
        var live = EffectiveEnemy();
        if (live is not null)
        {
            var siblings = SiblingsOf(live).SelectMany(sibling => sibling.Skills.Select(skill => (skill, sibling)));
            if (MatchWithin(normalized, siblings) is { } liveMatch)
            {
                return liveMatch;
            }
        }
        return MatchGlobalEnemySkillStrict(normalized);
    }

    static (SkillData Skill, EnemyData Owner)? MatchWithin(
        string normalized,
        IEnumerable<(SkillData Skill, EnemyData Owner)> candidates)
    {
        SkillData? bestSkill = null;
        EnemyData? bestOwner = null;
        var bestDistance = int.MaxValue;
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
        }
        return bestSkill is not null && bestOwner is not null && bestDistance <= cap
            ? (bestSkill, bestOwner)
            : null;
    }

    SkillData? MatchEnemySkill(string rawName)
    {
        var normalized = Normalize(rawName);
        if (normalized.Length < 3)
        {
            return null;
        }
        var live = EffectiveEnemy();
        if (live is not null)
        {
            var siblings = SiblingsOf(live).SelectMany(sibling => sibling.Skills.Select(skill => (skill, sibling)));
            if (MatchWithin(normalized, siblings) is { } liveMatch)
            {
                return liveMatch.Skill;
            }
        }
        var (identitySkill, _) = MatchSkill(rawName);
        if (identitySkill is not null)
        {
            return null;
        }
        if (MatchWithin(normalized, _enemySkillIndex.Select(entry => (entry.Skill, entry.Owner))) is { } globalMatch
            && EditDistance(normalized, Normalize(globalMatch.Skill.Name), 1) <= 1)
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
        if (_stickyPlanning is not { Skill: not null, IsEnemySkill: false } sticky)
        {
            return;
        }
        var now = Environment.TickCount64;
        var entry = sticky.IdentityName is null ? null : _sanity.Resolve(sticky.IdentityName);
        var liveSanity = entry?.Value;
        var age = entry is null ? sticky.SanityAgeSeconds : (int)((now - entry.Timestamp) / 1000);
        var sanityChanged = liveSanity is not null && liveSanity != sticky.Sanity;
        var matchupsMissing = sticky.Matchups is null && EffectiveEnemy() is not null;
        if (!sanityChanged && !matchupsMissing)
        {
            if (age != sticky.SanityAgeSeconds)
            {
                _stickyPlanning = sticky with { SanityAgeSeconds = age };
            }
            return;
        }
        var sanity = liveSanity ?? sticky.Sanity;
        var source = entry is null ? sticky.SanitySource : SanityTracker.Label(entry.Source);
        var (enemyName, matchups) = BuildMatchups(sticky.Skill, sticky.IdentityName, sanity);
        _stickyPlanning = sticky with
        {
            Sanity = sanity,
            SanitySource = source,
            SanityAgeSeconds = age,
            EnemyName = enemyName ?? sticky.EnemyName,
            Matchups = matchups ?? sticky.Matchups,
            ExactClash = RecomputeExactClash(sticky, sanity),
        };
    }

    MatchupOdds? RecomputeExactClash(PlanningHint sticky, int? sanity)
    {
        if (sticky.ExactClash is null || sticky.ExactEnemySkillName is null || sticky.Skill is null)
        {
            return sticky.ExactClash;
        }
        var identity = _data.Identities.FirstOrDefault(candidate => candidate.Name == sticky.IdentityName);
        var enemy = EffectiveEnemy();
        var enemySkill = enemy?.Skills.FirstOrDefault(skill => skill.Name == sticky.ExactEnemySkillName);
        if (identity is null || enemy is null || enemySkill is null)
        {
            return sticky.ExactClash;
        }
        var unit = new TurnUnit(identity, sanity ?? 0);
        var result = _solver.EvaluateClash(unit, sticky.Skill, new EnemyThreat(enemy, enemySkill));
        return new MatchupOdds(
            enemySkill.Name,
            result.WinProbability,
            result.ExpectedDamageDealt,
            result.ExpectedDamageTaken);
    }

    IReadOnlyList<string> RosterNames()
    {
        if (_team is { Count: > 0 } team)
        {
            return team.Select(member => member.Name).ToList();
        }
        return _observedIdentities;
    }

    int RosterSanity(string identityName) => _sanity.Resolve(identityName)?.Value ?? 0;

    void UpdateBestMoves()
    {
        var enemy = EffectiveEnemy();
        var roster = RosterNames();
        if (enemy is null || roster.Count == 0)
        {
            _bestMoves = null;
            _bestMovesSignature = null;
            return;
        }
        var units = new List<TurnUnit>();
        foreach (var name in roster)
        {
            var identity = _data.Identities.FirstOrDefault(candidate => candidate.Name == name);
            if (identity is not null)
            {
                units.Add(new TurnUnit(identity, RosterSanity(name)));
            }
        }
        if (units.Count == 0)
        {
            _bestMoves = null;
            _bestMovesSignature = null;
            return;
        }
        var signature = $"{enemy.Name}|{string.Join(",", units.Select(unit => $"{unit.Identity.Name}:{unit.Sanity}"))}";
        if (signature == _bestMovesSignature && _bestMoves is not null)
        {
            return;
        }
        _bestMovesSignature = signature;
        _bestMoves = BestMoveAdvisor.Advise(_solver, units, new[] { enemy });
    }

    void UpdateAutoEnemy(VisionReading reading)
    {
        TryLockEnemyFromName(reading.Text(RegionNames.TargetEnemyName));
        TryLockEnemyFromName(reading.Text(RegionNames.TargetUnitName));
    }

    void TryLockEnemyFromName(TextReading target)
    {
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
            if (_autoEnemy is not null && Normalize(_autoEnemy.Name) == Normalize(bestEnemy.Name))
            {
                _autoEnemyTimestamp = Environment.TickCount64;
                return;
            }
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
        }
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
            || snapshot.Planning?.RawSkillName != _lastPublished.Planning?.RawSkillName
            || snapshot.Planning?.Sanity != _lastPublished.Planning?.Sanity
            || snapshot.Planning?.SanitySource != _lastPublished.Planning?.SanitySource
            || snapshot.Planning?.EnemyName != _lastPublished.Planning?.EnemyName
            || (snapshot.Planning?.Matchups?.Count ?? -1) != (_lastPublished.Planning?.Matchups?.Count ?? -1)
            || !ReferenceEquals(snapshot.BestMoves, _lastPublished.BestMoves))
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
