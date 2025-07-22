// File: CsvSink.cs
using System;
using System.Globalization;
using System.IO;
using System.Text;
using PriceImpactSimulator.Domain;

namespace PriceImpactSimulator.Persistence;

/// <summary>Пишет события симуляции в CSV c меткой времени запуска.</summary>
public sealed class CsvSink : IDisposable
{
    private readonly StreamWriter _trades;
    private readonly StreamWriter _orders;

    public CsvSink(string folderUtc)
    {
        Directory.CreateDirectory(folderUtc);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        _trades = Create(Path.Combine(folderUtc, $"trades_{stamp}.csv"),
            "ts,side,price,qty");
        _orders = Create(Path.Combine(folderUtc, $"orders_{stamp}.csv"),
            "ts,orderId,execType,side,price,lastQty,leaves");

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

    // ---------- housekeeping ----------
    public void Dispose()
    {
        _trades.Dispose();
        _orders.Dispose();
    }
}