namespace Tsundosika.LimbusAssistant.Engine;

public sealed record AlternativeMove(
    int SkillNumber,
    string SkillName,
    double WinProbability,
    double ExpectedDamageDealt);
