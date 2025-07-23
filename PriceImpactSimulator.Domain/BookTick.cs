// book mid-price tick
namespace PriceImpactSimulator.Domain;

public sealed record BookTick
(
    DateTime Timestamp,
    decimal  MidPrice,
    int      OutstandingOrders
);