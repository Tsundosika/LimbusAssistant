namespace Tsundosika.LimbusAssistant.Vision;

public sealed class VisionPipeline(INumberReader numberReader, TemplateLibrary templates, CalibrationProfile profile)
{
    public CalibrationProfile Profile { get; } = profile;

    public async Task<VisionReading> ReadAsync(CaptureFrame frame)
    {
        var content = LetterboxDetector.DetectContent(frame);
        var numbers = new Dictionary<string, NumberReading>();
        var icons = new Dictionary<string, IconReading>();
        using var mat = FrameMat.ToMat(frame);
        foreach (var region in Profile.Regions)
        {
            var rect = region.Rect.ToPixelsWithin(content);
            if (region.Kind == RegionKind.Number)
            {
                numbers[region.Name] = await numberReader.ReadAsync(frame, rect);
            }
            else
            {
                using var gray = FrameMat.CropGray(mat, rect);
                icons[region.Name] = templates.IsEmpty ? IconReading.Unknown : templates.BestMatch(gray);
            }
        }
        return new VisionReading(numbers, icons, frame.Width, frame.Height, content, DateTimeOffset.Now);
    }
}
