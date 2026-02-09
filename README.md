
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

Open http://localhost:5027
 in four different browsers or browser profiles.
Each browser represents one player in the game.

# Contributing

We really like people helping us with the project. Nevertheless, take your time to read our contributing guidelines [here](./CONTRIBUTING.md).

## License
GPLv3
