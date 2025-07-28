using System;
using System.Collections.Generic;
using PriceImpactSimulator.Domain;
using PriceImpactSimulator.StrategyApi;

namespace PriceImpactSimulator.Strategies;

// Простая стратегия усреднения: покупает актив небольшими порциями и
// сразу закрывает позицию при достижении цели по прибыли или стоп‑лоссу.
public sealed class DripFlipStrategy : IStrategy, IStrategyWithStats
{
    // --- Параметры стратегии ---
    // Размер одной покупки
    private const int     SliceQty  = 1;
    // Цель по прибыли в абсолютных ценовых пунктах
    private const decimal TakeProf  = 0.10m;
    // Допустимый убыток (стоп‑лосс)
    private const decimal StopLoss  = 0.05m;

    // --- Состояние стратегии ---
    private StrategyContext      _ctx = null!; // предоставляет доступ к инфраструктуре
    private int                  _pos;         // текущая позиция
    private decimal              _vwap;        // средняя цена позиции
    private decimal              _bid;         // последняя известная лучшая цена покупки
    private decimal              _realised;    // накопленная реализованная прибыль
    private readonly HashSet<Guid> _liveIds = new(); // отслеживаем активные заявки

    // --- Показатели для мониторинга ---
    public StrategyMetrics Metrics => new(
        BuyingPowerUsed : _pos * _vwap,
        Position        : _pos,
        Vwap            : _pos > 0 ? _vwap : 0m,
        PnL             : _realised + _pos * (_bid - _vwap),
        RealisedPnL     : _realised);

    // IStrategy implementation
    // Получаем ссылку на инфраструктуру и выводим сообщение в лог
    public void Initialize(in StrategyContext ctx)
    {
        _ctx = ctx;
        _ctx.Logger("DripFlipStrategy ready.");
    }

    // Сохраняем текущую лучшую цену покупки для расчёта PnL
    public void OnOrderBook(in OrderBookSnapshot snap)
    {
        if (snap.Bids.Length > 0)
            _bid = snap.Bids[0].Price;
    }

    // Обработка отчётов об исполнении наших заявок
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
        // Закрываем позицию по достижению прибыли или стоп‑лосса
        if (_pos > 0 &&
            (_bid >= _vwap + TakeProf || _bid <= _vwap - StopLoss))
        {
            _ctx.Logger($"Flattening {_pos} @ market (bid={_bid:F2}, vwap={_vwap:F2})");
            var id = Guid.NewGuid();
            _liveIds.Add(id);
            return new[] { OrderCommand.New(id, Side.Sell, 0m, _pos) };
        }

        // Иначе докупаем ещё одну «порцию» по рынку
        var buyId = Guid.NewGuid();
        _liveIds.Add(buyId);
        return new[] { OrderCommand.New(buyId, Side.Buy, 0m, SliceQty) };
    }
}
