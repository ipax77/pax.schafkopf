
[![.NET](https://github.com/ipax77/pax.schafkopf/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/ipax77/pax.schafkopf/actions/workflows/dotnet.yml)

# pax.Schafkopf

A sample implementation of the traditional Bavarian card game **Schafkopf**, built as a real-time multiplayer web application using **Blazor** and **SignalR**.

The project serves as a reference for building interactive, stateful web applications with modern .NET.

## Website
[schafkopf.pax77.org](https://schafkopf.pax77.org)
![Game table screenshot](/images/table.png)

## Getting Started

> Requires **.NET 10 SDK**

### Clone the repository
```bash
git clone https://github.com/ipax77/pax.schafkopf.git
```

### Start the API
Start the backend API first:
```bash
cd pax.schafkopf/src/sk.api
dotnet run
```

### Start the Web App
In a second terminal:
```bash
cd pax.schafkopf/src/sk.pwa
dotnet run
```

Open http://localhost:5027, set a nickname, and create a table. Click
**Freie Plätze mit Bots füllen** to fill the empty seats with conservative heuristic bots.
For a mixed table, invite other humans before filling the remaining seats. Each human
uses a separate browser or browser profile. Bots automatically ready after each deal;
click **Weiter** in the result dialog to start the next one. Remove bots in that dialog
between rounds to make room for friends.

## Headless engine and bots

The [WSL training plan](docs/training-plan.md) defines separate bidding and playing models,
staged PPO training, and the implementation milestones.

The [bot and evaluation guide](training/README.md) covers the implemented heuristic policies,
WSL environment checks, seeded comparisons, and replay commands. Live bot seats use
`heuristic-v1/conservative`; neural training is not enabled yet.

See [the engine contract](docs/engine.md) for deterministic simulation, supported rules,
stable action IDs, seat-limited observations, and live bot integration hooks.
Run the simulation check with `dotnet run --project tools/sk.simulation-audit -c Release -- 1000000`.
See [engine benchmarks](src/sk.benchmarks/README.md) for repeatable throughput/allocation measurements
and the results of the first two optimization rounds.

# Contributing

We really like people helping us with the project. Nevertheless, take your time to read our contributing guidelines [here](./CONTRIBUTING.md).

## License
GPLv3
