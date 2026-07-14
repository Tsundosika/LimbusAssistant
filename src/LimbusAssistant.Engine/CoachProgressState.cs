namespace Tsundosika.LimbusAssistant.Engine;

public sealed record CoachProgressState(
    IReadOnlyList<bool> Done,
    int CurrentIndex,
    int TurnNumber)
{
    public int DoneCount => Done.Count(flag => flag);

    public int Total => Done.Count;

    public bool IsComplete => CurrentIndex < 0 && Total > 0;

    public static CoachProgressState Empty { get; } = new([], -1, 0);
}
