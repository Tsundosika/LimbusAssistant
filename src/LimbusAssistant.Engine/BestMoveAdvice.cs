namespace Tsundosika.LimbusAssistant.Engine;

public sealed record BestMoveAdvice(
    string Sinner,
    string IdentityName,
    int SkillNumber,
    string SkillName,
    bool IsUnopposed,
    string? TargetEnemyName,
    string? TargetSkillName,
    double WinProbability,
    double ExpectedDamageDealt,
    double ExpectedDamageTaken);
