using System.Text;
using OpenCvSharp;
using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant.CaptureProbe;

public sealed class ProbeSession(ProbeOptions options)
{
    const double PassConfidence = 0.8;

    readonly WindowsNumberReader _reader = new();

    public async Task<int> RunAsync()
    {
        if (options.InputFile is not null)
        {
            return await RunFileAsync(options.InputFile);
        }
        var window = options.WindowTitle is null
            ? GameWindowLocator.FindAuto()
            : GameWindowLocator.FindByTitle(options.WindowTitle);
        if (window is null)
        {
            Console.WriteLine("No game window found. Is the game running and not minimized?");
            return 1;
        }
        var profile = LoadProfile();
        Directory.CreateDirectory(options.OutputDirectory);
        Console.WriteLine($"window {window.ClientBounds.Width}x{window.ClientBounds.Height} at ({window.ClientBounds.X},{window.ClientBounds.Y})");
        Console.WriteLine($"output {Path.GetFullPath(options.OutputDirectory)}");

        using var source = FrameSourceFactory.Create(window.Handle);
        var session = new StringBuilder();
        var frameCount = options.Watch
            ? Math.Max(1, options.Seconds * 1000 / options.IntervalMilliseconds)
            : 1;
        var streak = 0;
        var bestStreak = 0;
        for (var index = 0; index < frameCount; index++)
        {
            var frame = await CaptureWithRetryAsync(source);
            if (frame is null)
            {
                Console.WriteLine($"frame {index:D3}: no capture");
                session.AppendLine($"frame {index:D3}: no capture");
                streak = 0;
                continue;
            }
            frame = FrameCropper.CropToClient(frame, window.Handle);
            var passed = await ProcessFrameAsync(frame, profile, index, session);
            streak = passed ? streak + 1 : 0;
            bestStreak = Math.Max(bestStreak, streak);
            if (options.Watch && index < frameCount - 1)
            {
                await Task.Delay(options.IntervalMilliseconds);
            }
        }
        session.AppendLine($"best consecutive PASS streak: {bestStreak}");
        File.WriteAllText(Path.Combine(options.OutputDirectory, "session.txt"), session.ToString());
        Console.WriteLine($"best consecutive PASS streak: {bestStreak}");
        return 0;
    }

