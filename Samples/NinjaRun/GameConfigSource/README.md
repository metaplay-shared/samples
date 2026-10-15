# Game config sources

Every designer-tunable value in the game, one CSV file per game config entry — and one JSON file,
`CourseParts.json`, for the entry a sheet cannot hold. The build reads them into a config archive, and a
published archive is the only route any of these numbers takes to a player — no client build, no server
restart. See [`../AGENTS.md`](../AGENTS.md) for the commands.

```bash
dotnet run --project tools/GameConfigGen                            # build both archives
dotnet run --project tools/GameConfigGen -- --dry-run               # build and validate, write nothing
dotnet run --project tools/GameConfigGen -- --print Backend/Server/GameConfig/StaticGameConfig.mpa

# Build a variant somewhere else — a what-if, without touching the committed archives. Every path is taken
# relative to the repo root, wherever the command was run from.
dotnet run --project tools/GameConfigGen \
    -- --sources /tmp/experiment --static /tmp/static.mpa --shared /tmp/shared.mpa
```

**A validation error fails the build.** No archive is written and the command exits non-zero, with the
library, the row and the member named. That holds for both kinds of check: a parse failure throws, and the
SDK's own item validators only report into the build report — the command refuses to write an archive
carrying an error either way. `--dry-run` is the form to run in CI.

## The files

The file name is the entry name declared in `SharedCode/GameConfigs/SharedGameConfig.cs`, and that is not a
convention but the mechanism: the build asks its source for one sheet per declared entry. **Adding an entry
means adding its sheet**, or the build fails — `EveryConfigEntryHasItsSourceFile` in
`Backend/SharedCode.Tests/` says so in one line rather than leaving it to a stack trace.

**One entry is the deliberate exception.** `CourseParts` is read from `CourseParts.json` instead, because a
course part is nested — lists of ground segments, spike beds and bridges — and a sheet's rows cannot hold
that. The entries named in `GameConfigBuild.JsonSourcedEntries` name their own build source in
`GameBuildParameters`, a second file-system source over this same directory that hands the file over as
bytes, and `SharedCode/TimeTrial/CoursePartJson.cs` parses them. It must not be given a sheet as well: a
sheet nobody reads is a trap, so both the build tool's pre-build check and `EveryConfigEntryHasItsSourceFile`
fail on a `CourseParts.csv`, and on a missing `CourseParts.json`.

| File | Holds |
|---|---|
| `Global.csv` | Game-wide tunables, one per row: how long a run ticket stays submittable, how long a map stays in the registry's active set before the oldest of them is replaced, the map offline practice plays, the Orbs each placement pays, and the rise, slope length and run-out length of a composed map's connective climbs |
| `TimeTrialRules.csv` | The movement values the time trial simulation runs on, one per row: pace and gravity, the launch and its charge, the stumble, and the automatic mantle — two fixed step counts around a rise speed, the height cleared above a lip, and the pace a completed mantle sets the runner down at |
| `Skills.csv` | The Skill tree Orbs are spent on: one row per node, with its tier, prerequisite, price, movement modifiers and the icon its medallion is drawn with |
| `CourseParts.json` | The course parts maps are composed from: one object per part, with its geometry, the positions it may be composed into, its look, and notes for the next editor |

