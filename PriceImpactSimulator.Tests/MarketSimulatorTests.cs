using PriceImpactSimulator.Domain;
using PriceImpactSimulator.Engine;
using Xunit;

namespace PriceImpactSimulator.Tests;

public class MarketSimulatorTests
{
    [Fact]
    public void Snapshot_Is_Sorted()
    {
        var book = new OrderBook();
        var ts = DateTime.UtcNow;

        book.AddLimit(new Order(Guid.NewGuid(), ts, Side.Buy, 19.95m, 100, OrderType.Limit, null), ts);
        book.AddLimit(new Order(Guid.NewGuid(), ts, Side.Buy, 19.97m, 100, OrderType.Limit, null), ts);
        book.AddLimit(new Order(Guid.NewGuid(), ts, Side.Buy, 19.96m, 100, OrderType.Limit, null), ts);

        book.AddLimit(new Order(Guid.NewGuid(), ts, Side.Sell, 20.05m, 100, OrderType.Limit, null), ts);
        book.AddLimit(new Order(Guid.NewGuid(), ts, Side.Sell, 20.02m, 100, OrderType.Limit, null), ts);
        book.AddLimit(new Order(Guid.NewGuid(), ts, Side.Sell, 20.03m, 100, OrderType.Limit, null), ts);

        var snap = book.Snapshot(ts, 3);

        Assert.Equal(new[] { 19.97m, 19.96m, 19.95m }, snap.Bids.Select(b => b.Price));
        Assert.Equal(new[] { 20.02m, 20.03m, 20.05m }, snap.Asks.Select(a => a.Price));
    }
}