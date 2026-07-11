namespace Tsundosika.LimbusAssistant.Vision;

public sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName)
{
    public override string ToString() =>
        string.IsNullOrEmpty(ProcessName) ? Title : $"{Title} — {ProcessName}";
}
