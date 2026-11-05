# WebClient

The Table Stakes game client. It is a Blazor WebAssembly app (`net10.0-browser`) that runs the Metaplay client in the browser and connects to the game server over a WebSocket. It builds on `WebClientBase` and `SharedCode.Client` (`../SharedCode/SharedCode.Client.csproj`).

Run these from the project root (`Samples/TableStakes`):

```bash
dotnet run --project WebClient/WebClient.csproj          # dev server on http://localhost:5290
dotnet build WebClient/WebClient.csproj                  # also generates the pre-built serializer
dotnet publish WebClient/WebClient.csproj -c Release     # trimmed client, which `metaplay deploy webclient` uploads to the CDN
```

Start a game server first with `metaplay dev server`, or open `http://localhost:5290/?offline` to play without one.

Choosing the game server, the connection service, the serializer, offline mode, trimming, CDN hosting and home-screen install are described in [`docs/web-client.md`](../docs/web-client.md).