`TimeTrialRules.csv` is the one to be careful with: those values are part of what a recording means, so the
server recomputes a submitted run against whatever sheet is published at the time. Retuning them while runs
are open changes what those runs are measured by — see
[How a run is validated](../README.md#how-a-run-is-validated). `Skills.csv` shares that
hazard, because a Skill's modifiers are part of the same values; it expresses them as permille of the base
member rather than as absolute values, so retuning `TimeTrialRules.csv` carries every Skill with it. A
`Skills.csv` column is named for the member it scales, or, for the step-count stat, for the rate it
raises — `FootingPermille`, for the stumble and the mantle's two fixed phases — so renaming the member renames the column. Every permille is more-is-better:
`SkillInfo.UnchangedPermille` leaves a stat alone and above it is a benefit on every column, so the speeds
are multiplied by it and the step counts divided by it. Its
`Icon` column names one `SkillIcon` value (`Tag`, `Sandal`, `Shield`, `Bolt`, `Dash`, `Jump`, `Mountain`)
for the client to draw; the drawings live in the client, so a new icon is a code change and the sheet only
picks among the ones that exist.

**A row dropped from a sheet is a member dropped from the class, and its id is retired rather than reused.**
`TimeTrialRules` holds three retired ids, 9, 10 and 19, from the values a pit's floor and the speed-based
mantle replaced. A stored model or an older client would otherwise read a new value as the old member.

**A change to what a recording means is not a sheet change at all.** It is a bump of
`TimeTrialConstants.RulesetVersion` in code, after which the server refuses recordings made under the
previous version and every ghost pool reseeds — version 16 is the scripted runners planning every jump on a
copy of the simulation. Retuning a value in this
directory never does that on its own; it changes what an open run is recomputed against, which is the softer
hazard above.

## How a sheet is written

- **The header row names members.** A column header is the member name, exactly and case-sensitively;
  `displayname` does not match `DisplayName`. An unknown header fails the build — *unless every cell under it
  is empty*, in which case the column is silently dropped, so a typo'd column nobody has filled in yet will
  pass. (A type may instead take its columns from a constructor tagged
  `[MetaGameConfigBuildConstructor]`, matching parameter names case-insensitively. Nothing here uses one.)
- **One column carries the key**, tagged `#key`. A single-object entry such as `Global.csv` is the
  exception: it is written as `Member,Value` rows instead.
- **A blank cell means "not written here"**, which leaves the member at its C# default.
- **An enum cell is the value's name**, spelled as the enum declares it: `Shield`, not `2`. A blank one is
  the default, the enum's first value.
- **A multi-row item** leaves the key cell empty on its continuation rows, and ends at the next row that
  fills one — the key cell alone decides the grouping. A collection member is written as a `Foo[]` column,
  one element per cell down the item's rows (or across the `Value` span in a `Member,Value` sheet); `Foo[N]`
  addresses one element, and a dotted header (`Schedule.Start.Date`) reaches a nested member. A `Foo[]`
  column does not by itself make an item multi-row.
- **`//` in a header cell drops the column; `//` in the leftmost cell drops the row.** That is where a note
  to the next editor goes, and it is how the marker row at the top of each sheet here is ignored. The test is
  the raw cell value, untrimmed, so the `//` must be the first two characters.
- **A course id cell may name a map, `+` and all.** A map id is its three part ids joined by `+`, which the
  SDK's default reading of a `StringId` stops at; the game registers a parser of its own that admits it, so a
  cell such as `OfflinePracticeMapId` can hold one.

The sheets in this directory are small enough that they exercise only the first four of these. The rest is
written down because the moment a real game config arrives, it needs them.

## How the parts file is written

`CourseParts.json` is one JSON array with one object per part, in the order the parts were authored. The
example is `rr-lowland` with its decorations cut down to two:

```json
[
  {
    "id": "rr-lowland",
    "positions": ["First", "Middle"],
    "isSetPiece": true,
    "ground": [
      { "startX": 0, "endX": 37, "topY": 0 },
      { "startX": 40, "endX": 44, "topY": 0 },
      { "startX": 44, "endX": 49, "topY": -0.9, "pit": true },
      { "startX": 49, "endX": 52, "topY": 0 },
      { "startX": 52, "endX": 69, "topY": -0.79999 },
      { "startX": 69, "endX": 93, "topY": 0 }
    ],
    "hazards": [
      { "startX": 58, "endX": 60 },
      { "startX": 72, "endX": 73 },
      { "startX": 76, "endX": 79 }
    ],
    "bridges": [
      { "startX": 10, "endX": 23 }
    ],
    "decorations": [
      { "kind": "stone-lantern", "x": 24.2, "layer": "Behind" },
      { "kind": "reed-flowers", "x": 32.79999, "layer": "Front" }
    ]
  }
]
```

- **`id` and `isSetPiece` are required, and every array is optional** — `positions`, `ground`, `hazards`,
  `bridges`, `decorations` and `vines` read as empty when left out, as `notes` reads as none. A part with no
  ground or no position is still refused, by the part's own rules. A property the format does not know fails
  the build, so a misspelt name cannot silently drop a value. The part editor writes every array but
  `decorations` and `vines`, which it writes only when the part has some.
- **A part names no look.** Every map is drawn the same way by position — first part in the foothills, second
  on the mid-ridge, last at the summit — so a `style` property, which parts once carried, is refused like any
  other unknown one.
- **`decorations` is an optional array of cosmetic props**, written after `bridges` and only when a part has
  some: `{ "kind": "stone-lantern", "x": 12.5, "layer": "Behind" }`, with `"flip": true` to mirror one. `kind`
  names a kind in the web client's decoration catalogue (`WebClient/wwwroot/js/courseDecorations.js`: today
  `stone-lantern` and `reed-flowers`), `x` is part-local metres on the part's own ground (never over a gap or
  on a bridge deck; the height comes from the ground there), and `layer` is `Behind` or `Front`. They are drawn
  and nothing else, so they change no run.
