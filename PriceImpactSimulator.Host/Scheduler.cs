// Host/Scheduler.cs

using System;
using System.Collections.Generic;
using System.Linq;
using PriceImpactSimulator.Domain;
using PriceImpactSimulator.Persistence;
using PriceImpactSimulator.StrategyApi;

namespace PriceImpactSimulator.Host;

/// <summary>
/// Turns individual strategies ON / OFF according to a simple
/// “window” schedule measured in **simulation seconds**  
/// (i.e. time‑stamps coming from the engine).
/// All state transitions are logged into <c>strategy_events_*.csv</c>
/// as 0 = OFF, 1 = ON.
/// </summary>
public sealed class Scheduler : IStrategy, IStrategyWithStats
{
    // --------------------------------------------------------------------
    private readonly List<(double on, double off, IStrategy strat)> _win = new();

    private CsvSink? _sink;
    private StrategyContext _ctx = null!;
    private DateTime _t0; // simulation time origin
    private bool _t0Set = false;
    private double _lastSec; // latest sim‑second seen

    // --------------------------------------------------------------------
    public Scheduler(IEnumerable<StrategyWindow> windows)
    {
        foreach (var w in windows)
            _win.Add((w.OffsetSec, w.OffsetSec + w.DurationSec, w.Strategy));
    }

    public void AttachSink(CsvSink sink) => _sink = sink;

    // ===== helpers =======================================================
    private void TouchOrigin(DateTime ts)
    {
        if (_t0Set) return;
        _t0 = ts;
        _t0Set = true;
    }

    private double Sec(DateTime ts) => (ts - _t0).TotalSeconds;

    private static bool InRange(double t, double a, double b) => t >= a && t < b;

    private bool Live(IStrategy s, double t)
        => _win.Any(w => w.strat == s && InRange(t, w.on, w.off));

    // ===== IStrategy implementation ======================================
    public void Initialize(in StrategyContext ctx)
    {
        _ctx = ctx;
        foreach (var (_, _, s) in _win.DistinctBy(w => w.strat))
            s.Initialize(ctx);
    }

    public void OnOrderBook(in OrderBookSnapshot snap)
    {
        TouchOrigin(snap.Timestamp);
        _lastSec = Sec(snap.Timestamp);

        foreach (var (_, _, s) in _win)
            if (Live(s, _lastSec))
                s.OnOrderBook(snap);
    }

    public void OnExecution(in ExecutionReport rep)
    {
        TouchOrigin(rep.Timestamp);
        _lastSec = Sec(rep.Timestamp);

        foreach (var (_, _, s) in _win)
            if (Live(s, _lastSec))
                s.OnExecution(rep);
    }

    public IReadOnlyList<OrderCommand> GenerateCommands(DateTime utcNow)
    {
        TouchOrigin(utcNow);
        var t = Sec(utcNow);
        var dt = _ctx.SimulationStep.TotalSeconds;
        _lastSec = t;

        var cmds = new List<OrderCommand>();

        foreach (var s in _win.Select(w => w.strat).Distinct())
        {
            bool was = Live(s, t - dt);
            bool now = Live(s, t);

            // log state change
            if (was != now && _sink is not null)
                _sink.LogStrategy(utcNow, s.GetType().Name, now ? 1 : 0);

            if (now)
                cmds.AddRange(s.GenerateCommands(utcNow));
        }

        return cmds;
    }

    // ===== aggregated metrics of *currently live* strategies =============
    public StrategyMetrics Metrics
    {
        get
        {
            if (!_t0Set) return default;
            var live = _win
                .Where(w => Live(w.strat, _lastSec))
                .Select(w => w.strat)
                .OfType<IStrategyWithStats>();

            StrategyMetrics agg = default;
            foreach (var s in live)
            {
                var m = s.Metrics;
                agg = agg with
                {
                    BuyingPowerUsed = agg.BuyingPowerUsed + m.BuyingPowerUsed,
                    Position = agg.Position + m.Position,
                    Vwap = m.Position > 0 ? m.Vwap : agg.Vwap,
                    PnL = agg.PnL + m.PnL,
                    RealisedPnL = agg.RealisedPnL + m.RealisedPnL
                };
            }

            return agg;
        }
    }
}