using System;
using System.Collections.Generic;
using PriceImpactSimulator.Domain;
using PriceImpactSimulator.StrategyApi;

namespace PriceImpactSimulator.Strategies;

/// <summary>
/// «Капельный» набор позиции + единоразовый сброс:
/// • каждый тик покупаем SliceQty маркет‑лотов
/// • если bid ≥ VWAP+0.05 €  ИЛИ  ≤ VWAP−0.02 €  — сбрасываем всё маркетом
/// Учёт PnL ведётся ТОЛЬКО по собственным ордерам.
/// </summary>
public sealed class DripFlipStrategy : IStrategy, IStrategyWithStats
{
    // — параметры —
    private const int     SliceQty  = 1;
    private const decimal TakeProf  = 0.10m;
    private const decimal StopLoss  = 0.05m;

    // — состояние —
    private StrategyContext      _ctx = null!;
    private int                  _pos;
    private decimal              _vwap;
    private decimal              _bid;        // последний лучш. bid
    private decimal              _realised;
    private readonly HashSet<Guid> _liveIds = new();   // активные мои ордера

    // — публичные метрики —
    public StrategyMetrics Metrics => new(
        BuyingPowerUsed : _pos * _vwap,
        Position        : _pos,
        Vwap            : _pos > 0 ? _vwap : 0m,
        PnL             : _realised + _pos * (_bid - _vwap),
        RealisedPnL     : _realised);

    // ------------ IStrategy plumbing ------------------------------------
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
        // фильтруем только МОИ отчёты
        if (!_liveIds.Remove(rep.OrderId)) return;

        // если ордер не полностью исполнен — оставляем ID в трекинге
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
        else // Sell
        {
            _realised += rep.LastQty * (rep.Price - _vwap);
            _pos      -= rep.LastQty;

            if (_pos <= 0)          // полностью вышли — сброс VWAP
            {
                _pos  = 0;
                _vwap = 0m;
            }
        }
    }

    public IReadOnlyList<OrderCommand> GenerateCommands(DateTime nowUtc)
    {
        // ---- 1) flatten? -------------------------------------------------
        if (_pos > 0 &&
            (_bid >= _vwap + TakeProf || _bid <= _vwap - StopLoss))
        {
            _ctx.Logger($"Flattening {_pos} @ market (bid={_bid:F2}, vwap={_vwap:F2})");
            var id = Guid.NewGuid();
            _liveIds.Add(id);
            return new[] { OrderCommand.New(id, Side.Sell, 0m, _pos) };
        }

        // ---- 2) drip‑buy slice ------------------------------------------
        var buyId = Guid.NewGuid();
        _liveIds.Add(buyId);
        return new[] { OrderCommand.New(buyId, Side.Buy, 0m, SliceQty) };
    }
}
