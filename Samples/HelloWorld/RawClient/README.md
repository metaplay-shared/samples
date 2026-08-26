# HelloWorld Raw Client

A minimal "raw" .NET console client for the HelloWorld sample. It compiles the same shared game
logic as the Unity client (`Assets/SharedCode`) and drives it with the engine-agnostic Metaplay
client integration (`MetaplayClientState`), without any game engine.

## Running

1. Start the game server:

   ```bash
   dotnet run --project ../Backend/Server
   ```

2. Run the client:

   ```bash
   dotnet run
   ```

3. Press Enter to click the button (executes the `PlayerClickButton` action against the server),
   or type `q` + Enter to quit.

Guest credentials are persisted in `PersistentData/` under the working directory, so the same
player account is resumed across runs.
