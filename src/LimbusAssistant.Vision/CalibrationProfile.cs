namespace Tsundosika.LimbusAssistant.Vision;

public sealed record CalibrationProfile(string Name, IReadOnlyList<AnchorRegion> Regions)
{
    public static CalibrationProfile Default { get; } = new("default", new List<AnchorRegion>
    {
        new(RegionNames.DragSkillName, RegionKind.Text, new NormalizedRect(0.355, 0.155, 0.125, 0.055)),
        new(RegionNames.TargetUnitName, RegionKind.Text, new NormalizedRect(0.050, 0.014, 0.150, 0.042)),
    });
}
