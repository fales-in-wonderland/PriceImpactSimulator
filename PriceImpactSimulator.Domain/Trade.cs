namespace PriceImpactSimulator.Domain;

public sealed record Trade
(
    DateTime Timestamp,
    Side     AggressorSide,
    decimal  Price,
    int      Quantity
);