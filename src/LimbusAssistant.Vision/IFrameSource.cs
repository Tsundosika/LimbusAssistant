namespace Tsundosika.LimbusAssistant.Vision;

public interface IFrameSource : IDisposable
{
    CaptureFrame? TryCapture();
}
