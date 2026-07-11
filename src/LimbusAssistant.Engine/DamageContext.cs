namespace Tsundosika.LimbusAssistant.Engine;

public sealed record DamageContext(
    double SinResistance,
    double PhysicalResistance,
    StaggerLevel Stagger,
    int OffenseLevel,
    int DefenseLevel,
    int ClashCount,
    double DynamicModifier = 0);
