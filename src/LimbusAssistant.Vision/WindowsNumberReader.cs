using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using OpenCvSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Tsundosika.LimbusAssistant.Vision;

public sealed class WindowsNumberReader : INumberReader
{
    const int UpscaleFactor = 4;
    const double EarlyExitConfidence = 0.8;

    readonly OcrEngine? _engine = OcrEngine.TryCreateFromUserProfileLanguages();
    int _ocrCalls;

    public int ConsumeOcrCallCount() => Interlocked.Exchange(ref _ocrCalls, 0);

    public async Task<NumberReading> ReadAsync(Mat frameBgra, PixelRect region)
    {
        if (_engine is null)
        {
            return NumberReading.Unknown;
        }
        var clamped = ClampRegion(region, frameBgra.Width, frameBgra.Height);
        if (clamped.Width < 2 || clamped.Height < 2)
        {
            return NumberReading.Unknown;
        }
        using var roi = frameBgra[new Rect(clamped.X, clamped.Y, clamped.Width, clamped.Height)];
        using var bgr = ToBgr(roi);

        using var colorBinary = ColorNumberReader.BuildHighContrastBinary(bgr, UpscaleFactor);
        var best = await RecognizeAsync(colorBinary);
        if (best.Value is not null && best.Confidence >= EarlyExitConfidence)
        {
            return best;
        }

        using var scaledGray = BuildScaledGray(roi);
        using var otsu = BuildOtsuBinary(scaledGray);
        var otsuReading = await RecognizeAsync(otsu);
        if (otsuReading.Confidence > best.Confidence)
        {
            best = otsuReading;
        }
        if (best.Value is not null && best.Confidence >= EarlyExitConfidence)
        {
            return best;
        }

        using var enhanced = BuildEnhancedGrayscale(scaledGray);
        var enhancedReading = await RecognizeAsync(enhanced);
        return enhancedReading.Confidence > best.Confidence ? enhancedReading : best;
    }

    public async Task<TextReading> ReadTextAsync(Mat frameBgra, PixelRect region)
    {
        if (_engine is null)
        {
            return TextReading.Empty;
        }
        var clamped = ClampRegion(region, frameBgra.Width, frameBgra.Height);
        if (clamped.Width < 2 || clamped.Height < 2)
        {
            return TextReading.Empty;
        }
        using var roi = frameBgra[new Rect(clamped.X, clamped.Y, clamped.Width, clamped.Height)];

        using var whiteIsolated = BuildWhiteTextOnAnyBackground(roi);
        var isolated = await RecognizeTextAsync(whiteIsolated);
        using var scaledGray = BuildScaledGray(roi);
        var plain = await RecognizeTextAsync(scaledGray);
        var best = Better(isolated, plain);
        if (IsGoodText(best))
        {
            return best;
        }

        using var grayThreshold = new Mat();
        Cv2.Threshold(scaledGray, grayThreshold, 170, 255, ThresholdTypes.Binary);
        Cv2.BitwiseNot(grayThreshold, grayThreshold);
        PadWhite(grayThreshold);
        best = Better(best, await RecognizeTextAsync(grayThreshold));
        if (IsGoodText(best))
        {
            return best;
        }

        using var darkText = new Mat();
        Cv2.Threshold(scaledGray, darkText, 130, 255, ThresholdTypes.Binary);
        PadWhite(darkText);
        best = Better(best, await RecognizeTextAsync(darkText));
        if (IsGoodText(best))
        {
            return best;
        }

        using var deskewed = Rotate(whiteIsolated, 4);
        best = Better(best, await RecognizeTextAsync(deskewed));
        if (IsGoodText(best))
        {
            return best;
        }

        using var enhanced = BuildEnhancedGrayscale(scaledGray);
        return Better(best, await RecognizeTextAsync(enhanced));
    }

    static bool IsGoodText(TextReading reading) =>
        reading.Confidence >= 0.6 && reading.Text.Count(char.IsLetter) >= 3;

    static TextReading Better(TextReading current, TextReading candidate)
    {
        var currentScore = current.Text.Count(char.IsLetter) * Math.Max(0.1, current.Confidence);
        var candidateScore = candidate.Text.Count(char.IsLetter) * Math.Max(0.1, candidate.Confidence);
        return candidateScore > currentScore ? candidate : current;
    }

