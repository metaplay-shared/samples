# Metaplay Samples

This repository contains some basic sample projects using the Metaplay SDK.

For a full merge-2 game implemented using Metaplay, see the [Project Orca repository](https://github.com/metaplay-shared/orca).

## List of Samples

<!-- TODO: Fill in proper list -->
- HelloWorld
- Idler
- HelloNFT

<!-- TODO: Link to docs pages for more information about individual samples? -->

## Running the Samples

### Prerequisites

To run the samples, you'll need to get the Metaplay SDK first:

1. Create an account in the [Metaplay Developer Portal](https://portal.metaplay.dev/).

2. Install the [Metaplay CLI](https://github.com/metaplay/cli).

3. Download the Metaplay SDK using the CLI:

    <!-- \todo Fill in SDK version from script -->
    ```shell
    samples$ metaplay init sdk --sdk-version=35
    ```

### Sample: Idler

First, start the Idler server with:

```shell
samples/Samples/Idler$ metaplay dev server
```

The LiveOps Dashboard for the sample is now available at http://localhost:5550.

Now, open the project (`Samples/Idler`) in Unity. To connect to the locally running server, open Unity menu **Metaplay** → **Environment Configs**, and set **Active Environment** to **Localhost**.
