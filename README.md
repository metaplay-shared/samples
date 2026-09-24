# Metaplay Basic Samples

This repository contains basic sample projects for using the Metaplay SDK.

For a full list of all Metaplay samples and their descriptions, see the [Sample Projects](https://docs.metaplay.io/introduction/sample-projects-overview.html) page in the documentation.

## Running the Samples

### Prerequisites

This repository stores images and other binaries with [Git LFS](https://git-lfs.com/). Install it before cloning (`git lfs install`), or the binaries check out as small pointer files. Downloading the repository as a ZIP from GitHub includes the real files.

To run the samples, you'll need to get the Metaplay SDK first:

1. Create an account in the [Metaplay Developer Portal](https://portal.metaplay.dev/).

2. Install the [Metaplay CLI](https://github.com/metaplay/cli).

3. Download the Metaplay SDK using the CLI:

   <!-- \todo Fill in SDK version from script -->
   ```shell
   samples$ metaplay init sdk --sdk-version=38
   ```

### Running a Sample

Follow these steps to run any of the Unity-based samples in this repository. `HelloBlazorWasm` runs in the browser instead of Unity — see [Running HelloBlazorWasm](#running-helloblazorwasm) below.

1. Start the sample project server:

    ```shell
    Samples/<Sample>$ metaplay dev server
    ```

2. Check out the the sample project's LiveOps Dashboard:

    * The Dashboard is available at [http://localhost:5550](http://localhost:5550).

3. Run the project in Unity:

    * Open the sample project (`Samples/<Sample>`) in Unity.
    * Open Unity menu **Metaplay** → **Environment Configs**, and set **Active Environment** to **Localhost** to ensure the client connects to the locally running server.
    * Press **Play** in Unity to run the client within the Unity Editor.

### Running HelloBlazorWasm

`HelloBlazorWasm` is a preview sample whose client is written in C# with Blazor WebAssembly and runs entirely in the browser, so it has no Unity project and the Unity step above does not apply. It also needs its serializer assembly generated before the client is run, and connects to the server over WebSocket rather than TCP.

See [`Samples/HelloBlazorWasm/README.md`](Samples/HelloBlazorWasm/README.md) for its build and run steps.
