namespace PriceImpactSimulator.Domain;

public sealed record Order
(
    Guid        Id,
    DateTime    Timestamp,
    Side        Side,
    decimal     Price,        
    int         Quantity,     
    OrderType   Type,
    int?        Visible       
)
{
    public Order Reduce(int executedQty)
        => this with { Quantity = Quantity - executedQty };
}