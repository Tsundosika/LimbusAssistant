namespace Tsundosika.LimbusAssistant.Engine;

public static class CoachUiText
{
    public static string NowPrefix(string locale) => locale == "de" ? "JETZT: " : "NOW: ";

    public static string Headline(string locale, int done, int total) => locale == "de"
        ? $"Idealer Zug ({done} von {total} erledigt)"
        : $"Ideal turn ({done} of {total} done)";

    public static string NewTurn(string locale) => locale == "de"
        ? "Neue Runde, neuer Plan"
        : "New turn, new plan";

    public static string AllDone(string locale) => locale == "de"
        ? "Alles zugewiesen. Drück To Battle!"
        : "All moves assigned. Press To Battle!";

    public static string NoTeamHint(string locale) => locale == "de"
        ? "Stell dein Team einmal ein (Ctrl+F9, Turn Advisor). Ich merke es mir."
        : "Set your team once (Ctrl+F9, Turn Advisor tab). I will remember it.";

    public static string NoEnemyHint(string locale) => locale == "de"
        ? "Ich schaue zu und warte, bis ich den Gegner erkenne. Fahr mit der Maus über einen Angriff."
        : "Watching, waiting to recognize the enemy. Hover an enemy attack to help me.";

    public static string ObservedTeamNote(string locale) => locale == "de"
        ? "nutze die Sinner, die ich bisher im Kampf gesehen habe"
        : "using the sinners I have seen in this fight";

    public static string SanityAssumedNote(string locale) => locale == "de"
        ? "SP unbekannt, neutral angenommen"
        : "sanity unknown, assuming neutral";

    public static string Intro(string locale, string advanceHotkey) => locale == "de"
        ? $"Folge der JETZT Zeile, ein Zug nach dem anderen. {advanceHotkey} überspringt einen Zug."
        : $"Follow the NOW line, one move at a time. {advanceHotkey} skips a move.";

    public static string CoachPick(string locale) => locale == "de"
        ? "Das ist der Zug vom Coach. Mach es."
        : "This is the coach's pick. Go for it.";

    public static string CoachInstead(string locale, BestMoveAdvice move) => locale == "de"
        ? $"Der Coach empfiehlt für {move.Sinner} lieber Skill {move.SkillNumber} ({move.SkillName})."
        : $"Coach suggests Skill {move.SkillNumber} ({move.SkillName}) for {move.Sinner} instead.";

    public static string MoreUnblocked(string locale, int count) => locale == "de"
        ? $"({count} weitere ungeblockt)"
        : $"({count} more unblocked)";

    public static string FreeHit(string locale) => locale == "de" ? "freier Treffer" : "free hit";
}
