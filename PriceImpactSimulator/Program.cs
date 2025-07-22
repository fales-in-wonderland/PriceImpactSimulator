using System;
using PriceImpactSimulator.Host;
using PriceImpactSimulator.StrategyApi;
using PriceImpactSimulator.Strategies;
using PriceImpactSimulator.Engine;

// simulation tick aligned with README specification
var step = TimeSpan.FromMilliseconds(10);

var ctx = new StrategyContext
{
    TickSize       = 0.01m,
    CapitalLimit   = 30_000_000m,
    SimulationStep = step,
    Logger         = Console.WriteLine
};

var simParams = new MarketSimulator.SimParams(
    TickSize:      0.01m,
    StartMidPrice: 20.00m,
    CancelProb:    0.005,
    TrendLookback: 20,
    PriceLookback: 20,
    K1Imbalance: 0.40,
    K2Trend    : 0.25,
    K3PriceDev : 0.15,
    LambdaDepth:   0.15,
    Q0:            2500,
    LogNormMu:     7,
    LogNormSigma:  1.1,
    Seed:          42);

//var strat1 = new LadderBidStrategy();
var strat2 = new DripAccumThenDumpStrategy();

var runner = new SimulationRunner(strat2, simParams, ctx, "logs");


runner.Run(TimeSpan.FromMinutes(1));

Console.WriteLine("Simulation finished. CSV logs are in ./logs");