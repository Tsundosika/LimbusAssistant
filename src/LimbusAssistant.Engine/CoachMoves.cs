namespace Tsundosika.LimbusAssistant.Engine;

public static class CoachMoves
{
    public static bool SameMoves(IReadOnlyList<BestMoveAdvice>? first, IReadOnlyList<BestMoveAdvice>? second)
    {
        if (first is null || second is null)
        {
            return ReferenceEquals(first, second);
        }
        if (first.Count != second.Count)
        {
            return false;
        }
        for (var i = 0; i < first.Count; i++)
        {
            if (first[i].IdentityName != second[i].IdentityName
                || first[i].SkillName != second[i].SkillName
                || first[i].TargetSkillName != second[i].TargetSkillName)
            {
                return false;
            }
        }
        return true;
    }
}
