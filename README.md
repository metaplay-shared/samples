# Metaplay Basic Samples

This repository contains basic sample projects for using the Metaplay SDK.

For a full list of all Metaplay samples and their descriptions, see the [Sample Projects](https://docs.metaplay.io/introduction/sample-projects-overview.html) page in the documentation.

## Running the Samples

### Prerequisites

To run the samples, you'll need to get the Metaplay SDK first:

1. Create an account in the [Metaplay Developer Portal](https://portal.metaplay.dev/).

2. Install the [Metaplay CLI](https://github.com/metaplay/cli).

3. Download the Metaplay SDK using the CLI:

   <!-- \todo Fill in SDK version from script -->
   ```shell
   samples$ metaplay init sdk --sdk-version=37
   ```

### Running a Sample

Follow these steps to run any of the samples in this repository.

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
