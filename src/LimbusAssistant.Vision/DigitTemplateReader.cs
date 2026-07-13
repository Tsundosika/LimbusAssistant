using OpenCvSharp;

namespace Tsundosika.LimbusAssistant.Vision;

public sealed class DigitTemplateReader : IDisposable
{
    const int GlyphWidth = 24;
    const int GlyphHeight = 32;
    const double AcceptScore = 0.55;
    const double AmbiguityMargin = 0.08;

    readonly Dictionary<int, Mat> _templates = new();

    DigitTemplateReader(Dictionary<int, Mat> templates)
    {
        foreach (var (digit, template) in templates)
        {
            _templates[digit] = template;
        }
    }

    public static DigitTemplateReader LoadFrom(string directory)
    {
        var templates = new Dictionary<int, Mat>();
        if (Directory.Exists(directory))
        {
            for (var digit = 0; digit <= 9; digit++)
            {
                var path = Path.Combine(directory, $"{digit}.png");
                if (!File.Exists(path))
                {
                    continue;
                }
                var template = Cv2.ImRead(path, ImreadModes.Grayscale);
                if (!template.Empty())
                {
                    templates[digit] = template;
                }
            }
        }
        return new DigitTemplateReader(templates);
    }

    public bool IsEmpty => _templates.Count < 10;

    public NumberReading ReadCircle(Mat frameBgra, PixelRect circle)
    {
        if (IsEmpty)
        {
            return NumberReading.Unknown;
        }
        var x = Math.Clamp(circle.X, 0, Math.Max(0, frameBgra.Width - 2));
        var y = Math.Clamp(circle.Y, 0, Math.Max(0, frameBgra.Height - 2));
        var width = Math.Clamp(circle.Width, 2, frameBgra.Width - x);
        var height = Math.Clamp(circle.Height, 2, frameBgra.Height - y);
        using var view = frameBgra[new Rect(x, y, width, height)];
        using var bgr = new Mat();
        Cv2.CvtColor(view, bgr, ColorConversionCodes.BGRA2BGR);
        if (height < 52)
        {
            Cv2.Resize(bgr, bgr, new Size(width * 2, height * 2), interpolation: InterpolationFlags.Cubic);
            height *= 2;
        }
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 175), new Scalar(180, 110, 255), mask);
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var all = contours.Select(Cv2.BoundingRect).ToList();
        var strict = all
            .Where(rect => rect.Height >= height / 3 && rect.Height <= height && rect.Width >= 3)
            .OrderBy(rect => rect.X)
            .ToList();
        var reading = ReadBoxes(mask, strict);
        if (reading.Value is not null)
        {
            return reading;
        }
        var loose = all
            .Where(rect => rect.Height >= height / 4 && rect.Height <= height && rect.Width >= 3)
            .ToList();
        if (loose.Count == 0)
        {
            return NumberReading.Unknown;
        }
        var tallest = loose.Max(rect => rect.Height);
        loose = loose
            .Where(rect => rect.Height >= tallest * 0.72)
            .OrderBy(rect => rect.X)
            .ToList();
        return ReadBoxes(mask, loose);
    }

    NumberReading ReadBoxes(Mat mask, List<Rect> boxes)
    {
        if (boxes.Count is < 1 or > 3)
        {
            return NumberReading.Unknown;
        }
        var digits = new List<int>();
        var totalScore = 0.0;
        foreach (var box in boxes)
        {
            using var glyph = mask[box];
            using var normalized = new Mat();
            Cv2.Resize(glyph, normalized, new Size(GlyphWidth, GlyphHeight), interpolation: InterpolationFlags.Cubic);
            Cv2.Threshold(normalized, normalized, 127, 255, ThresholdTypes.Binary);
            var (digit, score) = MatchDigit(normalized);
            if (digit < 0)
            {
                return NumberReading.Unknown;
            }
            digits.Add(digit);
            totalScore += score;
        }
        var value = digits.Aggregate(0, (accumulated, digit) => accumulated * 10 + digit);
        return new NumberReading(value, Math.Min(0.98, totalScore / digits.Count), $"template:{value}");
    }

    (int Digit, double Score) MatchDigit(Mat normalized)
    {
        var bestDigit = -1;
        var bestScore = double.MinValue;
        var secondScore = double.MinValue;
        foreach (var (digit, template) in _templates)
        {
            using var result = new Mat();
            Cv2.MatchTemplate(normalized, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double score, out _, out _);
            if (score > bestScore)
            {
                secondScore = bestScore;
                bestScore = score;
                bestDigit = digit;
            }
            else if (score > secondScore)
            {
                secondScore = score;
            }
        }
        if (bestScore < AcceptScore || bestScore - secondScore < AmbiguityMargin)
        {
            return (-1, 0);
        }
        return (bestDigit, bestScore);
    }

    public void Dispose()
    {
        foreach (var template in _templates.Values)
        {
            template.Dispose();
        }
        _templates.Clear();
    }
}
