using PriceImpactSimulator.Domain;

namespace PriceImpactSimulator.StrategyApi;

/// <summary>Instruction returned by a strategy, to be consumed by the engine.</summary>
public sealed record OrderCommand
(
    CommandType Type,
    Guid        OrderId,
    Side        Side     = Side.Buy,
    decimal     Price    = 0m,
    int         Quantity = 0
);