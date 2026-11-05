# Cloud deployment

The game server and the web client deploy separately, as the Blazor starter project's
[publishing guide](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/publishing.md#deploying-to-the-cloud)
describes: `metaplay build image` and `metaplay deploy server` for the server, which includes the custom dashboard,
and `dotnet publish` and `metaplay deploy webclient` for the client, which goes to the environment's CDN.

No Helm values file is needed: the environment's defaults serve the WebSocket gateway (9380) the client connects to.
A development environment runs with `Options.base.yaml` and `Options.dev.yaml`; the chart supplies the
environment-specific runtime settings and authentication configuration. `Options.local.yaml`, with its test-friendly
match deadlines, is for local runs only.

The game server answers the root of its public web host with a redirect to the client on the CDN, keeping the query
(`Backend/Server/WebClientRedirect/`).

## Verification

`metaplay deploy server` checks pod readiness, gateway connectivity and dashboard availability, but not a real browser
session. Follow it with a browser smoke check: connect a fresh account, inspect the collection, and start Practice and
play through the opening hand.

The match actors are ephemeral: a deployment ends the matches in progress. Player accounts persist separately.
