// execution report
public enum ExecType { New, Trade, Cancel, Reject }

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