using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant;

public sealed record AdvisorSnapshot(
    CaptureStatus Status,
    WindowBounds? GameBounds,
    VisionReading Reading,
    LiveClashEstimate? LiveClash,
    PlanningHint? Planning,
    bool ClashGateOpen,
    double Confidence,
    CaptureFrame? Frame,
    TickMetrics Metrics,
    DateTimeOffset Timestamp)
{
    public static AdvisorSnapshot NotFound() => new(
        CaptureStatus.GameNotFound,
        null,
        VisionReading.Empty,
        null,
        null,
        false,
        0,
        null,
        TickMetrics.Empty,
        DateTimeOffset.Now);
}
