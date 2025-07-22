// File: StrategyContext.cs
using System;

namespace PriceImpactSimulator.StrategyApi;

/// <summary>Read‑only environment info exposed to a strategy at runtime.</summary>
public sealed class StrategyContext
{
    public required decimal   TickSize          { get; init; }
    public required decimal   CapitalLimit      { get; init; }
    public required TimeSpan  SimulationStep    { get; init; }
    public required Action<string> Logger       { get; init; }
}