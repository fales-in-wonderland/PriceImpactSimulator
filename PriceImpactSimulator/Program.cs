// simulation entry
﻿using System;
using PriceImpactSimulator.Host;
using PriceImpactSimulator.StrategyApi;
using PriceImpactSimulator.Strategies;
using PriceImpactSimulator.Engine;


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
    K2Trend    : 0.30,
    K3PriceDev : 0.30,
    LambdaDepth:   0.15,
    Q0:            2500,
    LogNormMu:     7,
    LogNormSigma:  1.1,
    Seed:          42);

var ladder = new LadderLiftStrategy();
var drip   = new DripFlipStrategy();

var schedule = new[]
{
    new StrategyWindow(ladder,  10, 10),   
    new StrategyWindow(drip,   30, 10),   
    new StrategyWindow(ladder, 50, 10),   
    new StrategyWindow(drip , 50, 10),    
};

var schedule = new[]
{
    new StrategyWindow(ladder,  20, 20),   
    new StrategyWindow(drip,   60, 20),   
    new StrategyWindow(ladder, 100, 20),   
    new StrategyWindow(drip , 100, 20),    
};

var scheduler = new Scheduler(schedule);

var runner = new SimulationRunner(
    scheduler,
    simParams, ctx, "logs");

runner.Run(TimeSpan.FromMinutes(3));

Console.WriteLine("Simulation finished. CSV logs are in ./logs");