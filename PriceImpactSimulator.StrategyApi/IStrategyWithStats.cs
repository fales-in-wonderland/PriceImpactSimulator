namespace PriceImpactSimulator.StrategyApi;

public interface IStrategyWithStats : IStrategy
{
    StrategyMetrics Metrics { get; }
}
