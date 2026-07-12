namespace Tsundosika.LimbusAssistant;

public sealed record LiveClashEstimate(
    double WinProbability,
    double FirstExchangeWinProbability,
    double ExpectedAttackPowerOnWin,
    bool FromDataset,
    double Confidence);
