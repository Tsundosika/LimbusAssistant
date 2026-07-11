using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant;

public sealed record AdvisorSnapshot(
    CaptureStatus Status,
    WindowBounds? GameBounds,
    VisionReading Reading,
    LiveClashEstimate? LiveClash,
    double Confidence,
    CaptureFrame? Frame,
    DateTimeOffset Timestamp)
{
    public static AdvisorSnapshot NotFound() => new(
        CaptureStatus.GameNotFound,
        null,
        VisionReading.Empty,
        null,
        0,
        null,
        DateTimeOffset.Now);
}
