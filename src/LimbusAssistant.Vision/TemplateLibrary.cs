using OpenCvSharp;

namespace Tsundosika.LimbusAssistant.Vision;

public sealed class TemplateLibrary : IDisposable
{
    readonly Dictionary<string, Mat> _templates;

    TemplateLibrary(Dictionary<string, Mat> templates)
    {
        _templates = templates;
    }

    public static TemplateLibrary LoadFrom(string directory)
    {
        var templates = new Dictionary<string, Mat>();
        if (Directory.Exists(directory))
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.png"))
            {
                var template = Cv2.ImRead(path, ImreadModes.Grayscale);
                if (!template.Empty())
                {
                    templates[Path.GetFileNameWithoutExtension(path)] = template;
                }
            }
        }
        return new TemplateLibrary(templates);
    }

    public bool IsEmpty => _templates.Count == 0;

    public IconReading BestMatch(Mat regionGray, double threshold = 0.8)
    {
        string? bestName = null;
        var bestScore = 0.0;
        foreach (var (name, template) in _templates)
        {
            if (template.Width > regionGray.Width || template.Height > regionGray.Height)
            {
                continue;
            }
            using var result = new Mat();
            Cv2.MatchTemplate(regionGray, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double score, out _, out _);
            if (score > bestScore)
            {
                bestScore = score;
                bestName = name;
            }
        }
        return bestScore >= threshold ? new IconReading(bestName, bestScore) : new IconReading(null, bestScore);
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
