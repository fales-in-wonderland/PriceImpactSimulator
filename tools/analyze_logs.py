"""generate html report from csv logs"""
import glob, os, re, webbrowser
from pathlib import Path
from itertools import cycle
import pandas as pd
import plotly.graph_objs as go
from plotly.subplots import make_subplots
import plotly.io as pio

LOG_DIR = Path(r"..\PriceImpactSimulator\bin\Debug\net9.0\logs").resolve()
CANDLE_INTERVAL = "1S"

def latest_stamp() -> str:
    book_files = sorted(LOG_DIR.glob("book_*.csv"), key=os.path.getmtime)
    if not book_files:
        raise RuntimeError("no log files found")
    return book_files[-1].stem.split("_", 1)[1]

RUN_STAMP = latest_stamp()

def file(name: str) -> Path:
    path = LOG_DIR / f"{name}_{RUN_STAMP}.csv"
    if not path.exists():
        raise FileNotFoundError(path)
    return path

def load():
    rows, snap, ts_re = [], {}, re.compile(r"^\d{4}-\d\d-\d\dT\d\d:\d\d")
    with file("book").open() as fh:
        for ln in fh:
            if not ts_re.match(ln):
                continue
            ts, bp, bq, ap, aq = ln.strip().split(",")
            if snap.get("ts") and ts != snap["ts"]:
                rows.append(snap.copy()); snap.clear()
            snap.setdefault("ts", ts)
            if bp:
                snap["bb"]   = float(bp)
                snap["bqty"] = snap.get("bqty", 0) + int(bq)
            if ap:
                snap["ba"]   = float(ap)
                snap["aqty"] = snap.get("aqty", 0) + int(aq)
        if snap:
            rows.append(snap)
    book = pd.DataFrame(rows).astype({"bb":float,"ba":float,"bqty":int,"aqty":int})
    book["ts"] = pd.to_datetime(book.ts)
    book["imb"] = (book.bqty - book.aqty) / (book.bqty + book.aqty).replace(0, pd.NA)
    trades = pd.read_csv(file("trades"), parse_dates=["ts"])
    trades.qty = trades.qty.astype(int)
    buys, sells = [df.copy() for _, df in trades.groupby(trades.side)]
    ohlc = (trades
            .set_index("ts")
            .price
            .resample(CANDLE_INTERVAL)
            .ohlc())
    vol_b = buys .set_index("ts").qty.resample(CANDLE_INTERVAL).sum().rename("buyVol")
    vol_s = sells.set_index("ts").qty.resample(CANDLE_INTERVAL).sum().rename("sellVol")
    vol   = pd.concat([vol_b, vol_s], axis=1).fillna(0)
    stats = pd.read_csv(file("stats"), parse_dates=["ts"]).astype({"buyPower":float,"position":int,"vwap":float,"pnl":float})
    ev = pd.read_csv(file("strategy_events"), parse_dates=["ts"])
    ev = ev.sort_values("ts")
    intervals = []
    for strat, grp in ev.groupby("strategy"):
        grp = grp.reset_index(drop=True)
        start = None
        for _, row in grp.iterrows():
            if row.event == 1 and start is None:
                start = row.ts
            elif row.event == 0 and start is not None:
                intervals.append(dict(strategy=strat, start=start, end=row.ts))
                start = None
        if start is not None:
            intervals.append(dict(strategy=strat, start=start, end=ev.ts.max()))
    timeline = pd.DataFrame(intervals)
    return book, ohlc, vol, stats, timeline

def build_fig(book, ohlc, vol, stats, timeline):
    fig = make_subplots(rows=4, cols=1, shared_xaxes=True,
        row_heights=[0.40, 0.25, 0.23, 0.12],
        vertical_spacing=0.03,
        specs=[[{"secondary_y": False}],
               [{"secondary_y": True}],
               [{"secondary_y": True}],
               [{"secondary_y": False}]],
        subplot_titles=("Price – 1s candles","Market depth & tape","Strategy metrics","Strategy timeline"))
    fig.add_trace(go.Candlestick(x=ohlc.index, open=ohlc.open, high=ohlc.high,
        low=ohlc.low, close=ohlc.close, increasing_line_color="#26e665",
        decreasing_line_color="#ff4136", name="OHLC", showlegend=False), row=1, col=1)
    fig.add_trace(go.Scatter(x=book.ts, y=book.imb, mode="lines", name="Imbalance",
        line=dict(color="#F5A623", width=1.3)), row=2, col=1, secondary_y=False)
    fig.add_trace(go.Bar(x=vol.index, y=vol.buyVol, name="Buy vol",
        marker_color="rgba(38,230,101,0.55)"), row=2, col=1, secondary_y=True)
    fig.add_trace(go.Bar(x=vol.index, y=-vol.sellVol, name="Sell vol",
        marker_color="rgba(255,65,54,0.55)"), row=2, col=1, secondary_y=True)
    fig.add_trace(go.Scatter(x=stats.ts, y=stats.buyPower, name="Buy-Power €",
        line=dict(color="#1f77b4")), row=3, col=1, secondary_y=False)
    fig.add_trace(go.Scatter(x=stats.ts, y=stats.position, name="Position",
        line=dict(color="#ff7f0e", dash="dot")), row=3, col=1, secondary_y=True)
    fig.add_trace(go.Scatter(x=stats.ts, y=stats.pnl, name="PnL €",
        line=dict(color="#17becf", dash="dash")), row=3, col=1, secondary_y=True)
    strategies = timeline.strategy.unique().tolist()
    fig.update_yaxes(type="category", categoryorder="array",
                     categoryarray=strategies, row=4, col=1)
    palette = {"LadderLiftStrategy": "#ffaa00", "DripFlipStrategy": "#00d2d5"}
    for _, row in timeline.iterrows():
        color = palette.get(row.strategy, "#888")
        fig.add_trace(go.Scatter(x=[row.start, row.end], y=[row.strategy, row.strategy],
            mode="lines", line=dict(color=color, width=14), hoverinfo="text",
            text=f"{row.strategy}: {row.start.time()} -> {row.end.time()}", showlegend=False),
            row=4, col=1)
    max_vol = max(vol.buyVol.max(), vol.sellVol.max())
    fig.update_yaxes(range=[-max_vol*1.1, max_vol*1.1], row=2, col=1, secondary_y=True)
    fig.update_yaxes(title_text="Imb.", row=2, col=1, secondary_y=False)
    fig.update_yaxes(showticklabels=False, row=4, col=1)
    fig.update_layout(template="plotly_dark", xaxis_rangeslider_visible=False,
        paper_bgcolor="#000", plot_bgcolor="#000", bargap=0.05, height=1000, width=1550,
        margin=dict(l=60, r=30, t=60, b=60), legend_orientation="h",
        legend_yanchor="bottom", legend_y=1.02, legend_x=1, legend_xanchor="right",
        title=f"PriceImpactSimulator – run {RUN_STAMP}")
    return fig

if __name__ == "__main__":
    book, ohlc, vol, stats, tl = load()
    fig = build_fig(book, ohlc, vol, stats, tl)
    out_html = LOG_DIR / f"report_{RUN_STAMP}.html"
    html = pio.to_html(fig, include_plotlyjs="cdn", full_html=True)
    html = html.replace("<head>", "<head><style>body{background:#000;margin:0;color:#ddd;font-family:Arial,Helvetica,sans-serif}</style>")
    out_html.write_text(html, encoding="utf-8")
    webbrowser.open(out_html.as_uri())
    print(f"Saved {out_html}")
