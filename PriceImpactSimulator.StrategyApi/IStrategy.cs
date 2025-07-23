// strategy interface
﻿
using System;
using System.Collections.Generic;
using PriceImpactSimulator.Domain;

namespace PriceImpactSimulator.StrategyApi;


public interface IStrategy
{
    void Initialize(in StrategyContext ctx);

    void OnOrderBook(in OrderBookSnapshot snapshot);

    void OnExecution(in ExecutionReport report);

    IReadOnlyList<OrderCommand> GenerateCommands(DateTime utcNow);
}