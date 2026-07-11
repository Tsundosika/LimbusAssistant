namespace Tsundosika.LimbusAssistant.Engine;

public static class ExpectedAttackPower
{
    public static double OnClashWin(ClashSkill winner, IReadOnlyList<ClashEndState> winStates) =>
        winStates.Sum(state => state.Probability * RemainingCoinsTotal(winner, state.CoinsRemaining, state.Paralyze));

    public static double Unopposed(ClashSkill skill) =>
        RemainingCoinsTotal(skill, skill.CoinCount, skill.Paralyze);

    static double RemainingCoinsTotal(ClashSkill skill, int coins, int paralyze)
    {
        var total = 0.0;
        for (var coin = 1; coin <= coins; coin++)
        {
            var expectedHeads = Math.Max(0, coin - paralyze) * skill.HeadProbability;
            total += Math.Max(0, skill.BasePower + expectedHeads * skill.CoinPower);
        }
        return total;
    }
}
