# Server runtime options

This directory holds the game server's runtime options files. The server loads `Options.base.yaml` and then one
environment file, whose values override the base.

| File | Used by | Contents |
|---|---|---|
| `Options.base.yaml` | Every environment | `Database:MasterVersion: 2` |
| `Options.local.yaml` | A server running locally (`metaplay dev server`) | `WebSockets:ListenHost: "0.0.0.0"`, so other devices on the network can connect |
| `Options.dev.yaml` | Development cloud environments, including this project's `Demo` environment (`public-cycles-beam-quickly`) | No overrides |
| `Options.staging.yaml` | Staging cloud environments | No overrides |
| `Options.production.yaml` | Production cloud environments | No overrides |

For details and customization, see [Customize Runtime Options Sources](https://docs.metaplay.io/cloud-deployments/advanced-topics/configuring-a-deployment.html#customize-runtime-options-sources).

The option sections the game defines have their defaults in code. See
[`docs/architecture.md`](../../../docs/architecture.md#runtime-options). For how runtime options work, see
[Working with Runtime Options](https://docs.metaplay.io/game-server-programming/how-to-guides/working-with-runtime-options.html).
