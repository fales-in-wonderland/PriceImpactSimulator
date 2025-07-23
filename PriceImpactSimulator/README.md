# Price Impact Simulator

This project demonstrates a simple trading simulator with sample strategies used for a coding exercise.

## Structure

- `Domain` – data transfer objects for orders, trades and snapshots.
- `Engine` – order book and market simulator.
- `StrategyApi` – interfaces for strategy development.
- `Strategies` – example strategies built on the API.
- `Host` – simulation runner and scheduler.
- `Persistence` – CSV logging helpers.
- `Tests` – small unit test suite.

## Running

```bash
dotnet restore
dotnet run --project PriceImpactSimulator --configuration Release
```

Logs will appear in the `logs` folder next to the executable.

## Customisation

Implement new strategies under `PriceImpactSimulator.Strategies` and register them in `Program.cs`. Simulator parameters can be adjusted in code.
