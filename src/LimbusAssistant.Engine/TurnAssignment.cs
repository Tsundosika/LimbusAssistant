namespace Tsundosika.LimbusAssistant.Engine;

public sealed record TurnAssignment(
    TurnUnit Unit,
    SkillData Skill,
    EnemyThreat? Threat,
    double WinProbability,
    double ExpectedDamageDealt,
    double ExpectedDamageTaken,
    double ExpectedValue)
{
    public bool IsUnopposed => Threat is null;
}
