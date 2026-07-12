namespace Tsundosika.LimbusAssistant.Vision;

public sealed record CalibrationProfile(string Name, IReadOnlyList<AnchorRegion> Regions)
{
    public static CalibrationProfile Default { get; } = new("default", new List<AnchorRegion>
    {
        new(RegionNames.DragSkillName, RegionKind.Text, new NormalizedRect(0.355, 0.155, 0.125, 0.055)),
        new(RegionNames.SanitySlot1, RegionKind.Number, new NormalizedRect(0.315, 0.947, 0.032, 0.055)),
        new(RegionNames.SanitySlot2, RegionKind.Number, new NormalizedRect(0.442, 0.947, 0.032, 0.055)),
        new(RegionNames.SanitySlot3, RegionKind.Number, new NormalizedRect(0.569, 0.947, 0.032, 0.055)),
        new(RegionNames.AllyClashPower, RegionKind.Number, new NormalizedRect(0.355, 0.36, 0.06, 0.065)),
        new(RegionNames.EnemyClashPower, RegionKind.Number, new NormalizedRect(0.585, 0.36, 0.06, 0.065)),
    });
}
