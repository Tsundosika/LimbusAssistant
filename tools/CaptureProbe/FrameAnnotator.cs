using OpenCvSharp;
using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant.CaptureProbe;

public static class FrameAnnotator
{
    public static Mat Annotate(Mat frameBgra, CalibrationProfile profile, PixelRect content)
    {
        var annotated = new Mat();
        Cv2.CvtColor(frameBgra, annotated, ColorConversionCodes.BGRA2BGR);
        DrawRect(annotated, content, new Scalar(0, 200, 0), "content");
        DrawRect(annotated, ClashGate.SampleBand.ToPixelsWithin(content), new Scalar(0, 220, 220), "gate");
        foreach (var region in profile.Regions)
        {
            var rect = region.Rect.ToPixelsWithin(content);
            var color = region.Kind == RegionKind.Number ? new Scalar(0, 0, 255) : new Scalar(255, 120, 0);
            DrawRect(annotated, rect, color, region.Name);
        }
        return annotated;
    }

    static void DrawRect(Mat image, PixelRect rect, Scalar color, string label)
    {
        Cv2.Rectangle(image, new Rect(rect.X, rect.Y, rect.Width, rect.Height), color, 2);
        Cv2.PutText(
            image,
            label,
            new Point(rect.X, Math.Max(12, rect.Y - 4)),
            HersheyFonts.HersheySimplex,
            0.45,
            color,
            1,
            LineTypes.AntiAlias);
    }
}
