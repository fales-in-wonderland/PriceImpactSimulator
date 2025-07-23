using System;
using System.Globalization;
using System.IO;
using System.Text;
using PriceImpactSimulator.Domain;

namespace PriceImpactSimulator.Persistence;

public sealed class CsvSink : IDisposable
{
    private readonly StreamWriter _trades;
    private readonly StreamWriter _orders;
    private readonly StreamWriter _books;
    private readonly StreamWriter _events;
    private readonly StreamWriter _stats;
    private readonly StreamWriter _strEv;
    
    public CsvSink(string folderUtc)
    {
        Directory.CreateDirectory(folderUtc);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        _trades = Create(Path.Combine(folderUtc, $"trades_{stamp}.csv"),
            "ts,side,price,qty");
        _orders = Create(Path.Combine(folderUtc, $"orders_{stamp}.csv"),
            "ts,orderId,execType,side,price,lastQty,leaves");
        _books  = Create(Path.Combine(folderUtc, $"book_{stamp}.csv"),
            "ts,bidPrice,bidQty,askPrice,askQty");
        _events = Create(Path.Combine(folderUtc, $"events_{stamp}.csv"),
            "ts,message");
        _stats  = Create(Path.Combine(folderUtc, $"stats_{stamp}.csv"),
            "ts,buyPower,position,vwap,pnl");
        _strEv = Create(Path.Combine(folderUtc,$"strategy_events_{stamp}.csv"),
            "ts,strategy,event");

        static StreamWriter Create(string path, string header)
        {
            var sw = new StreamWriter(path, false, Encoding.UTF8);
            sw.WriteLine(header);
            return sw;
        }
    }

    // ---------- API -------------------------------------------------------

    public void LogTrade(in Trade t) =>
        _trades.WriteLine($"{t.Timestamp:O},{t.AggressorSide},{t.Price:F2},{t.Quantity}");

    public void LogExec(in ExecutionReport e) =>
        _orders.WriteLine($"{e.Timestamp:O},{e.OrderId},{e.ExecType},{e.Side}," +
                          $"{e.Price:F2},{e.LastQty},{e.LeavesQty}");

    public void LogBook(in OrderBookSnapshot snap)
    {
        var depth = Math.Max(snap.Bids.Length, snap.Asks.Length);
        for (int i = 0; i < depth; i++)
        {
            string bidPrice = i < snap.Bids.Length ? snap.Bids[i].Price.ToString("F2") : string.Empty;
            string bidQty = i < snap.Bids.Length ? snap.Bids[i].Quantity.ToString() : string.Empty;
            string askPrice = i < snap.Asks.Length ? snap.Asks[i].Price.ToString("F2") : string.Empty;
            string askQty = i < snap.Asks.Length ? snap.Asks[i].Quantity.ToString() : string.Empty;

            _books.WriteLine($"{snap.Timestamp:O},{bidPrice},{bidQty},{askPrice},{askQty}");
        }

        _books.WriteLine($"                                   ");
    }

    public void LogEvent(string message)
        => _events.WriteLine($"{DateTime.UtcNow:O},{message}");

    public void LogStats(DateTime ts, decimal buyPower, int pos, decimal vwap, decimal pnl)
        => _stats.WriteLine($"{ts:O},{buyPower:F2},{pos},{vwap:F2},{pnl:F2}");
    
    public void LogStrategy(DateTime ts, string name, int onOff) =>
        _strEv.WriteLine($"{ts:O},{name},{onOff}");
    
    // ---------- housekeeping ----------
    public void Dispose()
    {
        _trades.Dispose();
        _orders.Dispose();
        _books.Dispose();
        _events.Dispose();
        _stats.Dispose();
        _strEv.Dispose(); 
    }
}