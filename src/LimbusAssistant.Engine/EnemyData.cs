namespace Tsundosika.LimbusAssistant.Engine;

public sealed record EnemyData(
    string Id,
    string Name,
    int DefenseLevel,
    int StaggerThreshold,
    ResistanceSet Resistances,
    IReadOnlyList<SkillData> Skills);
