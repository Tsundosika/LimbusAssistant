using System.Diagnostics;
using Tsundosika.LimbusAssistant.Engine;
using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant;

public sealed class AdvisorLoop : IDisposable
{
    const string SkillTemplatePrefix = "skill.";

    readonly AppSettings _settings;
    readonly VisionPipeline _pipeline;
    readonly ClashCalculator _calculator = new();
    readonly Dictionary<string, SkillData> _skillsById;
    readonly CancellationTokenSource _cancellation = new();
    readonly int _targetFrameIntervalMs;
    IFrameSource? _source;
    GameWindow? _window;
    volatile string? _targetTitle;
    VisionReading _lastReading = VisionReading.Empty;
    LiveClashEstimate? _lastLiveClash;
    CaptureFrame? _lastFrame;

    public event Action<AdvisorSnapshot>? SnapshotPublished;

    public AdvisorLoop(AppSettings settings, GameData data, VisionPipeline pipeline)
    {
        _settings = settings;
        _pipeline = pipeline;
        _targetFrameIntervalMs = Math.Clamp(settings.CaptureIntervalMilliseconds, 1, 16);
        _targetTitle = string.IsNullOrWhiteSpace(settings.WindowTitle) ? null : settings.WindowTitle;
        _skillsById = data.Identities.SelectMany(identity => identity.Skills)
            .Concat(data.Enemies.SelectMany(enemy => enemy.Skills))
            .GroupBy(skill => skill.Id)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public void Start() => Task.Run(() => RunAsync(_cancellation.Token));

    public void SetTargetWindow(string? title) =>
        _targetTitle = string.IsNullOrWhiteSpace(title) ? null : title;

    async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                await TickAsync();
            }
            catch (Exception)
            {
                ReleaseSource();
            }
            try
            {
                var elapsedMs = (int)Math.Round(
                    (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency);
                var delayMs = Math.Max(0, _targetFrameIntervalMs - elapsedMs);
                await Task.Delay(delayMs, token);
            }
            catch (TaskCanceledException)
            {
            }
        }
    }

    async Task TickAsync()
    {
        var target = _targetTitle;
        var window = target is null ? GameWindowLocator.FindAuto() : GameWindowLocator.FindByTitle(target);
        if (window is null)
        {
            ReleaseSource();
            _window = null;
            Publish(AdvisorSnapshot.NotFound());
            return;
        }
        if (_source is null || _window is null || _window.Handle != window.Handle)
        {
            ReleaseSource();
            _source = FrameSourceFactory.Create(window.Handle);
        }
        _window = window;
        var frame = _source.TryCapture();
        if (frame is null)
        {
            Publish(new AdvisorSnapshot(
                CaptureStatus.WaitingForFrame,
                window.ClientBounds,
                _lastReading,
                _lastLiveClash,
                _lastReading.OverallConfidence,
                _lastFrame,
                DateTimeOffset.Now));
            return;
        }
        _lastReading = await _pipeline.ReadAsync(frame);
        _lastLiveClash = EstimateLiveClash(_lastReading);
        _lastFrame = frame;
        Publish(new AdvisorSnapshot(
            CaptureStatus.Ok,
            window.ClientBounds,
            _lastReading,
            _lastLiveClash,
            _lastReading.OverallConfidence,
            _lastFrame,
            DateTimeOffset.Now));
    }

    LiveClashEstimate? EstimateLiveClash(VisionReading reading)
    {
        var sanity = reading.Number(RegionNames.AllySanity).Value ?? 0;
        var ally = BuildSkill(
            reading,
            RegionNames.AllyClashPower,
            RegionNames.AllyClashCoins,
            RegionNames.AllySkillIcon,
            sanity);
        var enemy = BuildSkill(
            reading,
            RegionNames.EnemyClashPower,
            RegionNames.EnemyClashCoins,
            RegionNames.EnemySkillIcon,
            0);
        if (ally is null || enemy is null)
        {
            return null;
        }
        var outcome = _calculator.Calculate(ally.Value.Skill, enemy.Value.Skill);
        var attackPower = ExpectedAttackPower.OnClashWin(ally.Value.Skill, outcome.WinStates);
        return new LiveClashEstimate(
            outcome.EffectiveWinProbability,
            ClashCalculator.FirstExchangeWinProbability(ally.Value.Skill, enemy.Value.Skill),
            attackPower,
            ally.Value.FromDataset && enemy.Value.FromDataset,
            Math.Min(ally.Value.Confidence, enemy.Value.Confidence));
    }

    (ClashSkill Skill, bool FromDataset, double Confidence)? BuildSkill(
        VisionReading reading,
        string powerRegion,
        string coinsRegion,
        string iconRegion,
        int sanity)
    {
        var icon = reading.Icon(iconRegion);
        if (icon.Name is not null
            && icon.Name.StartsWith(SkillTemplatePrefix, StringComparison.Ordinal)
            && _skillsById.TryGetValue(icon.Name[SkillTemplatePrefix.Length..], out var skill))
        {
            var coins = reading.Number(coinsRegion).Value ?? skill.CoinCount;
            return (new ClashSkill(skill.BasePower, skill.CoinPower, coins, sanity), true, icon.Confidence);
        }
        var power = reading.Number(powerRegion);
        var coinsReading = reading.Number(coinsRegion);
        if (power.Value is null || coinsReading.Value is null)
        {
            return null;
        }
        var confidence = Math.Min(power.Confidence, coinsReading.Confidence);
        var minimumConfidence = Math.Min(_settings.MinimumConfidence, 0.35);
        if (confidence < minimumConfidence)
        {
            return null;
        }
        return (new ClashSkill(power.Value.Value, 0, coinsReading.Value.Value, sanity), false, confidence);
    }

    void Publish(AdvisorSnapshot snapshot) => SnapshotPublished?.Invoke(snapshot);

    void ReleaseSource()
    {
        _source?.Dispose();
        _source = null;
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        ReleaseSource();
    }
}
