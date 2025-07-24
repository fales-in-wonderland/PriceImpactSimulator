using System;
using System.Collections.Generic;
using PriceImpactSimulator.Domain;
using PriceImpactSimulator.StrategyApi;

namespace PriceImpactSimulator.Strategies;

// Buys in small slices and exits on profit or loss
public sealed class DripFlipStrategy : IStrategy, IStrategyWithStats
{
    // parameters
    private const int     SliceQty  = 1;
    private const decimal TakeProf  = 0.10m;
    private const decimal StopLoss  = 0.05m;

    // state
    private StrategyContext      _ctx = null!;
    private int                  _pos;
    private decimal              _vwap;
    private decimal              _bid;
    private decimal              _realised;
    private readonly HashSet<Guid> _liveIds = new();

    // public metrics
    public StrategyMetrics Metrics => new(
        BuyingPowerUsed : _pos * _vwap,
        Position        : _pos,
        Vwap            : _pos > 0 ? _vwap : 0m,
        PnL             : _realised + _pos * (_bid - _vwap),
        RealisedPnL     : _realised);

    // IStrategy implementation
    public void Initialize(in StrategyContext ctx)
    {
        _ctx = ctx;
        _ctx.Logger("DripFlipStrategy ready.");
    }

    public void OnOrderBook(in OrderBookSnapshot snap)
    {
        if (snap.Bids.Length > 0)
            _bid = snap.Bids[0].Price;
    }

    public void OnExecution(in ExecutionReport rep)
    {
        // ignore reports for other orders
        if (!_liveIds.Remove(rep.OrderId)) return;

        // keep id if partially filled
        if (rep.LeavesQty > 0 && rep.ExecType != ExecType.Cancel)
            _liveIds.Add(rep.OrderId);

        if (rep.ExecType != ExecType.Trade || rep.LastQty == 0) return;

        if (rep.Side == Side.Buy)
        {
            int prev = _pos;
            _pos += rep.LastQty;
            _vwap = prev == 0
                ? rep.Price
                : (_vwap * prev + rep.Price * rep.LastQty) / _pos;
        }
        else // sell
        {
            _realised += rep.LastQty * (rep.Price - _vwap);
            _pos      -= rep.LastQty;

            if (_pos <= 0)
            {
                _pos  = 0;
                _vwap = 0m;
            }
        }
    }

    public IReadOnlyList<OrderCommand> GenerateCommands(DateTime nowUtc)
    {
        // close position if target met
        if (_pos > 0 &&
            (_bid >= _vwap + TakeProf || _bid <= _vwap - StopLoss))
        {
            _ctx.Logger($"Flattening {_pos} @ market (bid={_bid:F2}, vwap={_vwap:F2})");
            var id = Guid.NewGuid();
            _liveIds.Add(id);
            return new[] { OrderCommand.New(id, Side.Sell, 0m, _pos) };
        }

        // otherwise buy next slice
        var buyId = Guid.NewGuid();
        _liveIds.Add(buyId);
        return new[] { OrderCommand.New(buyId, Side.Buy, 0m, SliceQty) };
    }
}
