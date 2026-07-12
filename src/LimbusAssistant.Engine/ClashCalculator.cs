namespace Tsundosika.LimbusAssistant.Engine;

public sealed class ClashCalculator
{
    readonly Dictionary<(ClashSkill Left, ClashSkill Right), (List<ClashEndState> Left, List<ClashEndState> Right)> _cache = new();

    public ClashOutcome Calculate(ClashSkill ally, ClashSkill enemy)
    {
        var (winStates, loseStates) = ResolveClash(ally, enemy);
        var win = winStates.Sum(state => state.Probability);
        var lose = loseStates.Sum(state => state.Probability);
        var unresolved = Math.Clamp(1 - win - lose, 0, 1);
        return new ClashOutcome(win, lose, unresolved, winStates, loseStates);
    }

    public static double FirstExchangeWinProbability(ClashSkill ally, ClashSkill enemy)
    {
        var (leftWin, _, rightWin) = SingleExchange(ally, enemy);
        var decisive = leftWin + rightWin;
        return decisive <= 0 ? 0.5 : leftWin / decisive;
    }

    public static ExchangeProbabilities SingleExchange(ClashSkill left, ClashSkill right)
    {
        var leftWin = 0.0;
        var draw = 0.0;
        var rightWin = 0.0;
        var leftDistribution = left.PowerDistribution();
        var rightDistribution = right.PowerDistribution();
        foreach (var leftOutcome in leftDistribution)
        {
            foreach (var rightOutcome in rightDistribution)
            {
                var joint = leftOutcome.Probability * rightOutcome.Probability;
                if (leftOutcome.Power > rightOutcome.Power)
                {
                    leftWin += joint;
                }
                else if (leftOutcome.Power < rightOutcome.Power)
                {
                    rightWin += joint;
                }
                else
                {
                    draw += joint;
                }
            }
        }
        return new ExchangeProbabilities(leftWin, draw, rightWin);
    }

    (List<ClashEndState> Left, List<ClashEndState> Right) ResolveClash(ClashSkill left, ClashSkill right)
    {
        if (left.CoinCount <= 0)
        {
            return ([], [new ClashEndState(1.0, right.CoinCount, right.Paralyze, left.Paralyze)]);
        }
        if (right.CoinCount <= 0)
        {
            return ([new ClashEndState(1.0, left.CoinCount, left.Paralyze, right.Paralyze)], []);
        }
        if (_cache.TryGetValue((left, right), out var cached))
        {
            return cached;
        }

        var (leftWin, draw, rightWin) = SingleExchange(left, right);
        if (left.Paralyze == 0 && right.Paralyze == 0)
        {
            var decisive = leftWin + rightWin;
            if (decisive == 0)
            {
                var stalemate = (new List<ClashEndState>(), new List<ClashEndState>());
                _cache[(left, right)] = stalemate;
                return stalemate;
            }
            leftWin /= decisive;
            rightWin /= decisive;
            draw = 0;
        }

        var leftMerged = new Dictionary<(int Coins, int Paralyze, int OpponentParalyze), double>();
        var rightMerged = new Dictionary<(int Coins, int Paralyze, int OpponentParalyze), double>();
        AccumulateBranch(leftWin, () => ResolveClash(left.AfterWin(), right.AfterLose()), leftMerged, rightMerged);
        AccumulateBranch(draw, () => ResolveClash(left.AfterWin(), right.AfterWin()), leftMerged, rightMerged);
        AccumulateBranch(rightWin, () => ResolveClash(left.AfterLose(), right.AfterWin()), leftMerged, rightMerged);

        var result = (ToEndStates(leftMerged), ToEndStates(rightMerged));
        _cache[(left, right)] = result;
        return result;
    }

    static void AccumulateBranch(
        double branchProbability,
        Func<(List<ClashEndState> Left, List<ClashEndState> Right)> resolve,
        Dictionary<(int, int, int), double> leftMerged,
        Dictionary<(int, int, int), double> rightMerged)
    {
        if (branchProbability <= 0)
        {
            return;
        }
        var (leftStates, rightStates) = resolve();
        Merge(branchProbability, leftStates, leftMerged);
        Merge(branchProbability, rightStates, rightMerged);
    }

    static void Merge(double branchProbability, List<ClashEndState> states, Dictionary<(int, int, int), double> merged)
    {
        foreach (var state in states)
        {
            var key = (state.CoinsRemaining, state.Paralyze, state.OpponentParalyze);
            merged[key] = merged.GetValueOrDefault(key) + branchProbability * state.Probability;
        }
    }

    static List<ClashEndState> ToEndStates(Dictionary<(int Coins, int Paralyze, int OpponentParalyze), double> merged) =>
        merged
            .OrderBy(pair => pair.Key)
            .Select(pair => new ClashEndState(pair.Value, pair.Key.Coins, pair.Key.Paralyze, pair.Key.OpponentParalyze))
            .ToList();
}
