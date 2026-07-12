using OpenCvSharp;

namespace Tsundosika.LimbusAssistant.Vision;

public sealed class VisionPipeline(INumberReader numberReader, TemplateLibrary templates, CalibrationProfile profile)
{
    const int FullSweepInterval = 15;
    const double UsableConfidence = 0.60;

    readonly Dictionary<string, PixelRect> _lastResolvedRegions = new();
    readonly Dictionary<string, (ulong Hash, PixelRect Rect, NumberReading Reading)> _numberCache = new();
    int _ticksSinceFullSweep = FullSweepInterval;

    public CalibrationProfile Profile { get; } = profile;

    public Task<VisionReading> ReadAsync(CaptureFrame frame) =>
        ReadAsync(frame, LetterboxDetector.DetectContent(frame));

    public async Task<IReadOnlyList<(PixelRect Rect, NumberReading Reading)>> ReadDockSanityAsync(
        CaptureFrame frame,
        PixelRect content)
    {
        using var mat = FrameMat.ToMat(frame);
        var results = new List<(PixelRect, NumberReading)>();
        foreach (var circle in DockScanner.FindSanityCircles(mat, content))
        {
            var reading = await numberReader.ReadAsync(mat, circle);
            results.Add((circle, reading));
        }
        return results;
    }

    public async Task<(PixelRect Rect, NumberReading Reading)?> ReadDraggerSanityAsync(
        CaptureFrame frame,
        PixelRect content)
    {
        const int maxDistance = 300;
        using var mat = FrameMat.ToMat(frame);
        if (HighlightScanner.FindHighlightedUnit(mat, content) is not { } highlight)
        {
            return null;
        }
        var circles = DockScanner.FindSanityCircles(mat, content, DockScanner.FieldBand);
        if (circles.Count == 0)
        {
            return null;
        }
        var highlightCenterX = highlight.X + highlight.Width / 2.0;
        var highlightCenterY = highlight.Y + highlight.Height / 2.0;
        var nearest = circles
            .Select(circle => (Circle: circle, Distance: Distance(circle, highlightCenterX, highlightCenterY)))
            .OrderBy(pair => pair.Distance)
            .First();
        if (nearest.Distance > maxDistance)
        {
            return null;
        }
        var reading = await numberReader.ReadAsync(mat, nearest.Circle);
        return (nearest.Circle, reading);
    }

