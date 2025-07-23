// runs multiple strategies together
﻿using PriceImpactSimulator.Domain;
using PriceImpactSimulator.StrategyApi;

namespace PriceImpactSimulator.Host;

public sealed class MultiStrategy : IStrategy
{
    private readonly IStrategy[] _strategies;
    public MultiStrategy(params IStrategy[] strats) => _strategies = strats;

    public void Initialize(in StrategyContext ctx)
    {
        foreach (var s in _strategies) s.Initialize(ctx);
    }

    public void OnOrderBook(in OrderBookSnapshot snap)
    {
        foreach (var s in _strategies) s.OnOrderBook(snap);
    }

    public void OnExecution(in ExecutionReport rep)
    {
        foreach (var s in _strategies) s.OnExecution(rep);
    }

    public IReadOnlyList<OrderCommand> GenerateCommands(DateTime nowUtc)
    {
        var list = new List<OrderCommand>();
        foreach (var s in _strategies) list.AddRange(s.GenerateCommands(nowUtc));
        return list;
    }
}
