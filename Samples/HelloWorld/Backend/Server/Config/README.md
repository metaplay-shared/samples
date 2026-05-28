# Server Runtime Options

## Overview

This directory contains the Runtime Options files for the various cloud environments of your game.

For more information on configuring your game server using the runtime options, take a look at [Working with Runtime Options](https://docs.metaplay.io/game-server-programming/how-to-guides/working-with-runtime-options.html).

## Default Options Files

The following runtime options files are generated for new projects by default:

* `Options.base.yaml` contains project-wide options and is used by all cloud environments and when the server running locally.
* `Options.local.yaml` contains options overrides for when the server is run locally.
* `Options.dev.yaml` contains options overrides for all development cloud environments (by default, the `develop` and `stable` environments).
* `Options.staging.yaml` contains options overrides for the `staging` environment.
* `Options.production.yaml` contains options overrides for the `production` environment.

You can use these options files to configure your game server differently in the various environments.

## Custom Environment Options

Each environment can have a Helm values file (e.g. `Backend/Deployments/*-server.yaml`) that specifies which runtime options files to load, along with other deployment configuration. To register a deployment file for an environment, add a `serverValuesFile` entry to `metaplay-project.yaml`. Assuming we have `mycustomenv` which requires a custom Options file:

```yaml
environments:
  - name: MyCustomEnv
    humanId: my-custom-env
    type: development
    stackDomain: p1.metaplay.io
    serverValuesFile: Backend/Deployments/mycustomenv-server.yaml
```

Then in the deployment file, we can configure which options to use:

```yaml
config:
  files:
    - "./Config/Options.base.yaml"
    - "./Config/Options.mycustomenv.yaml"
```
