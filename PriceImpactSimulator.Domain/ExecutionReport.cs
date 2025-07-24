
namespace PriceImpactSimulator.Domain;

public enum ExecType { New, Trade, Cancel, Reject }

// Fill or cancel report sent to the strategy
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