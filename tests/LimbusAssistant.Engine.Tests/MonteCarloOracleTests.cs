using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.Engine.Tests;

public class MonteCarloOracleTests
{
    const int Iterations = 200_000;
    const int ExchangeCap = 10_000;
    const double Tolerance = 0.01;

    [Theory]
    [InlineData(4, 7, 1, 0, 0, 0, 5, 4, 2, 0, 0, 0)]
    [InlineData(8, -2, 3, 0, 0, 0, 6, 2, 2, 0, 0, 0)]
    [InlineData(4, 7, 2, 0, 1, 0, 5, 4, 2, 0, 0, 0)]
    [InlineData(4, 7, 1, 0, 0, 3, 5, 4, 2, 0, 0, 0)]
    [InlineData(4, 7, 2, 0, 0, -2, 5, 4, 2, 0, 0, 2)]
    [InlineData(4, 7, 2, 45, 0, 0, 4, 7, 2, -45, 0, 0)]
    [InlineData(4, 3, 4, 10, 0, 0, 5, 2, 3, -10, 0, 0)]
    [InlineData(6, 5, 3, 20, 2, 4, 7, 3, 3, -20, 1, -3)]
    public void CalculateMatchesSimulation(
        int allyBase, int allyCoin, int allyCount, int allySanity, int allyParalyze, int allyModifier,
        int enemyBase, int enemyCoin, int enemyCount, int enemySanity, int enemyParalyze, int enemyModifier)
    {
        var ally = new ClashSkill(allyBase, allyCoin, allyCount, allySanity, allyParalyze, allyModifier);
        var enemy = new ClashSkill(enemyBase, enemyCoin, enemyCount, enemySanity, enemyParalyze, enemyModifier);
        var outcome = new ClashCalculator().Calculate(ally, enemy);

        var random = new Random(12345);
        var wins = 0;
        for (var i = 0; i < Iterations; i++)
        {
            if (SimulateClash(ally, enemy, random))
            {
                wins++;
            }
        }
        var simulated = wins / (double)Iterations;

        Assert.InRange(simulated, outcome.WinProbability - Tolerance, outcome.WinProbability + Tolerance);
    }

    static bool SimulateClash(ClashSkill ally, ClashSkill enemy, Random random)
    {
        var allyCoins = ally.CoinCount;
        var enemyCoins = enemy.CoinCount;
        var allyParalyze = ally.Paralyze;
        var enemyParalyze = enemy.Paralyze;
        for (var exchange = 0; exchange < ExchangeCap; exchange++)
        {
            if (allyCoins <= 0)
            {
                return false;
            }
            if (enemyCoins <= 0)
            {
                return true;
            }
            var allyPower = RollPower(ally, allyCoins, allyParalyze, random);
            var enemyPower = RollPower(enemy, enemyCoins, enemyParalyze, random);
            var previousAllyCoins = allyCoins;
            var previousEnemyCoins = enemyCoins;
            if (allyPower > enemyPower)
            {
                enemyCoins--;
            }
            else if (enemyPower > allyPower)
            {
                allyCoins--;
            }
            allyParalyze = Math.Max(0, allyParalyze - previousAllyCoins);
            enemyParalyze = Math.Max(0, enemyParalyze - previousEnemyCoins);
        }
        throw new InvalidOperationException("Clash simulation did not resolve.");
    }

    static int RollPower(ClashSkill skill, int coins, int paralyze, Random random)
    {
        var flipped = Math.Max(0, coins - paralyze);
        var headProbability = (50 + Math.Clamp(skill.Sanity, ClashSkill.MinSanity, ClashSkill.MaxSanity)) / 100.0;
        var heads = 0;
        for (var i = 0; i < flipped; i++)
        {
            if (random.NextDouble() < headProbability)
            {
                heads++;
            }
        }
        return Math.Max(0, skill.BasePower + skill.CoinPower * heads + skill.Modifier);
    }
}
