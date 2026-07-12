namespace Tsundosika.LimbusAssistant.CaptureProbe;

public sealed record ProbeOptions(
    bool Watch,
    int Seconds,
    int IntervalMilliseconds,
    string? WindowTitle,
    string OutputDirectory,
    string? ProfilePath,
    bool NoOcr,
    bool Stages)
{
    public string? InputFile { get; init; }

    public static ProbeOptions? Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("shot" or "watch" or "file"))
        {
            return null;
        }
        if (args[0] == "file")
        {
            if (args.Length < 2 || !File.Exists(args[1]))
            {
                return null;
            }
            var fileOptions = ParseCommon(args, 2, false);
            return fileOptions is null ? null : fileOptions with { InputFile = args[1] };
        }
        var watch = args[0] == "watch";
        return ParseCommon(args, 1, watch);
    }

    static ProbeOptions? ParseCommon(string[] args, int startIndex, bool watch)
    {
        var seconds = 20;
        var interval = 400;
        string? window = null;
        string? output = null;
        string? profile = null;
        var noOcr = false;
        var stages = false;
        for (var i = startIndex; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var s):
                    seconds = s;
                    i++;
                    break;
                case "--interval" when i + 1 < args.Length && int.TryParse(args[i + 1], out var ms):
                    interval = ms;
                    i++;
                    break;
                case "--window" when i + 1 < args.Length:
                    window = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--profile" when i + 1 < args.Length:
                    profile = args[++i];
                    break;
                case "--no-ocr":
                    noOcr = true;
                    break;
                case "--stages":
                    stages = true;
                    break;
                default:
                    return null;
            }
        }
        output ??= Path.Combine("probe-out", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        return new ProbeOptions(watch, seconds, interval, window, output, profile, noOcr, stages);
    }

    public static string Usage =>
        "usage: CaptureProbe shot|watch|file <png> [--seconds N] [--interval MS] [--window TITLE] [--out DIR] [--profile PATH] [--no-ocr] [--stages]";
}
