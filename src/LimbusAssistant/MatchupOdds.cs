namespace Tsundosika.LimbusAssistant;

public sealed record MatchupOdds(
    string EnemySkillName,
    double WinProbability,
    double ExpectedDamageDealt,
    double ExpectedDamageTaken);
