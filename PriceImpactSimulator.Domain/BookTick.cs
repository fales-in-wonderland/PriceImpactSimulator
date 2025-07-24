
namespace PriceImpactSimulator.Domain;

public sealed record BookTick
(
    DateTime Timestamp,
    decimal  MidPrice,
    int      OutstandingOrders
);