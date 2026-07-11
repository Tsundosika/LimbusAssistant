namespace Tsundosika.LimbusAssistant.Engine;

public sealed record EnemyThreat(
    EnemyData Enemy,
    SkillData Skill,
    StaggerLevel Stagger = StaggerLevel.None,
    int ClashCount = 0,
    int Sanity = 0);
