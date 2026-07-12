using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant;

public sealed record PlanningHint(
    string RawSkillName,
    SkillData? Skill,
    string? IdentityName,
    int? Sanity,
    double Confidence);
