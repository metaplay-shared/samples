# Server runtime options

This directory holds the game server's runtime options files. The server loads `Options.base.yaml` and then one
environment file, whose values override the base.

| File | Used by | Contents |
|---|---|---|
| `Options.base.yaml` | Every environment | `Database:MasterVersion: 2` |
| `Options.local.yaml` | A server running locally (`metaplay dev server`) | `WebSockets:ListenHost: "0.0.0.0"`, so other devices on the network can connect, and `WebClientHosting:WebRootPath` pointing at the web client's publish output |
| `Options.dev.yaml` | Development cloud environments, including this project's `Demo` environment (`public-cycles-beam-quickly`) | No overrides |
| `Options.staging.yaml` | Staging cloud environments | No overrides |
| `Options.production.yaml` | Production cloud environments | No overrides |

A local server reads `Config/Options.base.yaml` and `Config/Options.local.yaml` unless the `METAPLAY_OPTIONS`
environment variable names other files. In the cloud, the `metaplay-gameserver` Helm chart picks the environment
file from the environment's family. To change the list for one environment, set `config.files` in
`Backend/Deployments/<environment>-server.yaml`.

The option sections the game defines have their defaults in code. See
[`docs/architecture.md`](../../../docs/architecture.md#runtime-options). For how runtime options work, see
[Working with Runtime Options](https://docs.metaplay.io/game-server-programming/how-to-guides/working-with-runtime-options.html).
