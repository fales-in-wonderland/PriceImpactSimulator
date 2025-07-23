// simple drip strategy
﻿using System;
using System.Collections.Generic;
using PriceImpactSimulator.Domain;
using PriceImpactSimulator.StrategyApi;

namespace PriceImpactSimulator.Strategies;






public sealed class DripFlipStrategy : IStrategy, IStrategyWithStats
{
    
    private const int SliceQty = 1; 
    private const decimal TakeProf = 0.05m; 
    private const decimal StopLoss = 0.02m; 
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(10);

    
    private StrategyContext _ctx = null!;
    private DateTime _start;
    private int _position;
    private decimal _vwap; 
    private decimal _lastBestBid;
    private decimal _realised;
    private readonly HashSet<Guid> _myOrders = new();

    
    public StrategyMetrics Metrics => new(
        BuyingPowerUsed: _position * _vwap,
        Position: _position,
        Vwap: _position > 0 ? _vwap : 0m,
        PnL: _realised + _position * (_lastBestBid - _vwap),
        RealisedPnL: _realised);

    
    public void Initialize(in StrategyContext ctx)
    {
        _ctx = ctx;
        _start = DateTime.UtcNow;
        _ctx.Logger("DripAccumThenDumpStrategy armed; will start after 40 s.");
    }

    public void OnOrderBook(in OrderBookSnapshot snap)
    {
        if (snap.Bids.Length > 0) _lastBestBid = snap.Bids[0].Price;
    }

    public void OnExecution(in ExecutionReport rep)
    {
        if (!_myOrders.Contains(rep.OrderId)) return;
        
        if (rep.ExecType != ExecType.Trade || rep.LastQty == 0) return;
        if (rep.Side == Side.Buy)
        {
            int prevPos = _position;
            _position += rep.LastQty;
            _vwap = prevPos == 0
                ? rep.Price
                : (_vwap * prevPos + rep.Price * rep.LastQty) / _position;
        }
        else if (rep.Side == Side.Sell)
        {
            _realised += rep.LastQty * (rep.Price - _vwap);

            _position -= rep.LastQty;
            if (_position <= 0)
            {
                _position = 0;
                _vwap = 0m;
            }
        }
    }

    public IReadOnlyList<OrderCommand> GenerateCommands(DateTime nowUtc)
    {
        
        if (nowUtc - _start < StartDelay) return Array.Empty<OrderCommand>();

        
        if (_position > 0 &&
            (_lastBestBid >= _vwap + TakeProf || _lastBestBid <= _vwap - StopLoss))
        {
            _ctx.Logger($"Flattening {_position} @ market (bid={_lastBestBid:F2}, vwap={_vwap:F2})");
            var id = Guid.NewGuid();
            _myOrders.Add(id);
            return new[] { OrderCommand.New(id, Side.Sell, 0m, _position) }; 
        }

        
        var buyId = Guid.NewGuid();
        _myOrders.Add(buyId);
        return new[] { OrderCommand.New(buyId, Side.Buy, 0m, SliceQty) };
    }
}