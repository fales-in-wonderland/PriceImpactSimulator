# Price Impact Simulator

This repository provides a small trading sandbox with a synthetic order book.
Strategies implement a simple interface and are executed by the simulation host.
The engine simulates random order flow so that algorithms can be tested without
external market data.

## Projects

- **Domain** – data models for orders, trades and snapshots.
- **Engine** – order book and market events generator.
- **StrategyApi** – base interfaces for strategies.
- **Host** – simulation runner and scheduler.
- **Persistence** – CSV logger and utilities.
- **Strategies** – sample algorithms.
- **Tools** – basic log visualisation.

## Requirements

- .NET 9 SDK
- Optional: Python 3 with `pandas` and `plotly` for log analysis

## Running

```bash
dotnet run --project PriceImpactSimulator
```

Logs are written to `./logs`.

## Tests

```bash
dotnet test
```

The repository also ships a small log viewer requiring Python 3 with `pandas` and `plotly`:

```bash
python tools/analyze_logs.py
```

The script produces an HTML report inside the `logs` directory.

