namespace Tsundosika.LimbusAssistant.Engine;

public sealed record SkillData(
    string Id,
    string Name,
    int BasePower,
    int CoinPower,
    int CoinCount,
    SinType Sin,
    DamageType DamageType,
    int OffenseLevel);
