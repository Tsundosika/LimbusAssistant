using OpenCvSharp;

namespace Tsundosika.LimbusAssistant.Vision;

public interface INumberReader
{
    Task<NumberReading> ReadAsync(Mat frameBgra, PixelRect region);

    Task<TextReading> ReadTextAsync(Mat frameBgra, PixelRect region);

    int ConsumeOcrCallCount();
}
