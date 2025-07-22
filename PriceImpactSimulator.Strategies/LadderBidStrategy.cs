using System;
using System.Collections.Generic;
using System.Linq;
using PriceImpactSimulator.Domain;
using PriceImpactSimulator.StrategyApi;

namespace PriceImpactSimulator.Strategies;

public sealed class LadderBidStrategy : IStrategy, IStrategyWithStats
{
    private StrategyContext _ctx = null!;
    private DateTime _startTime;
    private readonly TimeSpan _delay = TimeSpan.FromSeconds(30);

    private readonly List<(Guid id, decimal price, int qty)> _orders = new();

    private decimal _lastBestBid;
    private decimal _lastMid;

    private int _position;
    private decimal _vwap;
    private decimal _bpOrders;

    public StrategyMetrics Metrics => new(
        BuyingPowerUsed: _bpOrders + _position * _vwap,
        Position: _position,
        Vwap: _position > 0 ? _vwap : 0m,
        PnL: _position * (_lastMid - (_position > 0 ? _vwap : 0m))
    );

    public void Initialize(in StrategyContext ctx)
    {
        _ctx = ctx;
        _startTime = DateTime.UtcNow;
        _ctx.Logger($"LadderBidStrategy initialized at {_startTime:O}");
    }

    public void OnOrderBook(in OrderBookSnapshot snapshot)
    {
        if (snapshot.Bids.Length > 0)
            _lastBestBid = snapshot.Bids[0].Price;
        if (snapshot.Bids.Length > 0 && snapshot.Asks.Length > 0)
            _lastMid = (snapshot.Bids[0].Price + snapshot.Asks[0].Price) / 2m;
    }

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
            var entry = _orders[idx];
            _bpOrders -= entry.price * report.LastQty;

            int prevPos = _position;
            _position += report.LastQty;
            _vwap = prevPos == 0
                ? report.Price
                : (_vwap * prevPos + report.Price * report.LastQty) / _position;
            _ctx.Logger($"Fill {report.LastQty} @ {report.Price:F2}; pos={_position}");

            var remaining = report.LeavesQty;
            if (remaining == 0)
                _orders.RemoveAt(idx);
            else
                _orders[idx] = (entry.id, entry.price, remaining);
        }
        else if (idx >= 0 && report.ExecType == ExecType.Cancel)
        {
            var entry = _orders[idx];
            _bpOrders -= entry.price * entry.qty;
            _orders.RemoveAt(idx);
            _ctx.Logger($"Canceled {report.OrderId}");
        }
    }

    public IReadOnlyList<OrderCommand> GenerateCommands(DateTime utcNow)
    {
        if (utcNow - _startTime < _delay || _lastBestBid == 0m)
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

    private IReadOnlyList<OrderCommand> PlaceLadder(decimal startPrice)
    {
        _ctx.Logger($"Placing ladder from {startPrice:F2}");
        const int levels = 5;
        const double lambda = 0.5;
        const int baseQty = 1000;
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

    private IReadOnlyList<OrderCommand> ShiftLadder(decimal newStart, string dir)
    {
        _ctx.Logger($"Shifting ladder {dir} to start {newStart:F2}");
        var cancel = _orders.Select(o => OrderCommand.Cancel(o.id)).ToList();
        var place = PlaceLadder(newStart);
        cancel.AddRange(place);
        return cancel;
    }
}

