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

The per-environment Helm values files in your project's `Backend/Deployments/*-server.yaml` specify which runtime options are in use for each environment. You can also modify the Helm values files to configure which environment uses which runtime options files.

You can use these options files to configure your game server differently in the various environments.
