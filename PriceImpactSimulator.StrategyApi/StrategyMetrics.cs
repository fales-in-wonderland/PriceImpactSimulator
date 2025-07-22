namespace PriceImpactSimulator.StrategyApi;

public readonly record struct StrategyMetrics(
    decimal BuyingPowerUsed,
    int     Position,
    decimal Vwap,
    decimal PnL,
    decimal RealisedPnL
);
