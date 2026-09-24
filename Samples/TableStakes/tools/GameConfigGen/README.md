# GameConfigGen

Builds the game config archives from the sheets in `GameConfigSource/` and writes
`Backend/Server/GameConfig/StaticGameConfig.mpa` and `WebClient/wwwroot/Assets/SharedGameConfig.mpa`. Run from the
repo root:

```bash
dotnet run --project tools/GameConfigGen                 # build and write both archives
dotnet run --project tools/GameConfigGen -- --dry-run    # build and validate, write nothing
dotnet run --project tools/GameConfigGen -- --print Backend/Server/GameConfig/StaticGameConfig.mpa
dotnet run --project tools/GameConfigGen -- --sources <dir> --static <path> --shared <path>
```

The tool does not publish. See [`docs/game-config.md`](../../docs/game-config.md#the-build-tool) for its outputs,
failure modes and project references, and for publishing from the LiveOps Dashboard.
