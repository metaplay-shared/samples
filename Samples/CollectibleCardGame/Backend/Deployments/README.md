# Cloud deployment

The standard SDK container includes the server, bot client and custom dashboard. The sample's wrapper adds the published
Blazor browser client at `/gameserver/publicwebapp`.

From the sample root:

```bash
Tools/build-cloud-image.sh [<image-tag>]
metaplay deploy server <environment> <project-id>:<image-tag>
```

The image is named after the project ID in `metaplay-project.yaml`, the tag defaults to a UTC timestamp, and the
script prints the deploy command for the image it built.

The build uses `amd64` by default; `STICKYPAWS_IMAGE_ARCHITECTURE` can select another supported architecture. It
publishes the browser client in Release and excludes checkout-specific development settings. Its temporary Docker
context contains only the public browser files, so local credentials and unrelated repository files cannot enter that
layer. The script moves aside any ignored `bin\Debug` output left by EF on macOS; that literal-backslash folder breaks
MSBuild file enumeration when copied into a Linux build container.

No Helm values file is needed: the environment's defaults serve the public web API that hosts the browser client, and
the WebSocket gateway (9380) it connects to. A development environment runs with `Options.base.yaml` and
`Options.dev.yaml`; the chart supplies the environment-specific runtime settings and authentication configuration. Do
not include `Options.local.yaml` in cloud config: its test-friendly match deadlines and local paths are for local runs
only. The client derives its cloud WebSocket and asset-CDN hosts from the public page hostname.

## Endpoints

- Game: `https://<environment-public-host>/`
- Dashboard: `https://<environment-admin-host>/`

## Browser cache updates

The entry HTML always revalidates. The server stamps its local script and stylesheet URLs, including the Blazor loader
and `dotnet.js`, with a content-derived version, which changes when the published entry points or assembly manifest
change. Only framework files with content hashes in their names receive year-long immutable caching; stable URLs use
`no-cache` and revalidate. A normal reload picks up a deployed client; a tab already running the game keeps its loaded
client until reloaded.

## Verification

`metaplay deploy server` checks pod readiness, gateway connectivity and dashboard availability, but not Blazor asset
loading or a real browser WebSocket session. Follow it with a browser smoke check: connect a fresh account, inspect the
collection, and start Practice and play through the opening hand.

The match actors are ephemeral: a deployment interrupts active matches. Player accounts persist separately.
