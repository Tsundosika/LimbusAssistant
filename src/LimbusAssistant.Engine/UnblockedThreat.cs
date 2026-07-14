namespace Tsundosika.LimbusAssistant.Engine;

public sealed record UnblockedThreat(
    string EnemyName,
    string SkillName,
    double ExpectedDamage,
    string? SuggestedGuarder = null,
    int GuardSkillNumber = 0,
    string? GuardSkillName = null);
