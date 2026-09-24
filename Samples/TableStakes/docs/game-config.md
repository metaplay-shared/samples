# Game config

[Documentation index](../README.md#documentation) · Read first: [`architecture.md`](architecture.md)

Table Stakes keeps every tunable value in Metaplay game config. Values are authored as CSV sheets in
`GameConfigSource/`, built into config archives by `tools/GameConfigGen`, and published over the air from the LiveOps
Dashboard. This doc covers how the config is structured, validated and built, and the rules for editing it. The
commands are in [`AGENTS.md`](../AGENTS.md#game-config).

## Config classes

[`SharedGameConfig`](../SharedCode/GameConfigs/SharedGameConfig.cs) holds every entry. The game has no server-only
config class and no custom build pipeline. Each entry is one sheet in `GameConfigSource/` of the same name. The sheet
syntax is the SDK's ([Working with Game Config Data](https://docs.metaplay.io/feature-cookbooks/game-configs/working-with-game-config-data)).

Every entry is optional in the archive, so adding an entry does not make an older archive unloadable. The load-time
check refuses an archive that lacks what the game reads instead ([Load time](#load-time)).

[`GlobalConfig`](../SharedCode/GameConfigs/GlobalConfig.cs) holds the values that exist once, such as the starting
wallet and the wallet caps, and the pointers that select the active version of each versioned table. A pointer is a
`MetaRef`, so a pointer at a table that does not exist fails the build instead of the player's session.

Some item types take their columns from a build constructor, which also derives values that are not authored, such as an
in-app product's development id or an offer's precursor condition.
[`GameConfigParsers.cs`](../SharedCode/GameConfigs/GameConfigParsers.cs) parses the game's own cell types: currency
amounts, reward bundles and player property names.

The card game's rules read no config. A table copies its bot profiles and names when it is formed, so a publish affects
the next table, not one in progress.

## Validation

### Build time

On every build the SDK validates the config and then calls
[`GameConfigValidation.Validate`](../SharedCode/GameConfigs/GameConfigValidation.cs). Items that can check themselves
implement `IValidatedConfigItem` and are registered there. Checks that need more than one library, such as the economy
checks ([`economy.md`](economy.md)), are functions in the same file. Each feature doc states the design its checks
protect.

Every problem is collected before the build fails, so one build reports all of them. Each message names the library,
the item and the member, and both the LiveOps Dashboard and the build tool render the report per row.

### Load time

`SharedGameConfig.Validate` runs when an archive is imported, on the server and the client, and calls
`ThrowIfArchiveIsOutdated`. It refuses an archive that is missing an entry the game reads or leaves a required value
unset. Every entry is optional, so an archive built before an entry existed still imports, and without this check the
first session to follow the missing value would fail.

## The build tool

[`tools/GameConfigGen`](../tools/GameConfigGen/Program.cs) builds the sheets and writes two archives:

- `Backend/Server/GameConfig/StaticGameConfig.mpa`, the full archive the server starts with.
- `WebClient/wwwroot/Assets/SharedGameConfig.mpa`, the client's built-in copy, which offline mode loads. A client
  connected to a server downloads the active config from the CDN instead.

The game reads these archives, not the sheets, so a sheet edit has no effect until the tool runs. Commit both after
changing a sheet or a config class. On a merge conflict in an archive, take either side, run the tool, and commit what
it writes. The tool can also print an archive's contents, which shows what an archive really holds rather than what
its sheets say.

The tool drives the SDK's builder itself, because the SDK's own tool helper writes output as it goes and only prints a
failed build. Here a failed build leaves the previous archives on disk.

### Failure modes

A failed build writes nothing and exits with an error. There are three ways to fail:

1. **A missing sheet.** Every entry needs a sheet of the same name. The tool checks this first and lists the missing
   files, because the SDK would otherwise fail inside the entry's build step with a stack trace.
2. **A build that throws.** A parse error or a failed game check throws, and the tool prints the build report.
3. **An error the SDK only reports.** Some of the SDK's own item validators add an error to the build report without
   throwing, and the build returns an archive. The tool reads the report's highest message level and refuses the
   archive at `Error`. A build that checked only for exceptions would write that archive.

### Project references

[`GameConfigGen.csproj`](../tools/GameConfigGen/GameConfigGen.csproj) references the analyzer projects, because SDK
integration discovery reads source-generated type info from the entry assembly. It references `Backend/Server`, not
only the shared code, so the build loads the server's integration types. The integration registry uses the most
derived implementation it finds, and the server's player requirements validator reads the bot names from the active
config.

## Checking the committed archives

`GameConfigBuildTests` in `Backend/Server.Tests` builds the sheets with the same parameters as the tool and compares the
result with both committed archives, entry by entry. It compares pretty-printed items, not bytes, because every build
writes its own timestamp and version hash, so identical content produces different files and `git diff` on an archive
says nothing. The same tests pin a few shipped values, so changing one of those in a sheet also needs the test updated.

## Publishing over the air

The build tool does not publish. Publishing an archive is a LiveOps Dashboard action
([Managing Game Configs from the Dashboard](https://docs.metaplay.io/feature-cookbooks/game-configs/managing-game-configs-from-the-dashboard)).
Sessions that start after the publish receive the new config, with no server restart and no client rebuild.

A deployed server holds the built archive, not `GameConfigSource/`. A config build started from the Dashboard on a
deployed server needs a source it can reach, such as Google Sheets.

## Editing rules

- **Published tables are never edited.** The versioned tables, the ones `Global` selects with a pointer, get a new id
  and the pointer moves to it. Player state holds some of these ids: a player keeps the first-week schedule they
  started, and a tournament run keeps the reward table of the season it joined.
- **The cosmetic catalogue is append-only.** Retire an item by making it not purchasable. A deleted row makes owned
  items unresolvable.
- **The bot name roster is append-only in practice.** A removed name becomes available to players while a table may
  still show it.
- **Ids are permanent.** Change a row's other columns instead.
- **Semantics are code.** Currency types, mission objectives and cadences, and cosmetic kinds are enums in code.
  Config decides amounts, counts and which item applies.
- **Config items are read-only.** The SDK detects run-time mutation of config data, so collections are exposed as
  read-only views.
- **A new entry needs a sheet** of the same name, or the build fails.
