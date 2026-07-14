using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.Engine.Tests;

public class CoachMovesTests
{
    static BestMoveAdvice Move(string identity, string skill, string? target) => new(
        identity, identity, 1, skill, target is null, target is null ? null : "Enemy", target, 0.7, 10, 2);

    [Fact]
    public void SamePlanIsEqualAcrossRecomputes()
    {
        var first = new List<BestMoveAdvice> { Move("Don", "Lance", "Hit"), Move("Gregor", "Claw", null) };
        var second = new List<BestMoveAdvice> { Move("Don", "Lance", "Hit"), Move("Gregor", "Claw", null) };
        Assert.True(CoachMoves.SameMoves(first, second));
    }

    [Fact]
    public void DifferentTargetIsADifferentPlan()
    {
        var first = new List<BestMoveAdvice> { Move("Don", "Lance", "Hit") };
        var second = new List<BestMoveAdvice> { Move("Don", "Lance", "Other") };
        Assert.False(CoachMoves.SameMoves(first, second));
    }

    [Fact]
    public void DifferentLengthIsADifferentPlan()
    {
        var first = new List<BestMoveAdvice> { Move("Don", "Lance", "Hit") };
        Assert.False(CoachMoves.SameMoves(first, []));
    }

    [Fact]
    public void NullsOnlyEqualEachOther()
    {
        Assert.True(CoachMoves.SameMoves(null, null));
        Assert.False(CoachMoves.SameMoves(null, []));
    }
}
