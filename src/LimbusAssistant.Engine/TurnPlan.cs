namespace Tsundosika.LimbusAssistant.Engine;

public sealed record TurnPlan(IReadOnlyList<TurnAssignment> Assignments, double TotalExpectedValue);
