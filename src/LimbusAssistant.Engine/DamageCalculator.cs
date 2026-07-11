namespace Tsundosika.LimbusAssistant.Engine;

public static class DamageCalculator
{
    const double ClashCountBonus = 0.03;

    public static double FinalDamage(double coinRollTotal, DamageContext context)
    {
        var staticModifier = SinResistanceModifier(context.SinResistance)
            + PhysicalResistanceModifier(context.PhysicalResistance, context.Stagger)
            + OffenseAdvantage(context.OffenseLevel, context.DefenseLevel)
            + ClashCountBonus * context.ClashCount;
        var damage = coinRollTotal * (1 + staticModifier) * (1 + context.DynamicModifier);
        return Math.Max(0, damage);
    }

    public static double SinResistanceModifier(double multiplier) => ResistanceModifier(multiplier);

    public static double PhysicalResistanceModifier(double multiplier, StaggerLevel stagger) => stagger switch
    {
        StaggerLevel.Staggered => 1.0,
        StaggerLevel.StaggeredPlus => 1.5,
        StaggerLevel.StaggeredPlusPlus => 2.0,
        _ => ResistanceModifier(multiplier),
    };

    public static double OffenseAdvantage(int offenseLevel, int defenseLevel)
    {
        var difference = offenseLevel - defenseLevel;
        return difference / (double)(Math.Abs(difference) + 25);
    }

    static double ResistanceModifier(double multiplier) => multiplier switch
    {
        <= 0 => -0.5,
        < 1 => (multiplier - 1) / 2,
        _ => multiplier - 1,
    };
}
