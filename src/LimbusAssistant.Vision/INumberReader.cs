namespace Tsundosika.LimbusAssistant.Vision;

public interface INumberReader
{
    Task<NumberReading> ReadAsync(CaptureFrame frame, PixelRect region);
}
