namespace Tsundosika.LimbusAssistant.Vision;

public sealed class VisionPipeline(INumberReader numberReader, TemplateLibrary templates, CalibrationProfile profile)
{
    readonly Dictionary<string, PixelRect> _lastResolvedRegions = new();

    public CalibrationProfile Profile { get; } = profile;

    public async Task<VisionReading> ReadAsync(CaptureFrame frame)
    {
        var content = LetterboxDetector.DetectContent(frame);
        
        var numbers = new Dictionary<string, NumberReading>();
        var icons = new Dictionary<string, IconReading>();
        var regions = new Dictionary<string, PixelRect>();
        
        using var mat = FrameMat.ToMat(frame);
        foreach (var region in Profile.Regions)
        {
            var baseRect = region.Rect.ToPixelsWithin(content);

            if (region.Kind == RegionKind.Number)
            {
                var (reading, rect) = await ReadNumberWithDynamicSearchAsync(frame, content, region.Name, baseRect);
                numbers[region.Name] = reading;
                regions[region.Name] = rect;
            }
            else
            {
                var rect = ResolveIconRect(content, region.Name, baseRect);
                using var gray = FrameMat.CropGray(mat, rect);
                icons[region.Name] = templates.IsEmpty ? IconReading.Unknown : templates.BestMatch(gray);
                regions[region.Name] = rect;
            }
        }
        foreach (var (name, rect) in regions)
        {
            _lastResolvedRegions[name] = rect;
        }
        return new VisionReading(numbers, icons, regions, frame.Width, frame.Height, content, DateTimeOffset.Now);
    }

    async Task<(NumberReading Reading, PixelRect Rect)> ReadNumberWithDynamicSearchAsync(
        CaptureFrame frame,
        PixelRect content,
        string name,
        PixelRect baseRect)
    {
        var initial = await numberReader.ReadAsync(frame, baseRect);
        if (IsUsableNumber(name, initial))
        {
            return (initial, baseRect);
        }

        var candidates = BuildCandidateRects(content, name, baseRect).ToList();

        var bestReading = initial;
        var bestRect = baseRect;
        var bestScore = ScoreNumber(name, initial, baseRect, baseRect, content);
        foreach (var candidate in candidates)
        {
            var reading = await numberReader.ReadAsync(frame, candidate);
            if (IsUsableNumber(name, reading))
            {
                return (reading, candidate);
            }
            var score = ScoreNumber(name, reading, candidate, baseRect, content);
            if (score > bestScore)
            {
                bestScore = score;
                bestReading = reading;
                bestRect = candidate;
            }
        }

        return (bestReading, bestRect);
    }

    static bool IsUsableNumber(string name, NumberReading reading) =>
        reading.Value is not null
        && reading.Confidence >= 0.60
        && IsPlausibleValue(name, reading.Value.Value);

    static double ScoreNumber(string name, NumberReading reading, PixelRect candidate, PixelRect baseRect, PixelRect content)
    {
        if (reading.Value is null)
        {
            return -10;
        }
        var rangeBonus = IsPlausibleValue(name, reading.Value.Value) ? 0.2 : -0.5;
        var distancePenalty = PositionPenalty(candidate, baseRect, content);
        return reading.Confidence + rangeBonus - distancePenalty;
    }

    static bool IsPlausibleValue(string name, int value) => name switch
    {
        RegionNames.AllySanity => value is >= -45 and <= 45,
        RegionNames.AllyClashCoins or RegionNames.EnemyClashCoins => value is >= 0 and <= 30,
        RegionNames.InGameWinRate => value is >= 0 and <= 100,
        _ => value is >= -99 and <= 999,
    };

    PixelRect ResolveIconRect(PixelRect content, string name, PixelRect baseRect)
    {
        if (_lastResolvedRegions.TryGetValue(name, out var previous)
            && Fits(previous, content))
        {
            return previous;
        }
        return baseRect;
    }

    IEnumerable<PixelRect> BuildCandidateRects(PixelRect content, string name, PixelRect baseRect)
    {
        var seen = new HashSet<PixelRect>();
        if (seen.Add(baseRect))
        {
            yield return baseRect;
        }
        if (_lastResolvedRegions.TryGetValue(name, out var previous)
            && Fits(previous, content)
            && seen.Add(previous))
        {
            yield return previous;
        }

        var dxBase = Math.Max(2, baseRect.Width / 6);
        var dyBase = Math.Max(2, baseRect.Height / 4);
        var xOffsets = new[] { 0, -1, 1 };
        var yOffsets = new[] { 0, -1, 1 };
        foreach (var xOffset in xOffsets)
        {
            foreach (var yOffset in yOffsets)
            {
                var dx = dxBase * xOffset;
                var dy = dyBase * yOffset;
                var moved = MoveRect(baseRect, dx, dy, content);
                if (seen.Add(moved))
                {
                    yield return moved;
                }
            }
        }
    }

    static PixelRect MoveRect(PixelRect rect, int dx, int dy, PixelRect bounds)
    {
        var minX = bounds.X;
        var minY = bounds.Y;
        var maxX = bounds.X + bounds.Width - rect.Width;
        var maxY = bounds.Y + bounds.Height - rect.Height;
        var x = Math.Clamp(rect.X + dx, minX, Math.Max(minX, maxX));
        var y = Math.Clamp(rect.Y + dy, minY, Math.Max(minY, maxY));
        return new PixelRect(x, y, rect.Width, rect.Height);
    }

    static bool Fits(PixelRect rect, PixelRect bounds)
    {
        var right = rect.X + rect.Width;
        var bottom = rect.Y + rect.Height;
        return rect.X >= bounds.X
            && rect.Y >= bounds.Y
            && right <= bounds.X + bounds.Width
            && bottom <= bounds.Y + bounds.Height;
    }

    static double PositionPenalty(PixelRect candidate, PixelRect baseRect, PixelRect content)
    {
        var cx = candidate.X + candidate.Width * 0.5;
        var cy = candidate.Y + candidate.Height * 0.5;
        var bx = baseRect.X + baseRect.Width * 0.5;
        var by = baseRect.Y + baseRect.Height * 0.5;
        var dx = Math.Abs(cx - bx) / Math.Max(1.0, content.Width);
        var dy = Math.Abs(cy - by) / Math.Max(1.0, content.Height);
        return dx * 1.8 + dy * 2.6;
    }
}
