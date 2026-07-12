using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using OpenCvSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Tsundosika.LimbusAssistant.Vision;

public sealed class WindowsNumberReader : INumberReader
{
    const int UpscaleFactor = 4;

    readonly OcrEngine? _engine = OcrEngine.TryCreateFromUserProfileLanguages();

    public async Task<NumberReading> ReadAsync(CaptureFrame frame, PixelRect region)
    {
        if (_engine is null)
        {
            return NumberReading.Unknown;
        }
        using var mat = FrameMat.ToMat(frame);
        var clamped = ClampRegion(region, frame.Width, frame.Height);
        if (clamped.Width < 2 || clamped.Height < 2)
        {
            return NumberReading.Unknown;
        }
        using var bgrRoi = mat[new Rect(clamped.X, clamped.Y, clamped.Width, clamped.Height)];

        var best = NumberReading.Unknown;

        using var colorBinary = ColorNumberReader.BuildHighContrastBinary(bgrRoi, UpscaleFactor);
        var colorReading = await RecognizeAsync(colorBinary);
        if (colorReading.Confidence > best.Confidence)
        {
            best = colorReading;
        }

        if (best.Value is not null && best.Confidence >= 0.85)
        {
            return best;
        }

        using var gray = new Mat();
        Cv2.CvtColor(bgrRoi, gray, ColorConversionCodes.BGR2GRAY);
        using var scaled = new Mat();
        Cv2.Resize(gray, scaled,
            new OpenCvSharp.Size(gray.Width * UpscaleFactor, gray.Height * UpscaleFactor),
            interpolation: InterpolationFlags.Cubic);

        using var otsu = BuildOtsuBinary(scaled);
        var otsuReading = await RecognizeAsync(otsu);
        if (otsuReading.Confidence > best.Confidence)
        {
            best = otsuReading;
        }

        if (best.Value is not null && best.Confidence >= 0.8)
        {
            return best;
        }

        using var enhanced = BuildEnhancedGrayscale(scaled);
        var enhancedReading = await RecognizeAsync(enhanced);
        if (enhancedReading.Confidence > best.Confidence)
        {
            best = enhancedReading;
        }

        using var adaptive = BuildAdaptiveBinary(scaled);
        var adaptiveReading = await RecognizeAsync(adaptive);
        if (adaptiveReading.Confidence > best.Confidence)
        {
            best = adaptiveReading;
        }

        using var invertedColor = new Mat();
        Cv2.BitwiseNot(colorBinary, invertedColor);
        var invertedReading = await RecognizeAsync(invertedColor);
        if (invertedReading.Confidence > best.Confidence)
        {
            best = invertedReading;
        }

        return best;
    }

    async Task<NumberReading> RecognizeAsync(Mat image)
    {
        using var bgra = new Mat();
        if (image.Channels() == 1)
        {
            Cv2.CvtColor(image, bgra, ColorConversionCodes.GRAY2BGRA);
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

    static Mat BuildOtsuBinary(Mat scaled)
    {
        var binary = new Mat();
        Cv2.Threshold(scaled, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        if (Cv2.Mean(binary).Val0 < 128)
        {
            Cv2.BitwiseNot(binary, binary);
        }
        AddPadding(binary);
        return binary;
    }

    static Mat BuildEnhancedGrayscale(Mat scaled)
    {
        var enhanced = new Mat();
        using var clahe = Cv2.CreateCLAHE(2.0, new OpenCvSharp.Size(8, 8));
        clahe.Apply(scaled, enhanced);
        Cv2.GaussianBlur(enhanced, enhanced, new OpenCvSharp.Size(3, 3), 0);
        AddPadding(enhanced);
        return enhanced;
    }

    static Mat BuildAdaptiveBinary(Mat scaled)
    {
        var adaptive = new Mat();
        Cv2.AdaptiveThreshold(
            scaled,
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
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(2, 2));
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
