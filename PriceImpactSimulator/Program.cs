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
    TrendLookback: 100,
    PriceLookback: 2,
    K1Imbalance: 0.15,
    K2Trend    : 0.25,
    K3PriceDev : 1.80,
    LambdaDepth:   0.15,
    Q0:            2500,
    LogNormMu:     7,
    LogNormSigma:  1.1,
    Seed:          37);
/*    TrendLookback: 150,
    PriceLookback: 5,
    K1Imbalance: 0.18,
    K2Trend    : 0.65,
    K3PriceDev : 0.70,*/
var ladder = new LadderLiftStrategy();
var drip   = new DripFlipStrategy();

//var schedule = new[]
//{
//    new StrategyWindow(ladder,  30, 10),
//    new StrategyWindow(drip,   10, 10),
//    new StrategyWindow(ladder, 49, 10),
//    new StrategyWindow(drip , 49, 10),
//};

var schedule = new[]
{
    new StrategyWindow(drip,  20, 20),
    new StrategyWindow(ladder,80, 20),
    new StrategyWindow(ladder, 140, 20),
    new StrategyWindow(drip , 140, 20),    
};


var scheduler = new Scheduler(schedule);

var runner = new SimulationRunner(
    scheduler,
    simParams, ctx, "logs");

runner.Run(TimeSpan.FromMinutes(3));

Console.WriteLine("Simulation finished. CSV logs are in ./logs");
