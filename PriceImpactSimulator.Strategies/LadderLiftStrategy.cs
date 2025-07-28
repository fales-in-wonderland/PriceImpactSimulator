using System;
using System.Collections.Generic;
using System.Linq;
using PriceImpactSimulator.Domain;
using PriceImpactSimulator.StrategyApi;

namespace PriceImpactSimulator.Strategies;

// Стратегия размещает «лестницу» лимитных заявок на покупку и
// двигает её вслед за рынком, пока не получит сделку.
public sealed class LadderLiftStrategy : IStrategy, IStrategyWithStats
{
    private StrategyContext _ctx = null!;                        // окружение симуляции
    private DateTime _startTime;                                 // время запуска

    // Активные лимитные заявки стратегии
    private readonly List<(Guid id, decimal price, int qty)> _orders = new();

    private decimal _lastBestBid;                                // последняя лучшая цена покупки
    private decimal _lastMid;                                    // средняя цена рынка

    private int _position;                                       // позиция после исполнений
    private decimal _vwap;                                       // средняя цена позиции
    private decimal _bpOrders;                                   // объём денег в заявках

    // Метрики для отображения состояния стратегии в отчётах
    public StrategyMetrics Metrics => new(
        BuyingPowerUsed: _bpOrders + _position * _vwap,
        Position: _position,
        Vwap: _position > 0 ? _vwap : 0m,
        PnL: _position * (_lastMid - (_position > 0 ? _vwap : 0m)),
        RealisedPnL: 0m);

    // Сохраняем контекст и отмечаем стартовое время для логов
    public void Initialize(in StrategyContext ctx)
    {
        _ctx = ctx;
        _startTime = DateTime.UtcNow;
        _ctx.Logger($"LadderLiftStrategy initialized at {_startTime:O}");
    }

    // Обновляем последние наблюдаемые цены
    public void OnOrderBook(in OrderBookSnapshot snapshot)
    {
        if (snapshot.Bids.Length > 0)
            _lastBestBid = snapshot.Bids[0].Price;
        if (snapshot.Bids.Length > 0 && snapshot.Asks.Length > 0)
            _lastMid = (snapshot.Bids[0].Price + snapshot.Asks[0].Price) / 2m;
    }

    private readonly Queue<OrderCommand> _pendingCancels = new();

    // Обработка отчетов о заявках. При получении сделки снимаем оставшуюся лестницу
    public void OnExecution(in ExecutionReport report)
    {
        var orderId = report.OrderId;
        var idx = _orders.FindIndex(o => o.id == orderId);

        if (idx >= 0 && report.ExecType == ExecType.New)
        {
            var entry = _orders[idx];
            _orders[idx] = (entry.id, entry.price, report.LeavesQty);
        }
        else if (idx >= 0 && report.ExecType == ExecType.Trade && report.LastQty > 0)
        {
            _ctx.Logger("Got touched ‑ cancelling ladder to stay flat");


            foreach (var (id, p, q) in _orders)
            {
                _bpOrders -= p * q;
            }

            foreach (var o in _orders.Select(o => OrderCommand.Cancel(o.id)))
            {
                _pendingCancels.Enqueue(o);
            }

            _orders.Clear();
            return;
        }
        else if (idx >= 0 && report.ExecType == ExecType.Cancel)
        {
            var entry = _orders[idx];
            _bpOrders -= entry.price * entry.qty;
            _orders.RemoveAt(idx);
        }
    }

    // Генерируем новые приказы каждый тик. Если рынок движется - перемещаем лестницу
    public IReadOnlyList<OrderCommand> GenerateCommands(DateTime utcNow)
    {
        if (_pendingCancels.Count > 0)
        {
            var c = _pendingCancels.ToArray();
            _pendingCancels.Clear();
            return c;
        }

        if (_lastBestBid == 0m)
            return Array.Empty<OrderCommand>();

        if (_orders.Count == 0)
            return PlaceLadder(_lastBestBid - _ctx.TickSize);

        decimal myTop = _orders.Max(o => o.price);
        decimal diff = _lastBestBid - myTop;

        if (diff <= 0m)
            return ShiftLadder(myTop - _ctx.TickSize, "down");
        if (diff > _ctx.TickSize)
            return ShiftLadder(myTop + _ctx.TickSize, "up");

        return Array.Empty<OrderCommand>();
    }

    // Размещаем ступенчатый набор лимитных заявок вниз от указанной цены
    private IReadOnlyList<OrderCommand> PlaceLadder(decimal startPrice)
    {
        _ctx.Logger($"Placing ladder from {startPrice:F2}");
        const int levels = 5;
        const double lambda = 0.5;
        const int baseQty = 10000;
        var cmds = new List<OrderCommand>(levels);
        for (int i = 0; i < levels; i++)
        {
            var price = startPrice - i * _ctx.TickSize;
            var qty = (int)Math.Round(baseQty * Math.Exp(-lambda * i));
            var id = Guid.NewGuid();
            cmds.Add(OrderCommand.New(id, Side.Buy, price, qty));
            _orders.Add((id, price, qty));
            _bpOrders += price * qty;
        }

        return cmds;
    }

    // Сдвигаем текущую лестницу на другую цену
    private IReadOnlyList<OrderCommand> ShiftLadder(decimal newStart, string dir)
    {
        _ctx.Logger($"Shifting ladder {dir} to start {newStart:F2}");
        var cancel = _orders.Select(o => OrderCommand.Cancel(o.id)).ToList();
        var place = PlaceLadder(newStart);
        cancel.AddRange(place);
        return cancel;
    }
}
