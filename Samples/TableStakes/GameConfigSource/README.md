# Game config sources

The CSV sheets the game config is built from. Each file holds one entry of `SharedGameConfig` and is named after it,
for example `Cosmetics.csv` for the `Cosmetics` entry.

Build the archives from the repo root after editing a sheet, and commit both files the build writes:

```bash
dotnet run --project tools/GameConfigGen
dotnet run --project tools/GameConfigGen -- --dry-run   # validate only
```

A validation error fails the build and writes nothing.

See [`docs/game-config.md`](../docs/game-config.md) for how the config is structured, validated, built and
published, and for the editing rules.
