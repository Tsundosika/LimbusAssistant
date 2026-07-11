using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using OpenCvSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Tsundosika.LimbusAssistant.Vision;

public sealed class WindowsNumberReader : INumberReader
{
    const int UpscaleFactor = 3;

    readonly OcrEngine? _engine = OcrEngine.TryCreateFromUserProfileLanguages();

    public async Task<NumberReading> ReadAsync(CaptureFrame frame, PixelRect region)
    {
        if (_engine is null)
        {
            return NumberReading.Unknown;
        }
        var (pixels, width, height) = Preprocess(frame, region);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            width,
            height,
            BitmapAlphaMode.Ignore);
        var result = await _engine.RecognizeAsync(bitmap);
        return Interpret(result.Text);
    }

    static (byte[] Pixels, int Width, int Height) Preprocess(CaptureFrame frame, PixelRect region)
    {
        using var mat = FrameMat.ToMat(frame);
        using var gray = FrameMat.CropGray(mat, region);
        using var scaled = new Mat();
        Cv2.Resize(
            gray,
            scaled,
            new OpenCvSharp.Size(gray.Width * UpscaleFactor, gray.Height * UpscaleFactor),
            interpolation: InterpolationFlags.Cubic);
        using var binary = new Mat();
        Cv2.Threshold(scaled, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        if (Cv2.Mean(binary).Val0 < 128)
        {
            Cv2.BitwiseNot(binary, binary);
        }
        using var bgra = new Mat();
        Cv2.CvtColor(binary, bgra, ColorConversionCodes.GRAY2BGRA);
        var pixels = new byte[bgra.Width * bgra.Height * 4];
        Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);
        return (pixels, bgra.Width, bgra.Height);
    }

    static NumberReading Interpret(string rawText)
    {
        var filtered = new string(rawText.Where(c => char.IsAsciiDigit(c) || c == '-').ToArray());
        if (filtered.Length == 0 || !int.TryParse(filtered, out var value))
        {
            return new NumberReading(null, 0, rawText);
        }
        var meaningful = rawText.Count(c => !char.IsWhiteSpace(c));
        var confidence = meaningful == 0 ? 0 : 0.95 * filtered.Length / meaningful;
        return new NumberReading(value, confidence, rawText);
    }
}
