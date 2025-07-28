using System;
using System.Collections.Generic;
using System.Linq;
using PriceImpactSimulator.Domain;

namespace PriceImpactSimulator.Engine;

// Генератор рыночной активности. В каждом тике формирует случайные
// сделки и заявки, а также поддерживает минимальную ликвидность в книге.
// Симулятор опирается на параметры `SimParams` и работает поверх `OrderBook`.
public sealed class MarketSimulator
{
    // Книга заявок, в которой совершаются все операции
    private readonly OrderBook _book;
    // Источник случайных чисел для генерации рыночных событий
    private readonly Random _rng;
    // Набор параметров симулятора
    private readonly SimParams _p;
    // Стартовое срединное значение цены для инициализации ликвидности
    private readonly decimal _startMid;
    // Идентификаторы ордеров, выставленных самим симулятором для поддержания глубины
    private readonly HashSet<Guid> _houseLiquidity = new();

    // Скользящее окно последних сделок для расчёта тренда
    private readonly Queue<Trade> _recentTrades = new();
    // История средних цен для оценки отклонения цены
    private readonly Queue<decimal> _midHistory = new();

    // Инициализирует симулятор и заполняет стакан начальными уровнями
    public MarketSimulator(OrderBook book, SimParams p)
    {
        _book = book;
        _rng = new Random(p.Seed);
        _p = p;
        _startMid = p.StartMidPrice;

        // Создаём симметричный стакан из 10 уровней спроса и предложения.
        // Количество лотов на каждом уровне убывает экспоненциально, что
        // имитирует более жидкие дальние уровни.
        for (int lvl = 0; lvl < 10; lvl++)
        {
            var vol = (int)Math.Round(_p.Q0 * Math.Exp(-_p.LambdaDepth * lvl));

            var askPrice = _startMid + (lvl + 1) * _p.TickSize;
            var bidPrice = _startMid - (lvl + 1) * _p.TickSize;

            _book.AddLimit(new Order(Guid.NewGuid(), DateTime.UtcNow,
                Side.Sell, askPrice, vol, OrderType.Limit, null), DateTime.UtcNow);
            _book.AddLimit(new Order(Guid.NewGuid(), DateTime.UtcNow,
                Side.Buy, bidPrice, vol, OrderType.Limit, null), DateTime.UtcNow);
        }
    }

    public IReadOnlyCollection<Trade> RecentTrades => _recentTrades.ToList();



    // Параметры, управляющие поведением симулятора. Все значения подбираются
    // эмпирически и задают характеристики рынка и распределение объёмов.
    public record SimParams(
        decimal TickSize,
        decimal StartMidPrice,
        double CancelProb,
        int TrendLookback,
        int PriceLookback,
        double K1Imbalance,
        double K2Trend,
        double K3PriceDev,
        double LambdaDepth,
        int Q0,
        double LogNormMu,
        double LogNormSigma,
        int Seed);