    static Mat BuildWhiteTextOnAnyBackground(Mat roi)
    {
        using var bgr = ToBgr(roi);
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 175), new Scalar(180, 95, 255), mask);
        using var scaled = new Mat();
        Cv2.Resize(
            mask,
            scaled,
            new Size(mask.Width * UpscaleFactor, mask.Height * UpscaleFactor),
            interpolation: InterpolationFlags.Cubic);
        var binary = new Mat();
        Cv2.Threshold(scaled, binary, 127, 255, ThresholdTypes.Binary);
        Cv2.BitwiseNot(binary, binary);
        PadWhite(binary);
        return binary;
    }

    static void PadWhite(Mat image)
    {
        var border = Math.Max(8, image.Width / 20);
        Cv2.CopyMakeBorder(image, image, border, border, border, border, BorderTypes.Constant, new Scalar(255));
    }

    static Mat Rotate(Mat image, double degrees)
    {
        var center = new Point2f(image.Width / 2f, image.Height / 2f);
        using var rotation = Cv2.GetRotationMatrix2D(center, degrees, 1.0);
        var rotated = new Mat();
        Cv2.WarpAffine(
            image,
            rotated,
            rotation,
            new Size(image.Width, image.Height),
            InterpolationFlags.Cubic,
            BorderTypes.Constant,
            new Scalar(255));
        return rotated;
    }

    async Task<TextReading> RecognizeTextAsync(Mat gray)
    {
        Interlocked.Increment(ref _ocrCalls);
        using var bgra = new Mat();
        Cv2.CvtColor(gray, bgra, ColorConversionCodes.GRAY2BGRA);
        var pixels = new byte[bgra.Width * bgra.Height * 4];
        Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            bgra.Width,
            bgra.Height,
            BitmapAlphaMode.Ignore);
        var result = await _engine!.RecognizeAsync(bitmap);
        var text = result.Text.Trim();
        var letters = text.Count(char.IsLetter);
        var confidence = text.Length == 0 ? 0 : Math.Min(0.95, 0.4 + 0.55 * letters / Math.Max(1, text.Length));
        return new TextReading(text, confidence);
    }

    public async Task<IReadOnlyList<OcrStrategyResult>> ReadAllStrategiesAsync(
        Mat frameBgra,
        PixelRect region,
        Action<string, Mat>? onStage = null)
    {
        if (_engine is null)
        {
            return [];
        }
        var clamped = ClampRegion(region, frameBgra.Width, frameBgra.Height);
        if (clamped.Width < 2 || clamped.Height < 2)
        {
            return [];
        }
        using var roi = frameBgra[new Rect(clamped.X, clamped.Y, clamped.Width, clamped.Height)];
        using var bgr = ToBgr(roi);
        var results = new List<OcrStrategyResult>();

        using var colorBinary = ColorNumberReader.BuildHighContrastBinary(bgr, UpscaleFactor);
        onStage?.Invoke("color", colorBinary);
        results.Add(new OcrStrategyResult("color", await RecognizeAsync(colorBinary)));

        using var scaledGray = BuildScaledGray(roi);
        using var otsu = BuildOtsuBinary(scaledGray);
        onStage?.Invoke("otsu", otsu);
        results.Add(new OcrStrategyResult("otsu", await RecognizeAsync(otsu)));

        using var enhanced = BuildEnhancedGrayscale(scaledGray);
        onStage?.Invoke("clahe", enhanced);
        results.Add(new OcrStrategyResult("clahe", await RecognizeAsync(enhanced)));

        using var adaptive = BuildAdaptiveBinary(scaledGray);
        onStage?.Invoke("adaptive", adaptive);
        results.Add(new OcrStrategyResult("adaptive", await RecognizeAsync(adaptive)));

        using var invertedColor = new Mat();
        Cv2.BitwiseNot(colorBinary, invertedColor);
        onStage?.Invoke("inverted", invertedColor);
        results.Add(new OcrStrategyResult("inverted", await RecognizeAsync(invertedColor)));

        return results;
    }

    async Task<NumberReading> RecognizeAsync(Mat image)
    {
        Interlocked.Increment(ref _ocrCalls);
        using var bgra = new Mat();
        if (image.Channels() == 1)
        {
            Cv2.CvtColor(image, bgra, ColorConversionCodes.GRAY2BGRA);
        }
        else if (image.Channels() == 3)
        {
            Cv2.CvtColor(image, bgra, ColorConversionCodes.BGR2BGRA);
        }
        else
        {
            image.CopyTo(bgra);
        }
        var pixels = new byte[bgra.Width * bgra.Height * 4];
        Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            bgra.Width,
            bgra.Height,
            BitmapAlphaMode.Ignore);
        var result = await _engine!.RecognizeAsync(bitmap);
        return Interpret(result.Text);
    }

    static Mat ToBgr(Mat roi)
    {
        var bgr = new Mat();
        if (roi.Channels() == 4)
        {
            Cv2.CvtColor(roi, bgr, ColorConversionCodes.BGRA2BGR);
        }
        else
        {
            roi.CopyTo(bgr);
        }
        return bgr;
    }

    static Mat BuildScaledGray(Mat roi)
    {
        using var gray = new Mat();
        Cv2.CvtColor(
            roi,
            gray,
            roi.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
        var scaled = new Mat();
        Cv2.Resize(
            gray,
            scaled,
            new Size(gray.Width * UpscaleFactor, gray.Height * UpscaleFactor),
            interpolation: InterpolationFlags.Cubic);
        return scaled;
    }

    static Mat BuildOtsuBinary(Mat scaledGray)
    {
        var binary = new Mat();
        Cv2.Threshold(scaledGray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        if (Cv2.Mean(binary).Val0 < 128)
        {
            Cv2.BitwiseNot(binary, binary);
        }
        AddPadding(binary);
        return binary;
    }

    static Mat BuildEnhancedGrayscale(Mat scaledGray)
    {
        var enhanced = new Mat();
        using var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8));
        clahe.Apply(scaledGray, enhanced);
        Cv2.GaussianBlur(enhanced, enhanced, new Size(3, 3), 0);
        AddPadding(enhanced);
        return enhanced;
    }

    static Mat BuildAdaptiveBinary(Mat scaledGray)
    {
        var adaptive = new Mat();
        Cv2.AdaptiveThreshold(
            scaledGray,
            adaptive,
            255,
            AdaptiveThresholdTypes.GaussianC,
            ThresholdTypes.Binary,
            31,
            4);
        if (Cv2.Mean(adaptive).Val0 < 128)
        {
            Cv2.BitwiseNot(adaptive, adaptive);
        }
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
        Cv2.MorphologyEx(adaptive, adaptive, MorphTypes.Close, kernel);
        AddPadding(adaptive);
        return adaptive;
    }

    static void AddPadding(Mat image)
    {
        var border = Math.Max(4, image.Width / 20);
        Cv2.CopyMakeBorder(image, image, border, border, border, border, BorderTypes.Constant, new Scalar(0));
    }

    static NumberReading Interpret(string rawText)
    {
        var cleaned = rawText.Replace("O", "0").Replace("o", "0")
            .Replace("l", "1").Replace("I", "1")
            .Replace("S", "5").Replace("s", "5")
            .Replace("B", "8").Replace("b", "6")
            .Replace(".", "").Replace(",", "").Replace("'", "").Replace("\"", "")
            .Replace("`", "").Replace("~", "").Replace(":", "").Replace(";", "")
            .Replace("_", "").Replace("|", "");
        var filtered = new string(cleaned.Where(c => char.IsAsciiDigit(c) || c == '-').ToArray());
        if (filtered.Length == 0 || !int.TryParse(filtered, out var value))
        {
            return new NumberReading(null, 0, rawText);
        }
        var meaningful = cleaned.Count(c => !char.IsWhiteSpace(c));
        var confidence = meaningful == 0 ? 0 : 0.95 * filtered.Length / meaningful;
        return new NumberReading(value, confidence, rawText);
    }

    static PixelRect ClampRegion(PixelRect region, int frameWidth, int frameHeight)
    {
        var x = Math.Clamp(region.X, 0, Math.Max(0, frameWidth - 1));
        var y = Math.Clamp(region.Y, 0, Math.Max(0, frameHeight - 1));
        var w = Math.Clamp(region.Width, 1, frameWidth - x);
        var h = Math.Clamp(region.Height, 1, frameHeight - y);
        return new PixelRect(x, y, w, h);
    }
}
