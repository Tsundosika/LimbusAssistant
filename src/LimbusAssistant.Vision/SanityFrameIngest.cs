namespace Tsundosika.LimbusAssistant.Vision;

public static class SanityFrameIngest
{
    const double MinConfidence = 0.4;

    public static async Task IngestDockAsync(
        SanityTracker tracker,
        VisionPipeline pipeline,
        CaptureFrame frame,
        PixelRect content,
        IReadOnlyList<string>? teamNames,
        long now)
    {
        var slots = await pipeline.ReadDockSanityAsync(frame, content);
        var usable = slots
            .Where(slot => IsUsable(slot.Reading))
            .OrderBy(slot => slot.Rect.X)
            .ToList();
        for (var index = 0; index < usable.Count; index++)
        {
            tracker.Report($"dock {index + 1}", usable[index].Reading.Value!.Value, SanitySource.DockSlot, now);
        }
        if (teamNames is not { Count: > 0 })
        {
            return;
        }
        var band = DockScanner.DockBand.ToPixelsWithin(content);
        foreach (var slot in usable)
        {
            var center = slot.Rect.X + slot.Rect.Width / 2.0;
            var index = (int)((center - band.X) * teamNames.Count / Math.Max(1, band.Width));
            index = Math.Clamp(index, 0, teamNames.Count - 1);
            tracker.Report(teamNames[index], slot.Reading.Value!.Value, SanitySource.DockSlot, now);
        }
    }

    public static async Task IngestFieldAsync(
        SanityTracker tracker,
        VisionPipeline pipeline,
        CaptureFrame frame,
        PixelRect content,
        long now)
    {
        var circles = await pipeline.ReadFieldSanityAsync(frame, content);
        var usable = circles
            .Where(circle => IsUsable(circle.Reading))
            .OrderBy(circle => circle.Rect.X)
            .ToList();
        for (var index = 0; index < usable.Count; index++)
        {
            tracker.Report($"field {index + 1}", usable[index].Reading.Value!.Value, SanitySource.FieldDirect, now);
        }
    }

    public static async Task IngestActingAsync(
        SanityTracker tracker,
        VisionPipeline pipeline,
        CaptureFrame frame,
        PixelRect content,
        string? actingIdentity,
        long now)
    {
        if (actingIdentity is null)
        {
            return;
        }
        var acting = await pipeline.ReadDraggerSanityAsync(frame, content);
        if (acting is null || !IsUsable(acting.Value.Reading))
        {
            return;
        }
        var source = acting.Value.Reading.RawText.StartsWith("dockrank:", StringComparison.Ordinal)
            ? SanitySource.DockRank
            : SanitySource.FieldDirect;
        tracker.Report(actingIdentity, acting.Value.Reading.Value!.Value, source, now);
    }

    static bool IsUsable(NumberReading reading) =>
        reading.Value is >= -45 and <= 45 && reading.Confidence >= MinConfidence;
}
