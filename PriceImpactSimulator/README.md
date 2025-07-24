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

Logs appear in `./results` when using the default parameters.

## Tests

```bash
dotnet test
```

The repository also ships a small log viewer requiring Python 3 with `pandas` and `plotly`:

```bash
python tools/analyze_logs.py
```

See [`results/report_20250724_011030.html`](../results/report_20250724_011030.html) for an example output.
