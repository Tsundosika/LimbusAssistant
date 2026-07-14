using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.Engine.Tests;

public class CoachProgressTests
{
    static BestMoveAdvice Move(string identity, string skill, string? target = "Enemy Hit") => new(
        identity, identity, 1, skill, target is null, target is null ? null : "Enemy", target, 0.7, 10, 2);

    [Fact]
    public void StartsEmptyAndComplete()
    {
        var progress = new CoachProgress();
        Assert.Equal(-1, progress.State.CurrentIndex);
        Assert.Equal(0, progress.State.Total);
    }

    [Fact]
    public void ObservingTheCurrentPairingAdvances()
    {
        var progress = new CoachProgress();
        progress.Reset([Move("Don", "Lance"), Move("Gregor", "Claw")]);
        Assert.Equal(0, progress.State.CurrentIndex);
        Assert.True(progress.ObservePairing("Don", "Lance", "Enemy Hit"));
        Assert.Equal(1, progress.State.CurrentIndex);
        Assert.Equal(1, progress.State.DoneCount);
    }

    [Fact]
    public void ObservingAnyUndoneMoveTicksItOffOutOfOrder()
    {
        var progress = new CoachProgress();
        progress.Reset([Move("Don", "Lance"), Move("Gregor", "Claw")]);
        Assert.True(progress.ObservePairing("Gregor", "Claw", "Enemy Hit"));
        Assert.Equal(0, progress.State.CurrentIndex);
        Assert.Equal(1, progress.State.DoneCount);
    }

    [Fact]
    public void WrongTargetDoesNotTick()
    {
        var progress = new CoachProgress();
        progress.Reset([Move("Don", "Lance", "Enemy Hit")]);
        Assert.False(progress.ObservePairing("Don", "Lance", "Other Attack"));
        Assert.Equal(0, progress.State.DoneCount);
    }

    [Fact]
    public void HoveringTheSkillWithoutItsTargetDoesNotTick()
    {
        var progress = new CoachProgress();
        progress.Reset([Move("Don", "Lance", "Enemy Hit")]);
        Assert.False(progress.ObservePairing("Don", "Lance", null));
        Assert.Equal(0, progress.State.DoneCount);
    }

    [Fact]
    public void RebaseKeepsDoneMovesWhenThePlanIsRecomputed()
    {
        var progress = new CoachProgress();
        progress.Reset([Move("Don", "Lance"), Move("Gregor", "Claw")]);
        progress.ObservePairing("Don", "Lance", "Enemy Hit");
        progress.Rebase([Move("Gregor", "Claw"), Move("Don", "Lance")]);
        Assert.Equal(1, progress.State.DoneCount);
        Assert.Equal(0, progress.State.CurrentIndex);
        Assert.True(progress.State.Done[1]);
    }

    [Fact]
    public void ManualAdvanceSkipsTheCurrentMove()
    {
        var progress = new CoachProgress();
        progress.Reset([Move("Don", "Lance"), Move("Gregor", "Claw")]);
        Assert.True(progress.AdvanceManually());
        Assert.Equal(1, progress.State.CurrentIndex);
        Assert.True(progress.AdvanceManually());
        Assert.True(progress.State.IsComplete);
        Assert.False(progress.AdvanceManually());
    }

    [Fact]
    public void NewTurnResetsFlagsAndIncrementsTurnNumber()
    {
        var progress = new CoachProgress();
        progress.Reset([Move("Don", "Lance")]);
        progress.AdvanceManually();
        progress.NewTurn([Move("Don", "Lance"), Move("Gregor", "Claw")]);
        Assert.Equal(1, progress.State.TurnNumber);
        Assert.Equal(0, progress.State.DoneCount);
        Assert.Equal(0, progress.State.CurrentIndex);
    }

    [Fact]
    public void DuplicateObservationDoesNotDoubleTick()
    {
        var progress = new CoachProgress();
        progress.Reset([Move("Don", "Lance"), Move("Don", "Lance")]);
        Assert.True(progress.ObservePairing("Don", "Lance", "Enemy Hit"));
        Assert.True(progress.ObservePairing("Don", "Lance", "Enemy Hit"));
        Assert.True(progress.State.IsComplete);
    }
}
