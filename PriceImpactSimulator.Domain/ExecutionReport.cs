// File: ExecutionReport.cs
namespace PriceImpactSimulator.Domain;

public enum ExecType { New, Trade, Cancel, Reject }

/// <summary>Fill / state‑change notification delivered to a strategy.</summary>
public sealed record ExecutionReport
(
    Guid         OrderId,
    ExecType     ExecType,
    Side         Side,
    decimal      Price,      
    int          LastQty,    
    int          LeavesQty,
    DateTime     Timestamp
);