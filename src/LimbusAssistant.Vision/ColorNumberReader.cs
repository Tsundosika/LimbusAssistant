using OpenCvSharp;

namespace Tsundosika.LimbusAssistant.Vision;

public static class ColorNumberReader
{
    public static Mat ExtractNumberPixels(Mat bgrRegion)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(bgrRegion, hsv, ColorConversionCodes.BGR2HSV);

        using var whiteMask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 180), new Scalar(180, 60, 255), whiteMask);

        using var yellowMask = new Mat();
        Cv2.InRange(hsv, new Scalar(15, 80, 160), new Scalar(40, 255, 255), yellowMask);

        using var redMask1 = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 80, 140), new Scalar(10, 255, 255), redMask1);
        using var redMask2 = new Mat();
        Cv2.InRange(hsv, new Scalar(170, 80, 140), new Scalar(180, 255, 255), redMask2);
        using var redMask = new Mat();
        Cv2.BitwiseOr(redMask1, redMask2, redMask);

        using var combined = new Mat();
        Cv2.BitwiseOr(whiteMask, yellowMask, combined);
        Cv2.BitwiseOr(combined, redMask, combined);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
        Cv2.Dilate(combined, combined, kernel, iterations: 1);
        Cv2.Erode(combined, combined, kernel, iterations: 1);

        return combined.Clone();
    }

    public static Mat BuildHighContrastBinary(Mat bgrRegion, int upscale = 4)
    {
        using var colorMask = ExtractNumberPixels(bgrRegion);
        using var scaled = new Mat();
        Cv2.Resize(colorMask, scaled,
            new Size(colorMask.Width * upscale, colorMask.Height * upscale),
            interpolation: InterpolationFlags.Cubic);
        using var cleaned = new Mat();
        Cv2.Threshold(scaled, cleaned, 127, 255, ThresholdTypes.Binary);

        var border = upscale * 2;
        var padded = new Mat(
            cleaned.Rows + border * 2,
            cleaned.Cols + border * 2,
            MatType.CV_8UC1,
            new Scalar(0));
        cleaned.CopyTo(padded[new Rect(border, border, cleaned.Cols, cleaned.Rows)]);
        return padded;
    }
}
