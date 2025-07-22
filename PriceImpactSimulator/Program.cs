using System;
using PriceImpactSimulator.Host;
using PriceImpactSimulator.StrategyApi;
using PriceImpactSimulator.Strategies;
using PriceImpactSimulator.Engine;

// simulation tick aligned with README specification
var step = TimeSpan.FromMilliseconds(100);

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
    CancelProb:    0.01,
    TrendLookback: 10,
    PriceLookback: 10,
    K1Imbalance:   0.60,
    K2Trend:       0.50,
    K3PriceDev:    0.15,
    LambdaDepth:   0.15,
    Q0:            2500,
    LogNormMu:     7,
    LogNormSigma:  1.1,
    Seed:          42);


var strategy = new NoOpStrategy(); 
var runner   = new SimulationRunner(strategy, simParams, ctx, logFolder: "logs");

runner.Run(TimeSpan.FromMinutes(1));

Console.WriteLine("Simulation finished. CSV logs are in ./logs");