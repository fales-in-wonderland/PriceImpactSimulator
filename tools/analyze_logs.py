from __future__ import annotations
import os, re, glob, webbrowser
from pathlib import Path

import pandas as pd
import plotly.graph_objs as go
from plotly.subplots import make_subplots
import plotly.io as pio

# Simple log visualisation
LOG_DIR = Path(r"..\PriceImpactSimulator\bin\Debug\net9.0\logs").resolve()
CANDLE_INTERVAL = "1S"

def latest_stamp() -> str:
    books = sorted(LOG_DIR.glob("book_*.csv"), key=os.path.getmtime)
    if not books:
        raise RuntimeError(f"no logs in {LOG_DIR}")
    return books[-1].stem.split("_", 1)[1]

STAMP = latest_stamp()

def path(kind: str) -> Path:
    p = LOG_DIR / f"{kind}_{STAMP}.csv"
    if not p.exists():
        raise FileNotFoundError(p)
    return p

def load():
    rows, snap, ts_re = [], {}, re.compile(r"^\d{4}-\d\d-\d\dT\d\d:\d\d")
    with path("book").open() as fh:
        for ln in fh:
            if not ts_re.match(ln): continue
            ts, bp,bq, ap,aq = ln.strip().split(",")
            if snap.get("ts") and ts != snap["ts"]:
                rows.append(snap.copy()); snap.clear()
            snap.setdefault("ts", ts)
            if bp:
                snap["bb"]   = float(bp)
                snap["bqty"] = snap.get("bqty",0)+int(bq)
            if ap:
                snap["ba"]   = float(ap)
                snap["aqty"] = snap.get("aqty",0)+int(aq)
        if snap: rows.append(snap)
    book = pd.DataFrame(rows).fillna({"bqty":0,"aqty":0})
    for c in ("bb","ba"):  book[c] = book[c].astype(float)
    for c in ("bqty","aqty"): book[c] = book[c].astype("Int64")
    book["ts"]  = pd.to_datetime(book.ts)
    book["imb"] = (book.bqty-book.aqty)/(book.bqty+book.aqty).replace(0,pd.NA)

    trades = pd.read_csv(path("trades"), parse_dates=["ts"])
    trades.qty = trades.qty.astype(int)
    buys, sells = [g.copy() for _,g in trades.groupby(trades.side)]

    ohlc = (trades.set_index("ts").price
            .resample(CANDLE_INTERVAL).ohlc())
    vol_b = buys .set_index("ts").qty.resample(CANDLE_INTERVAL).sum().rename("buyVol")
    vol_s = sells.set_index("ts").qty.resample(CANDLE_INTERVAL).sum().rename("sellVol")
    vol   = pd.concat([vol_b, vol_s], axis=1).fillna(0)

    stats = pd.read_csv(path("stats"), parse_dates=["ts"]).astype(
        {"buyPower":float,"position":int,"vwap":float,"pnl":float})

    ev = pd.read_csv(path("strategy_events"), parse_dates=["ts"]).sort_values("ts")
    windows = []
    for strat, grp in ev.groupby("strategy"):
        start = None
        for _, row in grp.iterrows():
            if row.event==1 and start is None: start=row.ts
            elif row.event==0 and start is not None:
                windows.append((strat,start,row.ts)); start=None
        if start is not None:
            windows.append((strat,start,ev.ts.max()))
    tl = pd.DataFrame(windows, columns=["strategy","start","end"])
    return book, ohlc, vol, stats, tl

def build_fig(book, ohlc, vol, stats, tl):
    fig = make_subplots(
        rows=3, cols=1, shared_xaxes=True,
        row_heights=[0.50,0.27,0.23],
        vertical_spacing=0.03,
        specs=[[{"secondary_y":False}],
               [{"secondary_y":True}],
               [{"secondary_y":True}]],
        subplot_titles=("Price – 1 s candles",
                        "Market depth & tape",
                        "Strategy metrics"))

    fig.add_trace(go.Candlestick(
        x=ohlc.index, open=ohlc.open, high=ohlc.high,
        low=ohlc.low, close=ohlc.close,
        increasing_line_color="#26e665",
        decreasing_line_color="#ff4136",
        name="OHLC", showlegend=False),
        row=1,col=1)

    fig.add_trace(go.Scatter(
        x=book.ts, y=book.imb, mode="lines",
        name="Imbalance", line=dict(color="#F5A623", width=1.3)),
        row=2,col=1,secondary_y=False)
    fig.add_trace(go.Bar(
        x=vol.index, y=vol.buyVol, name="Buy vol",
        marker_color="rgba(38,230,101,0.55)"),
        row=2,col=1,secondary_y=True)
    fig.add_trace(go.Bar(
        x=vol.index, y=-vol.sellVol, name="Sell vol",
        marker_color="rgba(255,65,54,0.55)"),
        row=2,col=1,secondary_y=True)

    fig.add_trace(go.Scatter(
        x=stats.ts, y=stats.buyPower, name="Buy‑Power €",
        line=dict(color="#1f77b4")),
        row=3,col=1,secondary_y=False)
    fig.add_trace(go.Scatter(
        x=stats.ts, y=stats.position, name="Position",
        line=dict(color="#ff7f0e", dash="dot")),
        row=3,col=1,secondary_y=True)
    fig.add_trace(go.Scatter(
        x=stats.ts, y=stats.pnl, name="PnL €",
        line=dict(color="#17becf", dash="dash")),
        row=3,col=1,secondary_y=True)

    palette = {"LadderLiftStrategy":"#ffaa00",
               "DripFlipStrategy":"#00d2d5"}
    offsets = {s:0.96-i*0.03 for i,s in enumerate(tl.strategy.unique())}
    for strat,start,end in tl.itertuples(index=False):
        color = palette.get(strat,"#888")
        fig.add_vrect(x0=start, x1=end,
                      fillcolor=color, opacity=0.12,
                      line_width=0, layer="below", col=1, row="all")
        mid = start + (end-start)/2
        fig.add_annotation(x=mid, y=offsets[strat], xref="x",
                           yref="paper", text=strat,
                           showarrow=False,
                           font=dict(size=11,color=color),
                           bgcolor="rgba(0,0,0,0.45)", opacity=0.85)

    max_vol = max(vol.buyVol.max(), vol.sellVol.max())
    fig.update_yaxes(range=[-max_vol*1.1,max_vol*1.1],
                     row=2,col=1,secondary_y=True)
    fig.update_yaxes(title_text="Imb.", row=2,col=1,secondary_y=False)

    fig.update_layout(template="plotly_dark",
                      xaxis_rangeslider_visible=False,
                      paper_bgcolor="#000", plot_bgcolor="#000",
                      bargap=0.05, height=900, width=1550,
                      margin=dict(l=60,r=30,t=60,b=60),
                      legend_orientation="h",
                      legend_yanchor="bottom", legend_y=1.02,
                      legend_x=1, legend_xanchor="right",
                      title=f"PriceImpactSimulator – run {STAMP}")

    return fig

if __name__ == "__main__":
    book, ohlc, vol, stats, tl = load()
    fig = build_fig(book, ohlc, vol, stats, tl)

    out = LOG_DIR / f"report_{STAMP}.html"
    html = pio.to_html(fig, include_plotlyjs="cdn", full_html=True)
    html = html.replace(
        "<head>",
        "<head><style>body{background:#000;margin:0;color:#ddd;font-family:Arial,Helvetica,sans-serif}</style>")
    out.write_text(html, encoding="utf-8")
    webbrowser.open(out.as_uri())
    print("saved", out)
