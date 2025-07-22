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
        _ctx = ctx;
        _step = ctx.SimulationStep;

        _book = new OrderBook();
        _sim  = new MarketSimulator(_book, p);
        _sink = new CsvSink(logFolder);

        _strategy.Initialize(ctx);
    }

    public void Run(TimeSpan duration)
    {
        var now = DateTime.UtcNow;
        var end = now + duration;

        while (now < end)
        {
            // шаг фонового рынка
            var (execsBg, tradesBg, cancelsBg) = _sim.Step(now);

            foreach (var tr in tradesBg) _sink.LogTrade(tr);
            foreach (var ex in execsBg) _sink.LogExec(ex);
            foreach (var ex in cancelsBg) _sink.LogExec(ex);

            // книга → стратегия
            var snap = _book.Snapshot(now, depthLevels: 10);
            _strategy.OnOrderBook(snap);

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
                _book.AddLimit(order);
                _sink.LogExec(new ExecutionReport(
                    order.Id, ExecType.New, order.Side,
                    order.Price,
                    0, cmd.Quantity, ts));

                break;

            case CommandType.Cancel:
                foreach (var ex in _book.Cancel(cmd.OrderId, ts)) _sink.LogExec(ex);
                break;
        }
    }
}
