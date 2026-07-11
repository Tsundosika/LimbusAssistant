namespace Tsundosika.LimbusAssistant.Engine;

public sealed record ClashSuggestion(
    ClashCandidate Ally,
    EnemyThreat Threat,
    double WinProbability,
    double ExpectedDamage,
    double Score);
