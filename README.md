# Ship Mesh Simulator

A learning project that asks: if real ships had mesh radios, how would the network behave?

It will show live ships on a map. Each ship acts as an emulated mesh node, passing messages to a shore station one hop at a time and finding another route if a ship fails.

## What's real and what's emulated

| Part | Status |
|---|---|
| Ship identity, position, speed, and course | Real, live AIS data from [aisstream.io](https://aisstream.io) |
| Which ships are in range | Calculated from real distances and a chosen radio range |
| Signal strength, messages, routing, and failures | Emulated by this app, with no real radios involved |

## Status

- [x] Milestone 1, Connect: print live ship positions in the terminal
- [x] Milestone 2, Track: keep each ship's latest position and remove stale ships
- [x] Milestone 3, Map: show ships on a live map
- [x] Milestone 4, Link: draw links between ships in range
- [x] Milestone 5, Relay: pass messages hop by hop
- [x] Milestone 6, Recover: kill a node and reroute
- [ ] Milestone 7, Back up: replay saved ship data
- [ ] Milestone 8, Polish: tests, diagram, and demo video

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a free [aisstream.io](https://aisstream.io) API key.

Save your key privately. This stores it outside the project, so it can't be committed:

```
dotnet user-secrets set "AisStream:ApiKey" "YOUR_KEY"
```

Then start the app and open <http://localhost:5000> in your browser. Press `Ctrl+C` in the terminal to stop it.

```
dotnet run
```

## How it was built

This project is being built with Claude, an AI assistant, as a way to learn C# and mesh networking concepts.
