
namespace PriceImpactSimulator.Domain;

// Compact depth view for strategies
public readonly struct OrderBookLevel
{
    public OrderBookLevel(decimal price, int qty)
    { Price = price; Quantity = qty; }
    public decimal Price  { get; }
    public int     Quantity { get; }
}

public sealed class OrderBookSnapshot
{
    public required DateTime Timestamp { get; init; }
    public required OrderBookLevel[] Bids { get; init; }
    public required OrderBookLevel[] Asks { get; init; }
}