# Server Runtime Options

## Overview

This directory contains the runtime options files for running your game server locally and in the cloud.

For more information on configuring your game server using the runtime options, take a look at [Working with Runtime Options](https://docs.metaplay.io/game-server-programming/how-to-guides/working-with-runtime-options.html).

## Default Options Files

The following runtime options files are generated for new projects by default:

* `Options.base.yaml` contains project-wide options and is used by all cloud environments and when the server runs locally.
* `Options.local.yaml` contains options overrides for when the server is run locally.
* `Options.dev.yaml` contains options overrides for all development cloud environments.
* `Options.staging.yaml` contains options overrides for staging cloud environments.
* `Options.production.yaml` contains options overrides for production cloud environments.

For details and customization, see [Customize Runtime Options Sources](https://docs.metaplay.io/cloud-deployments/advanced-topics/configuring-a-deployment.html#customize-runtime-options-sources).
