# Price Impact Simulator

This repository provides a small trading sandbox with a synthetic order book.
Strategies implement a simple interface and are executed by the simulation host.

## Projects

- **Domain** – data models for orders, trades and snapshots.
- **Engine** – order book and market events generator.
- **StrategyApi** – base interfaces for strategies.
- **Host** – simulation runner and scheduler.
- **Persistence** – CSV logger and utilities.
- **Strategies** – sample algorithms.
- **Tools** – basic log visualisation.

## Running

```bash
dotnet run --project PriceImpactSimulator
```

Logs appear in `./logs`.

## Tests

```bash
dotnet test
```
