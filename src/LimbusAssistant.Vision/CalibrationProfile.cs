namespace Tsundosika.LimbusAssistant.Vision;

public sealed record CalibrationProfile(string Name, IReadOnlyList<AnchorRegion> Regions)
{
    public static CalibrationProfile Default { get; } = new("default", new List<AnchorRegion>
    {
        new(RegionNames.AllyClashPower, RegionKind.Number, new NormalizedRect(0.355, 0.36, 0.06, 0.065)),
        new(RegionNames.EnemyClashPower, RegionKind.Number, new NormalizedRect(0.585, 0.36, 0.06, 0.065)),
        new(RegionNames.AllyClashCoins, RegionKind.Number, new NormalizedRect(0.355, 0.435, 0.06, 0.04)),
        new(RegionNames.EnemyClashCoins, RegionKind.Number, new NormalizedRect(0.585, 0.435, 0.06, 0.04)),
        new(RegionNames.AllySanity, RegionKind.Number, new NormalizedRect(0.295, 0.43, 0.045, 0.05)),
        new(RegionNames.AllySinIcon, RegionKind.Icon, new NormalizedRect(0.325, 0.34, 0.035, 0.06)),
        new(RegionNames.EnemySinIcon, RegionKind.Icon, new NormalizedRect(0.645, 0.34, 0.035, 0.06)),
        new(RegionNames.AllySkillIcon, RegionKind.Icon, new NormalizedRect(0.25, 0.30, 0.08, 0.14)),
        new(RegionNames.EnemySkillIcon, RegionKind.Icon, new NormalizedRect(0.67, 0.30, 0.08, 0.14)),
        new(RegionNames.InGameWinRate, RegionKind.Number, new NormalizedRect(0.465, 0.395, 0.07, 0.05)),
    });
}
