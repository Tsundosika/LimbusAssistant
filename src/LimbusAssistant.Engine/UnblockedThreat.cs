namespace Tsundosika.LimbusAssistant.Engine;

public sealed record UnblockedThreat(
    string EnemyName,
    string SkillName,
    double ExpectedDamage,
    string? SuggestedGuarder = null,
    string? GuardSkillName = null);
