// empty strategy
﻿
using System;
using System.Collections.Generic;
using PriceImpactSimulator.Domain;
using PriceImpactSimulator.StrategyApi;

namespace PriceImpactSimulator.Strategies;

public sealed class NoOpStrategy : IStrategy
{
    public void Initialize(in StrategyContext ctx) { }

    public void OnOrderBook(in OrderBookSnapshot snapshot) { }

    public void OnExecution(in ExecutionReport report) { }

    public IReadOnlyList<OrderCommand> GenerateCommands(DateTime utcNow)
        => Array.Empty<OrderCommand>();
}