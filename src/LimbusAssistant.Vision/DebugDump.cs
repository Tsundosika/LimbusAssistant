using OpenCvSharp;

namespace Tsundosika.LimbusAssistant.Vision;

public static class DebugDump
{
    public static void SaveFrame(CaptureFrame frame, string path)
    {
        using var mat = FrameMat.ToMat(frame);
        Cv2.ImWrite(path, mat);
    }
}
