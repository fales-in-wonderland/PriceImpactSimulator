using System.Collections.Generic;
using PriceImpactSimulator.Domain;

namespace PriceImpactSimulator.Engine;

/// <summary>Простая книга заявок с ценовым шагом 0.01 €.</summary>
public sealed class OrderBook
{
    private readonly SortedDictionary<decimal, Queue<Order>> _bids =
        new(new DescComparer());               // max‑price first
    private readonly SortedDictionary<decimal, Queue<Order>> _asks =
        new();                                 // min‑price first

    private readonly Dictionary<Guid, (decimal price, Side side)> _index = new();

    public IEnumerable<Guid> ActiveOrderIds => _index.Keys;
    public decimal? BestBid => _bids.Count != 0 ? _bids.Keys.First() : null;
    public decimal? BestAsk => _asks.Count != 0 ? _asks.Keys.First() : null;
    
    public decimal? Mid
    {
        get
        {
            if (BestBid.HasValue && BestAsk.HasValue)
                return (BestBid.Value + BestAsk.Value) / 2m;
            return BestBid ?? BestAsk;
        }
    }

    internal SortedDictionary<decimal, Queue<Order>> BidsInternal => _bids;
    internal SortedDictionary<decimal, Queue<Order>> AsksInternal => _asks;


    #region Public API ------------------------------------------------------

    /// <summary>Adds a limit order with immediate matching if crossing.</summary>
    public (IEnumerable<ExecutionReport> execs, IEnumerable<Trade> trades)
        AddLimit(Order order, DateTime ts)
    {
        var execs = new List<ExecutionReport>();
        var trades = new List<Trade>();

        // try match against opposite book first
        var opp = order.Side == Side.Buy ? _asks : _bids;
        while (order.Quantity > 0 && opp.Count > 0)
        {
            var best = opp.Keys.First();
            bool crosses = order.Side == Side.Buy
                ? order.Price >= best
                : order.Price <= best;
            if (!crosses) break;

            var q = opp[best];
            while (q.Count > 0 && order.Quantity > 0)
            {
                var resting = q.Peek();
                var execQty = int.Min(order.Quantity, resting.Quantity);

                trades.Add(new Trade(ts, order.Side, best, execQty));

                q.Dequeue();
                if (resting.Quantity > execQty)
                {
                    q.Enqueue(resting with { Quantity = resting.Quantity - execQty });
                }
                else
                {
                    _index.Remove(resting.Id);
                }

                order = order with { Quantity = order.Quantity - execQty };

                execs.Add(new ExecutionReport(
                    resting.Id, ExecType.Trade, resting.Side,
                    best,
                    execQty, resting.Quantity - execQty, ts));

                execs.Add(new ExecutionReport(
                    order.Id, ExecType.Trade, order.Side,
                    best,
                    execQty, order.Quantity, ts));
            }

            if (q.Count == 0) opp.Remove(best);
        }

        if (order.Quantity > 0)
        {
            var book = order.Side == Side.Buy ? _bids : _asks;
            if (!book.TryGetValue(order.Price, out var q))
            {
                q = new Queue<Order>();
                book[order.Price] = q;
            }
            q.Enqueue(order);
            _index[order.Id] = (order.Price, order.Side);

            execs.Add(new ExecutionReport(
                order.Id, ExecType.New, order.Side,
                order.Price,
                0, order.Quantity, ts));
        }

        return (execs, trades);
    }

    public int QuantityAt(Side side, decimal price)
    {
        var book = side == Side.Buy ? _bids : _asks;
        return book.TryGetValue(price, out var q) ? q.Sum(o => o.Quantity) : 0;
    }

    public Order[] OrdersAtPrice(Side side, decimal price)
    {
        var book = side == Side.Buy ? _bids : _asks;
        return book.TryGetValue(price, out var q) ? q.ToArray() : Array.Empty<Order>();
    }

    public IEnumerable<ExecutionReport> Cancel(Guid orderId, DateTime ts)
    {
        if (!_index.TryGetValue(orderId, out var meta))
            yield break;                                // nothing to cancel

        var (price, side) = meta;
        var book = side == Side.Buy ? _bids : _asks;
        var q = book[price];
        var kept = new Queue<Order>();

        while (q.TryDequeue(out var ord))
        {
            if (ord.Id == orderId)
            {
                _index.Remove(orderId);
                yield return new ExecutionReport(
                    ord.Id, ExecType.Cancel, ord.Side,
                    ord.Price, 0, 0, ts);
                continue;
            }
            kept.Enqueue(ord);
        }
        if (kept.Count == 0) book.Remove(price);
        else book[price] = kept;
    }

    /// <summary>Исполняет market‑ордер и (опционально) возвращает отчёты.</summary>
    public (IEnumerable<ExecutionReport> execs, IEnumerable<Trade> trades)
        ExecuteMarket(Side side, int qty, DateTime ts)
    {
        var execs = new List<ExecutionReport>();
        var trades = new List<Trade>();
        var book = side == Side.Buy ? _asks : _bids;

        while (qty > 0 && book.Count > 0)
        {
            var price = book.Keys.First();
            var queue = book[price];

            while (queue.Count > 0 && qty > 0)
            {
                var resting = queue.Peek();
                var execQty = int.Min(qty, resting.Quantity);

                // агрессор не имеет собственного OrderId (симуляционный рынок)
                trades.Add(new Trade(ts, side, price, execQty));

                queue.Dequeue();
                if (resting.Quantity > execQty)
                {
                    queue.Enqueue(resting with { Quantity = resting.Quantity - execQty });
                }
                else
                {
                    _index.Remove(resting.Id);
                }

                qty -= execQty;
                execs.Add(new ExecutionReport(
                    resting.Id, ExecType.Trade, resting.Side,
                    price,
                    execQty, resting.Quantity - execQty, ts));

            }

            if (queue.Count == 0) book.Remove(price);
        }
        return (execs, trades);
    }

    public OrderBookSnapshot Snapshot(DateTime ts, int depthLevels)
    {
        static OrderBookLevel[] TakeLevels(IEnumerable<KeyValuePair<decimal, Queue<Order>>> src, int n)
        {
            var arr = new List<OrderBookLevel>(n);
            foreach (var kv in src)
            {
                arr.Add(new OrderBookLevel(kv.Key, kv.Value.Sum(o => o.Quantity)));
                if (arr.Count == n) break;
            }
            return arr.ToArray();
        }

        return new OrderBookSnapshot
        {
            Timestamp = ts,
            Bids = TakeLevels(_bids, depthLevels),
            Asks = TakeLevels(_asks, depthLevels)
        };
    }

    #endregion

    private sealed class DescComparer : IComparer<decimal>
    {
        public int Compare(decimal x, decimal y) => y.CompareTo(x);
    }
}