    // Выполняет один шаг симуляции. Возвращает отчёты об исполнении ордеров,
    // списки сделок и отмен. Внутри производится поддержание ликвидности,
    // случайный выбор направления и типа следующего ордера.
    public (IEnumerable<ExecutionReport> execs,
        IEnumerable<Trade> trades,
        IEnumerable<ExecutionReport> cancels)
        Step(DateTime ts)
    {
        // Сначала следим, чтобы в книге сохранялась базовая ликвидность
        EnsureLiquidity(ts);

        // Случайно отменяем часть существующих заявок
        var cancelReports = CancelRandom(ts);

        // Далее генерируем новый рыночный или лимитный ордер
        var (pbuy, dirSide) = CalcDirectionProb();
        var side = _rng.NextDouble() < pbuy ? Side.Buy : Side.Sell;

        // Объём ордера распределён логнормально, что даёт реалистичные хвосты
        var qty = (int)Math.Max(1, Math.Round(Math.Exp(RandomNormal(_p.LogNormMu, _p.LogNormSigma))));
        // Режим исполнения: рыночный или лимитный с вероятностью 50/50
        var isMarket = _rng.NextDouble() < 0.5;

        if (isMarket)
        {
            // Агрессивное исполнение по лучшим ценам стакана
            var (execs, trs) = _book.ExecuteMarket(side, qty, ts);
            AppendTrades(trs);
            UpdateMidHistory(ts);
            return (execs, trs, cancelReports);
        }
        else
        {
            // Лимитный ордер ставится на случайно смещённом уровне от текущего mid
            var priceOffset = Math.Abs(RandomNormal(0, 1.5));
            var signedOff = side == Side.Buy ? -priceOffset : priceOffset;
            var midBase = _book.Mid ?? _startMid;
            var price = Math.Round(midBase + (decimal)signedOff * _p.TickSize, 2);

            var order = new Order(Guid.NewGuid(), ts, side, price, qty, OrderType.Limit, null);
            var (exs, trs) = _book.AddLimit(order, ts);

            UpdateMidHistory(ts);
            return (exs, trs, cancelReports);
        }

        double RandomNormal(double mu, double sigma)
        {
            // Генерация нормального распределения методом Бокса‑Мюллера
            var u1 = 1.0 - _rng.NextDouble();
            var u2 = 1.0 - _rng.NextDouble();
            var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) *
                                Math.Sin(2.0 * Math.PI * u2);
            return mu + sigma * randStdNormal;
        }
    }
    // Поддерживает симметричный «домашний» стакан ликвидности вокруг текущего mid
    private void EnsureLiquidity(DateTime ts)
    {
        decimal mid = _book.Mid ?? _startMid;
        const int Depth = 10;

        var keep = new HashSet<Guid>();

        for (int lvl = 0; lvl < Depth; lvl++)
        {
            decimal bidPrice = mid - (lvl + 1) * _p.TickSize;
            decimal askPrice = mid + (lvl + 1) * _p.TickSize;
            int     target   = (int)Math.Round(_p.Q0 * Math.Exp(-_p.LambdaDepth * lvl));

            // Подгоняем каждый уровень до целевого объёма
            keep.UnionWith(AdjustLevel(Side.Buy , bidPrice, target, ts));
            keep.UnionWith(AdjustLevel(Side.Sell, askPrice, target, ts));
        }

        // Удаляем из книги заявки, которые больше не нужны для поддержания объёма
        foreach (var id in _houseLiquidity.Except(keep).ToArray())
        {
            _book.Cancel(id, ts).ToList();
            _houseLiquidity.Remove(id);
        }
    }

    // Корректирует объём на определённом ценовом уровне в книге,
    // выставляя или снимая заявки симулятора
    private IEnumerable<Guid> AdjustLevel(Side side, decimal price, int targetQty, DateTime ts)
    {
        // Смотрим, сколько лотов по нужной цене уже принадлежит симулятору
        var allAtPrice   = _book.OrdersAtPrice(side, price);
        int houseQty     = allAtPrice.Where(o => _houseLiquidity.Contains(o.Id))
            .Sum(o => o.Quantity);
        // Расчёт недостающего/избыточного количества
        int delta        = targetQty - houseQty;
        var idsToKeep    = new List<Guid>();

        if (delta > 0)
        {
            // Недостаёт объёма – выставляем дополнительную заявку
            var id = Guid.NewGuid();
            _book.AddLimit(new Order(id, ts, side, price, delta,
                OrderType.Limit, null), ts);

            _houseLiquidity.Add(id);
            idsToKeep.Add(id);
        }

        if (delta < 0)
        {
            // Объёма больше чем нужно – снимаем лишние заявки
            int excess = -delta;
            foreach (var ord in allAtPrice.Where(o => _houseLiquidity.Contains(o.Id)))
            {
                if (excess <= 0) { idsToKeep.Add(ord.Id); continue; }

                _book.Cancel(ord.Id, ts).ToList();
                _houseLiquidity.Remove(ord.Id);
                excess -= ord.Quantity;
            }
        }

        idsToKeep.AddRange(allAtPrice.Where(o => _houseLiquidity.Contains(o.Id))
            .Select(o => o.Id));
        return idsToKeep;
    }

    // На основе текущего состояния книги и недавней истории оценить
    // вероятность прихода очередного покупателя. Метод возвращает вероятность
    // покупки и направление доминирующей стороны.
    private (double pBuy, Side biasDir) CalcDirectionProb()
    {
        var bidQty = _book.Snapshot(DateTime.MinValue, 3).Bids.Sum(l => l.Quantity);
        var askQty = _book.Snapshot(DateTime.MinValue, 3).Asks.Sum(l => l.Quantity);
        var imb = (double)(bidQty - askQty) / Math.Max(1, bidQty + askQty);

        var buys = _recentTrades.Count(t => t.AggressorSide == Side.Buy);
        var sells = _recentTrades.Count - buys;
        var trend = (_recentTrades.Count == 0) ? 0.0 : (double)(buys - sells) / _recentTrades.Count;

        var priceDevTicks = (_book.Mid ?? _startMid) - _startMid;
        var priceDev = (double)(priceDevTicks / _p.TickSize);

        if (_midHistory.Count == _p.PriceLookback)
        {
            var first = _midHistory.Peek();
            priceDev = (double)((_book.Mid - first) / _p.TickSize) * 0.01;
        }

        var pBuy = 0.5 + _p.K1Imbalance * imb + _p.K2Trend * trend - _p.K3PriceDev * priceDev;
        pBuy = Math.Clamp(pBuy, 0.05, 0.95);
        return (pBuy, imb >= 0 ? Side.Buy : Side.Sell);
    }

    // С небольшой вероятностью снимает существующие заявки,
    // имитируя уход ликвидности из рынка
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

    // Добавляем новые сделки в очередь для расчёта краткосрочного тренда
    private void AppendTrades(IEnumerable<Trade> trades)
    {
        foreach (var t in trades)
        {
            _recentTrades.Enqueue(t);
            if (_recentTrades.Count > _p.TrendLookback) _recentTrades.Dequeue();
        }
    }

    // Храним историю mid‑цен для оценки долгосрочного отклонения
    private void UpdateMidHistory(DateTime ts)
    {
        var midOpt = _book.Mid;
        if (midOpt is null) return;

        _midHistory.Enqueue(midOpt.Value);
        if (_midHistory.Count > _p.PriceLookback) _midHistory.Dequeue();
    }
}