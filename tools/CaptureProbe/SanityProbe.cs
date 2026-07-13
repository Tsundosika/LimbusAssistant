using OpenCvSharp;
using Tsundosika.LimbusAssistant.Engine;
using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant.CaptureProbe;

public sealed class SanityProbe(WindowsNumberReader reader, DigitTemplateReader digits)
{
    public async Task<int> RunAsync(string imagePath, string dataDirectory)
    {
        using var bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (bgr.Empty())
        {
            Console.WriteLine($"could not read image: {imagePath}");
            return 1;
        }
        using var bgra = new Mat();
        Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);
        var pixels = new byte[bgra.Width * bgra.Height * 4];
        System.Runtime.InteropServices.Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);
        var frame = new CaptureFrame(pixels, bgra.Width, bgra.Height);
        var content = LetterboxDetector.DetectContent(frame);

        var data = GameData.Load(dataDirectory);
        using var templates = TemplateLibrary.LoadFrom(
            Path.Combine("src", "LimbusAssistant", "Assets", "Templates"));
        var pipeline = new VisionPipeline(reader, templates, CalibrationProfile.Default, digits);
        var tracker = new SanityTracker();

        await SanityFrameIngest.IngestDockAsync(tracker, pipeline, frame, content, null, 0);
        await SanityFrameIngest.IngestFieldAsync(tracker, pipeline, frame, content, 0);
        var acting = await ResolveActingIdentityAsync(bgra, content, data);
        Console.WriteLine($"acting nameplate: {acting ?? "not matched"}");
        if (acting is not null)
        {
            await SanityFrameIngest.IngestActingAsync(tracker, pipeline, frame, content, acting, 0);
        }

        var entries = tracker.Snapshot();
        Console.WriteLine($"tracker entries: {entries.Count}");
        foreach (var (key, entry) in entries)
        {
            Console.WriteLine($"  {key,-28} {entry.Value,4}  ({SanityTracker.Label(entry.Source)})");
        }
        if (acting is not null && tracker.Resolve(acting) is { } resolved)
        {
            Console.WriteLine($"acting {acting} = {resolved.Value} via {SanityTracker.Label(resolved.Source)}");
        }
        else if (acting is not null)
        {
            Console.WriteLine($"acting {acting} = unknown");
        }
        return 0;
    }

    async Task<string?> ResolveActingIdentityAsync(Mat mat, PixelRect content, GameData data)
    {
        var region = CalibrationProfile.Default.Regions
            .FirstOrDefault(candidate => candidate.Name == RegionNames.TargetUnitName);
        if (region is null)
        {
            return null;
        }
        var rect = region.Rect.ToPixelsWithin(content);
        var text = await reader.ReadTextAsync(mat, rect);
        Console.WriteLine($"nameplate text: \"{text.Text}\" conf {text.Confidence:F2}");
        var normalized = Normalize(text.Text);
        if (normalized.Length < 6)
        {
            return null;
        }
        string? best = null;
        var bestLength = 0;
        foreach (var identity in data.Identities)
        {
            var candidate = Normalize(identity.Name);
            if (candidate.Length >= 6
                && candidate.Length > bestLength
                && (normalized.Contains(candidate, StringComparison.Ordinal)
                    || candidate.Contains(normalized, StringComparison.Ordinal)))
            {
                best = identity.Name;
                bestLength = candidate.Length;
            }
        }
        return best;
    }

    static string Normalize(string text) =>
        new(text.ToLowerInvariant().Where(char.IsAsciiLetter).ToArray());
}