- **`vines` is an optional array of hanging vines**, written after `decorations` and only when a part has
  some: `{ "anchorX": 20, "anchorY": 8, "platformStartX": 24, "platformEndX": 39, "platformTopY": 3 }`. The
  anchor is where the rope hangs from and the platform is where a good swing lands, all part-local metres
  (heights relative to the part's own level); every field is required. The rope's length and feel are not in
  the file — every vine hangs the one tuned rope. Unlike a decoration a vine changes what a run
  can do, and it keeps rules the build checks: the anchor clear of both end clearances and its whole swing on
  the part, the bottom of the swing clear of the ground under it, the platform ahead of the anchor, above the
  ground, on the part and short of the exit clearance with no spike bed or bridge under it, and the vines in
  order, each clear of the one before.
- **A ground row takes an optional `"pit": true`**, omitted when false, which makes that segment a pit
  floor at its own `topY` instead of ground to run on: `{ "startX": 30, "endX": 36, "topY": -2, "pit": true }`.
  A pit-flagged row must touch the rows on both sides, both strictly higher, cannot be a part's first or last
  ground row, and has no bridge over it. It may carry a spike bed within its span, which a runner who falls
  in lands on and stumbles over before the mantle out. Its depth is the lower neighbour's height minus its own, so
  it is the way to author a pit whose depth is chosen rather than taken from the gap rule. A plain low row
  between two higher ones is terrain, not a pit — the runner drops onto it and steps back up.
- **Lengths are metres, as plain decimal numbers**, in part-local coordinates: X from 0 along the part, Y
  relative to the part's own level. `22` and `22.5` are fine; `2.25e1` is refused. A value is read into
  fixed point from its text, so it is the same on every machine.
- **`positions` names `CoursePartPosition` values**, spelled as the enum declares them: `First`, `Middle`,
  `Last`.
- **`isSetPiece` marks a showcase section of a map.** A composition takes at most one set piece.
- **`notes` is where the authoring reasoning goes** — which pit is meant to be run into, why a part is cut
  the way it is. JSON has no comments, so this is the note to the next editor; it is not carried into the
  archive.
- **A mistake fails the build with the part and the field named**, for example
  `CourseParts.json: part [3] ('rr-crossriver'): ground[1].endX is missing.` — part and array indexes count from 0.
  The part's own rules (clearance at both ends, level ends, ordered geometry) and every map the parts compose
  are then checked by `SharedGameConfig.BuildTimeValidate`.
- **The part editor in the developer course tools writes this shape**: draw a part there, copy the object,
  paste it into the array and rebuild.

**A part's id is permanent and its geometry is immutable once published.** A map id is its part ids joined
by `+`, and every stored recording, personal best and ghost pool is filed against that map id, so editing a
published part in place changes what all of them mean and nothing detects it. New geometry is a new part
under a new id.

## Rules worth keeping

- **Ids are permanent; everything else is not.** Player state and analytics reference rows by id, so a row
  can be repriced or renamed but not re-keyed.
- **A published table is never edited in place** once player state can be part-way through it. Retuning
  means authoring a new id and pointing a `Global.csv` row at it, so a player keeps what they were promised.
- **Semantics are code, values are here.** What a currency or an objective *is* is a compatibility contract
  between the server and every stored player state. What these files decide is how much, how many, and which
  one.

## The over-the-air demo

`TimeTrialRules.csv`'s `RunSpeed` is the shortest demonstration in the repo: change it, build, publish the
archive from the LiveOps Dashboard on a running server, and the next session's runner moves at the new pace —
no client build, no restart. Read the result back from the archive the server hands out rather than from the
file it was built from, which is what `--print` is for.

The build tool does not publish. Making an archive active is a Dashboard action, which owns the
authentication and the audit trail for a change that takes effect immediately for every session that
follows.
