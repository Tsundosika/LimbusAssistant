namespace Tsundosika.LimbusAssistant;

public sealed record LiveClashEstimate(
    double WinProbability,
    double ExpectedAttackPowerOnWin,
    bool FromDataset,
    double Confidence);
