namespace Tsundosika.LimbusAssistant.Engine;

public sealed record BestMoveReport(
    IReadOnlyList<BestMoveAdvice> Moves,
    IReadOnlyList<UnblockedThreat> Unblocked,
    double TotalExpectedValue);
