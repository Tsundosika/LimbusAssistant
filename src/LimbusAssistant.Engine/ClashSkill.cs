namespace Tsundosika.LimbusAssistant.Engine;

public sealed record ClashSkill(int BasePower, int CoinPower, int CoinCount, int Sanity, int Paralyze = 0)
{
    public const int MinSanity = -45;
    public const int MaxSanity = 45;

    public double HeadProbability => (50 + Math.Clamp(Sanity, MinSanity, MaxSanity)) / 100.0;

    public ClashSkill AfterWin() => this with { Paralyze = Math.Max(0, Paralyze - CoinCount) };

    public ClashSkill AfterLose() => this with
    {
        CoinCount = CoinCount - 1,
        Paralyze = Math.Max(0, Paralyze - CoinCount),
    };

    public IReadOnlyList<PowerOutcome> PowerDistribution()
    {
        var flipped = Math.Max(0, CoinCount - Paralyze);
        var headProbability = HeadProbability;
        var byPower = new SortedDictionary<int, double>();
        for (var heads = 0; heads <= flipped; heads++)
        {
            var power = Math.Max(0, BasePower + CoinPower * heads);
            var probability = BinomialCoefficient(flipped, heads)
                * Math.Pow(headProbability, heads)
                * Math.Pow(1 - headProbability, flipped - heads);
            byPower[power] = byPower.GetValueOrDefault(power) + probability;
        }
        return byPower.Select(pair => new PowerOutcome(pair.Key, pair.Value)).ToList();
    }

    static double BinomialCoefficient(int n, int k)
    {
        var result = 1.0;
        for (var i = 1; i <= k; i++)
        {
            result *= (n - k + i) / (double)i;
        }
        return result;
    }
}
