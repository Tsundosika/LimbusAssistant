namespace Tsundosika.LimbusAssistant.Engine;

public sealed record IdentityData(
    string Id,
    string Name,
    string Sinner,
    int DefenseLevel,
    IReadOnlyList<SkillData> Skills);
