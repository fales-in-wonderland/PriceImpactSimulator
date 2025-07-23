// order command
﻿using PriceImpactSimulator.Domain;

namespace PriceImpactSimulator.StrategyApi;


public sealed record OrderCommand
(
    CommandType Type,
    Guid        OrderId,
    Side        Side     = Side.Buy,
    decimal     Price    = 0m,
    int         Quantity = 0
)
{
    public static OrderCommand New(Guid id, Side side, decimal price, int qty) =>
        new(CommandType.New, id, side, price, qty);

    public static OrderCommand Cancel(Guid id) =>
        new(CommandType.Cancel, id);
}