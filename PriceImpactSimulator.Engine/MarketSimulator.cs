using System;
using System.Collections.Generic;
using PriceImpactSimulator.Domain;

namespace PriceImpactSimulator.Engine;

/// <summary>Генерирует фоновые заявки + случайные отмены строго по утверждённым правилам.</summary>
public sealed class MarketSimulator
{
    private readonly OrderBook _book;
    private readonly Random _rng;
    private readonly SimParams _p;
    private readonly decimal _startMid;

    

    private readonly Queue<Trade> _recentTrades = new();                 // last N trades
    private readonly Queue<decimal> _midHistory = new();                // last N mids

    public MarketSimulator(OrderBook book, SimParams p)
    {
        _book = book;
        _rng = new Random(p.Seed);
        _p = p;
        _startMid = p.StartMidPrice;

        for (int lvl = 0; lvl < 10; lvl++)
        {
            var vol = (int)Math.Round(_p.Q0 * Math.Exp(-_p.LambdaDepth * lvl));

            var askPrice = _startMid + (lvl + 1) * _p.TickSize;
            var bidPrice = _startMid - (lvl + 1) * _p.TickSize;

            _book.AddLimit(new Order(Guid.NewGuid(), DateTime.UtcNow,
                Side.Sell, askPrice, vol, OrderType.Limit, null));
            _book.AddLimit(new Order(Guid.NewGuid(), DateTime.UtcNow,
                Side.Buy, bidPrice, vol, OrderType.Limit, null));
        }
    }

    public IReadOnlyCollection<Trade> RecentTrades => _recentTrades.ToList();

// Engine/MarketSimulator.cs  (внутри класса, заменяем record SimParams)

    public record SimParams(
        decimal TickSize,
        decimal StartMidPrice,
        double  CancelProb,
        int     TrendLookback,
        int     PriceLookback,
        double  K1Imbalance,
        double  K2Trend,
        double  K3PriceDev,
        double  LambdaDepth,
        int     Q0,
        double  LogNormMu,
        double  LogNormSigma,
        int     Seed);


    /// <summary>Выполняет один 100‑мс «тик» симуляции.</summary>
    public (IEnumerable<ExecutionReport> execs,
            IEnumerable<Trade> trades,
            IEnumerable<ExecutionReport> cancels)
        Step(DateTime ts)
    {
        EnsureLiquidity(ts);
        
        // 1. случайные отмены
        var cancelReports = CancelRandom(ts);

        // 2. сгенерировать новую заявку
        var (pbuy, dirSide) = CalcDirectionProb();
        var side = _rng.NextDouble() < pbuy ? Side.Buy : Side.Sell;

        var qty = (int)Math.Max(1, Math.Round(Math.Exp(RandomNormal(_p.LogNormMu, _p.LogNormSigma))));
        var isMarket = _rng.NextDouble() < 0.5;

        if (isMarket)
        {
            var (execs, trs) = _book.ExecuteMarket(side, qty, ts);
            AppendTrades(trs);
            UpdateMidHistory(ts);
            return (execs, trs, cancelReports);
        }
        else
        {
            var priceOffset = Math.Abs(RandomNormal(0, 1.5));
            var signedOff = side == Side.Buy ? -priceOffset : priceOffset;
            var midBase = _book.Mid ?? 20.00m;
            var price = Math.Round(midBase + (decimal)signedOff * _p.TickSize, 2);


            var order = new Order(Guid.NewGuid(), ts, side, price, qty, OrderType.Limit, null);
            _book.AddLimit(order);
            var newExec = new ExecutionReport(
                order.Id, ExecType.New, side,
                price,
                0, qty, ts);

            UpdateMidHistory(ts);
            return (new[] { newExec }, Array.Empty<Trade>(), cancelReports);
        }

        // local helpers ----------
        double RandomNormal(double mu, double sigma)
        {
            // Box‑Muller
            var u1 = 1.0 - _rng.NextDouble();
            var u2 = 1.0 - _rng.NextDouble();
            var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) *
                                Math.Sin(2.0 * Math.PI * u2);
            return mu + sigma * randStdNormal;
        }
    }

    // ---------- helpers ----------------------------------------------------
    private void EnsureLiquidity(DateTime ts)
    {
        var snap = _book.Snapshot(ts, 1);
        int threshold = (int)(_p.Q0 * 0.25);

        decimal bestBid = _book.BestBid ?? (_startMid - _p.TickSize);
        decimal bestAsk = _book.BestAsk ?? (_startMid + _p.TickSize);

        if (snap.Bids.Length == 0 || snap.Bids[0].Quantity < threshold)
        {
            _book.AddLimit(new Order(Guid.NewGuid(), ts, Side.Buy,
                bestBid, _p.Q0, OrderType.Limit, null));
        }

        if (snap.Asks.Length == 0 || snap.Asks[0].Quantity < threshold)
        {
            _book.AddLimit(new Order(Guid.NewGuid(), ts, Side.Sell,
                bestAsk, _p.Q0, OrderType.Limit, null));
        }
    }

    private (double pBuy, Side biasDir) CalcDirectionProb()
    {
        // (a) book imbalance
        var bidQty = _book.Snapshot(DateTime.MinValue, 3).Bids.Sum(l => l.Quantity);
        var askQty = _book.Snapshot(DateTime.MinValue, 3).Asks.Sum(l => l.Quantity);
        var imb = (double)(bidQty - askQty) / Math.Max(1, bidQty + askQty);

        // (b) trade trend
        var buys = _recentTrades.Count(t => t.AggressorSide == Side.Buy);
        var sells = _recentTrades.Count - buys;
        var trend = (_recentTrades.Count == 0) ? 0.0 : (double)(buys - sells) / _recentTrades.Count;

        // (c) price deviation
        var priceDevTicks = (_book.Mid ?? _startMid) - _startMid;
        var priceDev = (double)(priceDevTicks / _p.TickSize);

        if (_midHistory.Count == _p.PriceLookback)
        {
            var first = _midHistory.Peek();
            priceDev = (double)((_book.Mid - first) / _p.TickSize) * 0.01; // normalised per tick
        }
        
        var pBuy = 0.5 + _p.K1Imbalance * imb + _p.K2Trend * trend - _p.K3PriceDev * priceDev;
        pBuy = Math.Clamp(pBuy, 0.05, 0.95);
        return (pBuy, imb >= 0 ? Side.Buy : Side.Sell);
    }

    private IEnumerable<ExecutionReport> CancelRandom(DateTime ts)
    {
        var canceled = new List<ExecutionReport>();
        foreach (var id in _book.ActiveOrderIds.ToArray())
        {
            if (_rng.NextDouble() < _p.CancelProb)
                canceled.AddRange(_book.Cancel(id, ts));
        }
        return canceled;
    }

    private void AppendTrades(IEnumerable<Trade> trades)
    {
        foreach (var t in trades)
        {
            _recentTrades.Enqueue(t);
            if (_recentTrades.Count > _p.TrendLookback) _recentTrades.Dequeue();
        }
    }

    private void UpdateMidHistory(DateTime ts)
    {
        var midOpt = _book.Mid;
        if (midOpt is null) return;

        _midHistory.Enqueue(midOpt.Value);
        if (_midHistory.Count > _p.PriceLookback) _midHistory.Dequeue();
    }
}
