using PriceImpactSimulator.StrategyApi;

namespace PriceImpactSimulator.Host;

public sealed record StrategyWindow(
    IStrategy Strategy,
    double    OffsetSec,  
    double    DurationSec 
);