    async Task<int> RunFileAsync(string path)
    {
        using var bgr = Cv2.ImRead(path, ImreadModes.Color);
        if (bgr.Empty())
        {
            Console.WriteLine($"could not read image: {path}");
            return 1;
        }
        using var bgra = new Mat();
        Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);
        var pixels = new byte[bgra.Width * bgra.Height * 4];
        System.Runtime.InteropServices.Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);
        var frame = new CaptureFrame(pixels, bgra.Width, bgra.Height);
        Directory.CreateDirectory(options.OutputDirectory);
        var session = new StringBuilder();
        await ProcessFrameAsync(frame, LoadProfile(), 0, session);
        File.WriteAllText(Path.Combine(options.OutputDirectory, "session.txt"), session.ToString());
        return 0;
    }

    CalibrationProfile LoadProfile()
    {
        if (options.ProfilePath is null)
        {
            return CalibrationProfile.Default;
        }
        return CalibrationStore.Load(options.ProfilePath);
    }

    static async Task<CaptureFrame?> CaptureWithRetryAsync(IFrameSource source)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var frame = source.TryCapture();
            if (frame is not null)
            {
                return frame;
            }
            await Task.Delay(50);
        }
        return null;
    }

    async Task<bool> ProcessFrameAsync(CaptureFrame frame, CalibrationProfile profile, int index, StringBuilder session)
    {
        var content = LetterboxDetector.DetectContent(frame);
        var gateSignal = ClashGate.MeasureSignal(frame, content);
        using var mat = FrameMat.ToMat(frame);

        Cv2.ImWrite(FramePath(index, ".png"), mat);
        using var annotated = FrameAnnotator.Annotate(mat, profile, content);
        Cv2.ImWrite(FramePath(index, "_annotated.png"), annotated);

        var regionDirectory = FramePath(index, "_regions");
        Directory.CreateDirectory(regionDirectory);
        var report = new StringBuilder();
        report.AppendLine($"frame {index:D3} · {frame.Width}x{frame.Height}");
        report.AppendLine($"content rect: {content.X},{content.Y} {content.Width}x{content.Height}");
        report.AppendLine($"clash gate signal: {gateSignal:F4} (threshold {ClashGate.DefaultThreshold:F4})");
        var planningSignals = PlanningIndicator.MeasureBands(frame, content);
        report.AppendLine(
            $"planning phase visible: {PlanningIndicator.IsPlanningVisible(frame, content)}" +
            $" (bands {string.Join(", ", planningSignals.Select(signal => signal.ToString("F4")))})");
        report.AppendLine();

        var readableNumbers = 0;
        var numberRegions = 0;
        foreach (var region in profile.Regions)
        {
            var rect = region.Rect.ToPixelsWithin(content);
            SaveCrop(mat, rect, Path.Combine(regionDirectory, $"{region.Name}.png"));
            if (region.Kind == RegionKind.Text)
            {
                if (!options.NoOcr)
                {
                    if (region.Name == RegionNames.DragSkillName)
                    {
                        var candidates = RibbonScanner.FindSkillRibbons(mat, content);
                        report.AppendLine($"{region.Name}  ribbon candidates: {candidates.Count}");
                        foreach (var candidate in candidates)
                        {
                            var candidateText = await _reader.ReadTextAsync(mat, candidate);
                            report.AppendLine(
                                $"   candidate {candidate.X},{candidate.Y} {candidate.Width}x{candidate.Height}" +
                                $"  text \"{candidateText.Text}\" conf {candidateText.Confidence:F2}");
                        }
                        continue;
                    }
                    var text = await _reader.ReadTextAsync(mat, rect);
                    report.AppendLine(
                        $"{region.Name}  rect {rect.X},{rect.Y} {rect.Width}x{rect.Height}  text \"{text.Text}\" conf {text.Confidence:F2}");
                }
                continue;
            }
            if (region.Kind != RegionKind.Number)
            {
                report.AppendLine($"{region.Name}  rect {rect.X},{rect.Y} {rect.Width}x{rect.Height}  (icon, no ocr)");
                continue;
            }
            numberRegions++;
            report.AppendLine($"{region.Name}  rect {rect.X},{rect.Y} {rect.Width}x{rect.Height}");
            if (options.NoOcr)
            {
                continue;
            }
            var results = await _reader.ReadAllStrategiesAsync(
                mat,
                rect,
                options.Stages
                    ? (stage, image) => Cv2.ImWrite(Path.Combine(regionDirectory, $"{region.Name}_{stage}.png"), image)
                    : null);
            var bestConfidence = 0.0;
            foreach (var result in results)
            {
                report.AppendLine(
                    $"   {result.Strategy,-9} value {result.Reading.Value?.ToString() ?? "?",5}" +
                    $"  conf {result.Reading.Confidence:F2}  raw \"{result.Reading.RawText.Trim()}\"");
                if (result.Reading.Value is not null)
                {
                    bestConfidence = Math.Max(bestConfidence, result.Reading.Confidence);
                }
            }
            if (bestConfidence >= PassConfidence)
            {
                readableNumbers++;
            }
        }
        if (!options.NoOcr)
        {
            report.AppendLine();
            if (HighlightScanner.FindHighlightedUnit(mat, content) is { } highlight)
            {
                report.AppendLine($"highlighted unit: {highlight.X},{highlight.Y} {highlight.Width}x{highlight.Height}");
                var fieldCircles = DockScanner.FindSanityCircles(mat, content, DockScanner.FieldBand);
                report.AppendLine($"field sanity circles: {fieldCircles.Count}");
                foreach (var circle in fieldCircles)
                {
                    var fieldReading = await _reader.ReadAsync(mat, circle);
                    report.AppendLine(
                        $"   field circle {circle.X},{circle.Y} {circle.Width}x{circle.Height}" +
                        $"  value {fieldReading.Value?.ToString() ?? "?"}  conf {fieldReading.Confidence:F2}");
                }
            }
            else
            {
                report.AppendLine("highlighted unit: none");
            }
            var circles = DockScanner.FindSanityCircles(mat, content);
            report.AppendLine($"dock sanity circles found: {circles.Count}");
            foreach (var circle in circles)
            {
                var reading = await _reader.ReadAsync(mat, circle);
                report.AppendLine(
                    $"   circle {circle.X},{circle.Y} {circle.Width}x{circle.Height}" +
                    $"  value {reading.Value?.ToString() ?? "?"}  conf {reading.Confidence:F2}");
            }
        }

        var passed = !options.NoOcr && numberRegions > 0 && readableNumbers == numberRegions;
        report.AppendLine();
        report.AppendLine($"{(passed ? "PASS" : "FAIL")}: {readableNumbers}/{numberRegions} numbers at conf>={PassConfidence:F1}");
        File.WriteAllText(FramePath(index, "_ocr.txt"), report.ToString());

        var line = $"frame {index:D3}: gate {gateSignal:F4} · numbers {readableNumbers}/{numberRegions} · {(passed ? "PASS" : "FAIL")}";
        Console.WriteLine(line);
        session.AppendLine(line);
        return passed;
    }

    static void SaveCrop(Mat mat, PixelRect rect, string path)
    {
        var x = Math.Clamp(rect.X, 0, Math.Max(0, mat.Width - 1));
        var y = Math.Clamp(rect.Y, 0, Math.Max(0, mat.Height - 1));
        var width = Math.Clamp(rect.Width, 1, mat.Width - x);
        var height = Math.Clamp(rect.Height, 1, mat.Height - y);
        using var crop = mat[new Rect(x, y, width, height)];
        Cv2.ImWrite(path, crop);
    }

    string FramePath(int index, string suffix) =>
        Path.Combine(options.OutputDirectory, $"frame_{index:D3}{suffix}");
}
