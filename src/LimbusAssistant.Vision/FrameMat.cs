using System.Runtime.InteropServices;
using OpenCvSharp;

namespace Tsundosika.LimbusAssistant.Vision;

public static class FrameMat
{
    public static Mat ToMat(CaptureFrame frame)
    {
        var mat = new Mat(frame.Height, frame.Width, MatType.CV_8UC4);
        Marshal.Copy(frame.PixelsBgra, 0, mat.Data, frame.PixelsBgra.Length);
        return mat;
    }

    public static Mat CropGray(Mat bgra, PixelRect rect)
    {
        using var view = bgra[new Rect(rect.X, rect.Y, rect.Width, rect.Height)];
        var gray = new Mat();
        Cv2.CvtColor(view, gray, ColorConversionCodes.BGRA2GRAY);
        return gray;
    }
}
