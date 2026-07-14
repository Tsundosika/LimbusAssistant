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
    double ExpectedDamageTaken,
    SinType Sin = SinType.Wrath,
    DamageType DamageType = DamageType.Slash,
    int CoinCount = 1,
    double SinMultiplier = 1.0,
    double PhysicalMultiplier = 1.0,
    AlternativeMove? Alternative = null);
