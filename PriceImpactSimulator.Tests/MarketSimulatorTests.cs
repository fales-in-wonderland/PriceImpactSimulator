using PriceImpactSimulator.Domain;
using PriceImpactSimulator.Engine;
using Xunit;

namespace PriceImpactSimulator.Tests;

public class MarketSimulatorTests
{
    [Fact]
    public void Mid_Changes_When_Market_Buy()
    {
        var book = new OrderBook();
        var p =  new MarketSimulator.SimParams(
                TickSize:      0.01m,
                StartMidPrice: 20.00m,
                CancelProb:    0.005,
                TrendLookback: 10,
                PriceLookback: 10,
                K1Imbalance:   0.35,
                K2Trend:       0.25,
                K3PriceDev:    0.15,
                LambdaDepth:   0.2,
                Q0:            1000,
                LogNormMu:     7,
                LogNormSigma:  1.1,
                Seed:          42);

        var sim = new MarketSimulator(book, p);
        var ts = DateTime.UtcNow;

        for (int d = 0; d < 5; d++)
        {
            book.AddLimit(new Order(Guid.NewGuid(), ts, Side.Sell, 20.01m + d * 0.01m, 1000, OrderType.Limit, null));
            book.AddLimit(new Order(Guid.NewGuid(), ts, Side.Buy, 19.99m - d * 0.01m, 1000, OrderType.Limit, null));
        }

        var midBefore = book.Mid;
        sim.Step(ts);
        Assert.NotEqual(midBefore, book.Mid);
    }
}