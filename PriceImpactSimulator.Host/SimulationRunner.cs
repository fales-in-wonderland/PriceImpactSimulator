using System;
using System.Threading;
using PriceImpactSimulator.Domain;
using PriceImpactSimulator.Engine;
using PriceImpactSimulator.Persistence;
using PriceImpactSimulator.StrategyApi;

namespace PriceImpactSimulator.Host;

public sealed class SimulationRunner
{
    private readonly IStrategy        _strategy;
    private readonly StrategyContext  _ctx;
    private readonly MarketSimulator  _sim;
    private readonly OrderBook        _book;
    private readonly CsvSink          _sink;
    private readonly TimeSpan         _step;

    public SimulationRunner(
        IStrategy strategy,
        MarketSimulator.SimParams p,
        StrategyContext ctx,
        string logFolder)
    {
        _strategy = strategy;
        _sink = new CsvSink(logFolder);
        _ctx = new StrategyContext
        {
            TickSize = ctx.TickSize,
            CapitalLimit = ctx.CapitalLimit,
            SimulationStep = ctx.SimulationStep,
            Logger = msg => { ctx.Logger(msg); _sink.LogEvent(msg); }
        };
        _step = ctx.SimulationStep;

        _book = new OrderBook();
        _sim  = new MarketSimulator(_book, p);

        _strategy.Initialize(_ctx);
    }

    public void Run(TimeSpan duration)
    {
        var now = DateTime.UtcNow;
        var end = now + duration;
        var nextBookDump = now;
        var nextStats = now;

        while (now < end)
        {
            // шаг фонового рынка
            var (execsBg, tradesBg, cancelsBg) = _sim.Step(now);

            foreach (var tr in tradesBg) _sink.LogTrade(tr);
            foreach (var ex in execsBg)
            {
                _sink.LogExec(ex);
                _strategy.OnExecution(ex);
            }
            foreach (var ex in cancelsBg)
            {
                _sink.LogExec(ex);
                _strategy.OnExecution(ex);
            }

            // книга → стратегия
            var snap = _book.Snapshot(now, depthLevels: 10);
            _strategy.OnOrderBook(snap);

            if (now >= nextBookDump)
            {
                _sink.LogBook(snap);
                nextBookDump = now + TimeSpan.FromSeconds(10);
            }

            if (now >= nextStats && _strategy is IStrategyWithStats s)
            {
                var m = s.Metrics;
                _sink.LogStats(now, m.BuyingPowerUsed, m.Position, m.Vwap, m.PnL);
                nextStats = now + TimeSpan.FromSeconds(2);
            }

            // исполнение наших приказов
            /* примечание: для «нулевой» стратегии список пуст, 
               в MomentumIgnitor мы будем проверять exec.Side совпадает с нашими Id */
            // ...

            // стратегия выдаёт команды
            var cmds = _strategy.GenerateCommands(now);
            foreach (var cmd in cmds) Apply(cmd, now);

            Thread.Sleep(_step);
            now += _step;
        }

        _sink.Dispose();
    }

    private void Apply(OrderCommand cmd, DateTime ts)
    {
        switch (cmd.Type)
        {
            case CommandType.New:
                var order = new Order(cmd.OrderId, ts, cmd.Side, cmd.Price,
                                      cmd.Quantity, OrderType.Limit, null);
                var (execs, trades) = _book.AddLimit(order, ts);
                foreach (var tr in trades) _sink.LogTrade(tr);
                foreach (var ex in execs)
                {
                    _sink.LogExec(ex);
                    _strategy.OnExecution(ex);
                }
                break;

            case CommandType.Cancel:
                foreach (var ex in _book.Cancel(cmd.OrderId, ts))
                {
                    _sink.LogExec(ex);
                    _strategy.OnExecution(ex);
                }
                break;
        }
    }
}
