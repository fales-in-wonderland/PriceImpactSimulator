// File: OrderBookSnapshot.cs
namespace PriceImpactSimulator.Domain;

/// <summary>Compact depth view used by strategies (best N levels each side).</summary>
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
    public required OrderBookLevel[] Bids { get; init; }   // descending price
    public required OrderBookLevel[] Asks { get; init; }   // ascending price
}