    static double Distance(PixelRect circle, double x, double y)
    {
        var cx = circle.X + circle.Width / 2.0;
        var cy = circle.Y + circle.Height / 2.0;
        var dx = cx - x;
        var dy = cy - y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public async Task<VisionReading> ReadAsync(CaptureFrame frame, PixelRect content)
    {
        var numbers = new Dictionary<string, NumberReading>();
        var icons = new Dictionary<string, IconReading>();
        var texts = new Dictionary<string, TextReading>();
        var regions = new Dictionary<string, PixelRect>();
        _ticksSinceFullSweep++;
        using var mat = FrameMat.ToMat(frame);
        foreach (var region in Profile.Regions)
        {
            var baseRect = region.Rect.ToPixelsWithin(content);
            if (region.Kind == RegionKind.Number)
            {
                var (reading, rect) = await ReadNumberAsync(frame, mat, content, region.Name, baseRect);
                numbers[region.Name] = reading;
                regions[region.Name] = rect;
            }
            else if (region.Kind == RegionKind.Text)
            {
                var rect = baseRect;
                if (region.Name == RegionNames.DragSkillName)
                {
                    var (bestText, bestRect) = await ReadBestRibbonAsync(mat, content, baseRect);
                    texts[region.Name] = bestText;
                    regions[region.Name] = bestRect;
                    continue;
                }
                texts[region.Name] = await numberReader.ReadTextAsync(mat, rect);
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
        return new VisionReading(numbers, icons, texts, regions, frame.Width, frame.Height, content, DateTimeOffset.Now);
    }

    async Task<(TextReading Text, PixelRect Rect)> ReadBestRibbonAsync(Mat mat, PixelRect content, PixelRect fallback)
    {
        var candidates = RibbonScanner.FindSkillRibbons(mat, content);
        if (candidates.Count == 0)
        {
            return (await numberReader.ReadTextAsync(mat, fallback), fallback);
        }
        var bestText = TextReading.Empty;
        var bestRect = candidates[0];
        var bestScore = double.MinValue;
        foreach (var candidate in candidates)
        {
            var text = await numberReader.ReadTextAsync(mat, candidate);
            var letters = text.Text.Count(char.IsLetter);
            var score = letters * Math.Max(0.1, text.Confidence);
            if (score > bestScore)
            {
                bestScore = score;
                bestText = text;
                bestRect = candidate;
            }
        }
        return (bestText, bestRect);
    }

    async Task<(NumberReading Reading, PixelRect Rect)> ReadNumberAsync(
        CaptureFrame frame,
        Mat mat,
        PixelRect content,
        string name,
        PixelRect baseRect)
    {
        var seed = _lastResolvedRegions.TryGetValue(name, out var previous) && Fits(previous, content)
            ? previous
            : baseRect;
        var seedHash = FrameHash.SampleRegion(frame, seed);
        if (_numberCache.TryGetValue(name, out var cached)
            && cached.Rect == seed
            && cached.Hash == seedHash
            && (IsUsableNumber(name, cached.Reading) || _ticksSinceFullSweep < FullSweepInterval))
        {
            return (cached.Reading, seed);
        }

        var best = NumberReading.Unknown;
        var bestRect = seed;
        var bestScore = double.MinValue;
        foreach (var candidate in QuickCandidates(seed, baseRect, content))
        {
            var reading = await numberReader.ReadAsync(mat, candidate);
            if (IsUsableNumber(name, reading))
            {
                return Cache(name, seedHash, seed, candidate, reading);
            }
            var score = ScoreNumber(name, reading, candidate, baseRect, content);
            if (score > bestScore)
            {
                bestScore = score;
                best = reading;
                bestRect = candidate;
            }
        }

        if (_ticksSinceFullSweep >= FullSweepInterval)
        {
            _ticksSinceFullSweep = 0;
            foreach (var candidate in SweepCandidates(baseRect, content))
            {
                var reading = await numberReader.ReadAsync(mat, candidate);
                if (IsUsableNumber(name, reading))
                {
                    return Cache(name, seedHash, seed, candidate, reading);
                }
                var score = ScoreNumber(name, reading, candidate, baseRect, content);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = reading;
                    bestRect = candidate;
                }
            }
        }

        return Cache(name, seedHash, seed, bestRect, best);
    }

    (NumberReading, PixelRect) Cache(string name, ulong seedHash, PixelRect seed, PixelRect rect, NumberReading reading)
    {
        _numberCache[name] = (seedHash, seed, reading);
        return (reading, rect);
    }

    IEnumerable<PixelRect> QuickCandidates(PixelRect seed, PixelRect baseRect, PixelRect content)
    {
        var seen = new HashSet<PixelRect> { seed };
        yield return seed;
        if (seen.Add(baseRect))
        {
            yield return baseRect;
        }
        var nudge = MoveRect(baseRect, Math.Max(2, baseRect.Width / 6), 0, content);
        if (seen.Add(nudge))
        {
            yield return nudge;
        }
    }

    IEnumerable<PixelRect> SweepCandidates(PixelRect baseRect, PixelRect content)
    {
        var seen = new HashSet<PixelRect> { baseRect };
        var dxBase = Math.Max(2, baseRect.Width / 6);
        var dyBase = Math.Max(2, baseRect.Height / 4);
        foreach (var xOffset in new[] { 0, -1, 1 })
        {
            foreach (var yOffset in new[] { 0, -1, 1 })
            {
                var moved = MoveRect(baseRect, dxBase * xOffset, dyBase * yOffset, content);
                if (seen.Add(moved))
                {
                    yield return moved;
                }
            }
        }
    }

    static bool IsUsableNumber(string name, NumberReading reading) =>
        reading.Value is not null
        && reading.Confidence >= UsableConfidence
        && IsPlausibleValue(name, reading.Value.Value);

    static double ScoreNumber(string name, NumberReading reading, PixelRect candidate, PixelRect baseRect, PixelRect content)
    {
        if (reading.Value is null)
        {
            return -10;
        }
        var rangeBonus = IsPlausibleValue(name, reading.Value.Value) ? 0.2 : -0.5;
        return reading.Confidence + rangeBonus - PositionPenalty(candidate, baseRect, content);
    }

    static bool IsPlausibleValue(string name, int value)
    {
        if (name.StartsWith("hud.sanity.", StringComparison.Ordinal))
        {
            return value is >= -45 and <= 45;
        }
        return name switch
        {
            RegionNames.AllySanity => value is >= -45 and <= 45,
            RegionNames.AllyClashCoins or RegionNames.EnemyClashCoins => value is >= 0 and <= 30,
            RegionNames.InGameWinRate => value is >= 0 and <= 100,
            _ => value is >= -99 and <= 999,
        };
    }

    PixelRect ResolveIconRect(PixelRect content, string name, PixelRect baseRect)
    {
        if (_lastResolvedRegions.TryGetValue(name, out var previous) && Fits(previous, content))
        {
            return previous;
        }
        return baseRect;
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
