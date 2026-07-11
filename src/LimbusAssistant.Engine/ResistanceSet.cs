namespace Tsundosika.LimbusAssistant.Engine;

public sealed record ResistanceSet(
    IReadOnlyDictionary<DamageType, double> Physical,
    IReadOnlyDictionary<SinType, double> Sin)
{
    public double PhysicalFor(DamageType damageType) => Physical.GetValueOrDefault(damageType, 1.0);

    public double SinFor(SinType sin) => Sin.GetValueOrDefault(sin, 1.0);
}
