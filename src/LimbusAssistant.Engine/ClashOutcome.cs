namespace Tsundosika.LimbusAssistant.Engine;

public sealed record ClashOutcome(
    double WinProbability,
    double LoseProbability,
    double UnresolvedProbability,
    IReadOnlyList<ClashEndState> WinStates,
    IReadOnlyList<ClashEndState> LoseStates)
{
    public double EffectiveWinProbability => WinProbability + UnresolvedProbability / 2;

    public double ExpectedCoinsOnWin =>
        WinProbability <= 0
            ? 0
            : WinStates.Sum(state => state.Probability * state.CoinsRemaining) / WinProbability;
}
