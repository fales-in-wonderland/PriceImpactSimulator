// strategy context
﻿
using System;

namespace PriceImpactSimulator.StrategyApi;


public sealed class StrategyContext
{
    public required decimal   TickSize          { get; init; }
    public required decimal   CapitalLimit      { get; init; }
    public required TimeSpan  SimulationStep    { get; init; }
    public required Action<string> Logger       { get; init; }
}