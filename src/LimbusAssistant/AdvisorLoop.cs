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
    IFrameSource? _source;
    GameWindow? _window;
    long _lastFrameHash;
    VisionReading _lastReading = VisionReading.Empty;
    LiveClashEstimate? _lastLiveClash;
    CaptureFrame? _lastFrame;

    public event Action<AdvisorSnapshot>? SnapshotPublished;

    public AdvisorLoop(AppSettings settings, GameData data, VisionPipeline pipeline)
    {
        _settings = settings;
        _pipeline = pipeline;
        _skillsById = data.Identities.SelectMany(identity => identity.Skills)
            .Concat(data.Enemies.SelectMany(enemy => enemy.Skills))
            .GroupBy(skill => skill.Id)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public void Start() => Task.Run(() => RunAsync(_cancellation.Token));

    async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
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
                await Task.Delay(_settings.CaptureIntervalMilliseconds, token);
            }
            catch (TaskCanceledException)
            {
            }
        }
    }

    async Task TickAsync()
    {
        var window = GameWindowLocator.Find(_settings.WindowTitle);
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
        var hash = SampleHash(frame);
        if (hash != _lastFrameHash)
        {
            _lastFrameHash = hash;
            _lastReading = await _pipeline.ReadAsync(frame);
            _lastLiveClash = EstimateLiveClash(_lastReading);
            _lastFrame = frame;
        }
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
        if (confidence < _settings.MinimumConfidence)
        {
            return null;
        }
        return (new ClashSkill(power.Value.Value, 0, coinsReading.Value.Value, sanity), false, confidence);
    }

    static long SampleHash(CaptureFrame frame)
    {
        var hash = 17L;
        for (var i = 0; i < frame.PixelsBgra.Length; i += 4093)
        {
            hash = hash * 31 + frame.PixelsBgra[i];
        }
        return hash;
    }

    void Publish(AdvisorSnapshot snapshot) => SnapshotPublished?.Invoke(snapshot);

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
