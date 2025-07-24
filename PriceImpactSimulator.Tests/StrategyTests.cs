using System;
using System.Collections.Generic;
using PriceImpactSimulator.Domain;
using PriceImpactSimulator.Strategies;
using PriceImpactSimulator.StrategyApi;
using Xunit;

namespace PriceImpactSimulator.Tests;

public class StrategyTests
{
    [Fact]
    public void DripFlip_Sells_When_Target_Met()
    {
        var ctx = new StrategyContext
        {
            TickSize = 0.01m,
            CapitalLimit = 1000m,
            SimulationStep = TimeSpan.FromSeconds(1),
            Logger = _ => {}
        };
        var strat = new DripFlipStrategy();
        strat.Initialize(ctx);

        var rep = new ExecutionReport(Guid.NewGuid(), ExecType.Trade, Side.Buy,
            20.00m, 1, 0, DateTime.UtcNow);
        strat.OnExecution(rep);

        var snap = new OrderBookSnapshot
        {
            Timestamp = DateTime.UtcNow,
            Bids = new[] { new OrderBookLevel(20.20m, 100) },
            Asks = new[] { new OrderBookLevel(20.21m, 100) }
        };
        strat.OnOrderBook(snap);

        var cmds = strat.GenerateCommands(DateTime.UtcNow);
        Assert.Single(cmds);
        Assert.Equal(Side.Sell, cmds[0].Side);
    }

    [Fact]
    public void LadderLift_Places_Ladder_On_First_Bid()
    {
        var ctx = new StrategyContext
        {
            TickSize = 0.01m,
            CapitalLimit = 1000m,
            SimulationStep = TimeSpan.FromSeconds(1),
            Logger = _ => {}
        };
        var strat = new LadderLiftStrategy();
        strat.Initialize(ctx);

        var snap = new OrderBookSnapshot
        {
            Timestamp = DateTime.UtcNow,
            Bids = new[] { new OrderBookLevel(19.99m, 100) },
            Asks = new[] { new OrderBookLevel(20.01m, 100) }
        };
        strat.OnOrderBook(snap);

        var cmds = strat.GenerateCommands(DateTime.UtcNow);
        Assert.NotEmpty(cmds);
    }
}
