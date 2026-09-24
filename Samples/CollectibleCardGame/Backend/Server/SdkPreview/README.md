# `SdkPreview` — a vendored copy of the SDK's multiplayer-entity actor base

**This is not sample code. It is a copy of SDK source, and it is temporary.**

Everything in this folder is copied from `MetaplaySDK/Backend/Server/MultiplayerEntity/` at **SDK 38.0.0**, plus the
SDK change that adds per-member timeline operations (`ExecuteActionPerMember`, and the
`MultiplayerEntityTimelineOverrideBuffer` that substitutes the no-op for every other member). That change is not in
the R38 release. Read this folder as SDK code that happens to live here.

Re-syncing means copying the files again and re-applying the three changes below; do not hand-patch.

## Why it exists

The match delivers each seat its own hand, which is private to that seat. Doing that on the
replicated timeline — so the private half is ordered against the public half for free — needs
`ExecuteActionPerMember`, which is planned for SDK **R39**. R38 is released and takes no minor
version updates, and the mechanism cannot be reimplemented from game code: the substitution
happens inside the SDK's private flush path (`FlushActionOverrides`, `TryGatherFlushActions`,
`SendContentsToClient`), and no seam on `MultiplayerEntityActorBase` is `virtual`.

So the sample carries the R39 base class until R39 exists.

## How it goes away

**The build will tell you.** `Server.csproj` fails with an explanation if this folder still exists while the
SDK reports version 39 or higher, and `VendoredSdkBaseTests` fails the moment the SDK's own actor base gains
`ExecuteActionPerMember` — the second is the precise trigger, because a pre-release SDK can report version 39
before the API exists.

On the R39 upgrade:

1. Delete this folder, `Backend/Server/SdkPreviewInternalsVisible.cs` and
   `Backend/Server.Tests/VendoredSdkBaseTests.cs`.
2. In `Backend/Server/Match/MatchActor.cs`, change the base back to
   `EphemeralMultiplayerEntityActorBase<MatchModel, MatchAction>` — the SDK's, reached by the
   `using Metaplay.Server.MultiplayerEntity` that is already there — and drop the `SdkPreview.`
   qualifier and the comment above the declaration.
3. Delete the `RefuseToBuildTheVendoredSdkCopyAgainstR39` target from `Backend/Server/Server.csproj`.
4. Build. Nothing else should move: the vendored class is API-identical to the SDK's for
   everything the sample calls.

## What differs from the SDK original

Every difference is marked in-place with a `VENDORED CHANGE:` comment. They exist because a copy
living in the game's assembly cannot do everything the original can:

- **Base class.** The SDK's `MultiplayerEntityActorBase` derives from `EntityActor`, and its
  ephemeral subclass adds the marker interface `IEphemeralEntityActor` — which is `internal` to
  the SDK assembly, so game code cannot implement it, and `EntityConfigRegistry` throws at startup
  without it. This copy is ephemeral-only, so it derives from the public `EphemeralEntityActor`
  and inherits the marker instead.
- **Direct connections are not copied.** The UDP/KCP direct-transport path
  (`MultiplayerEntityActorBase.DirectConnection.cs`) is omitted entirely: it declares typecoded
  `MetaSerializable` types that collide with the SDK's registrations, and a Blazor WebAssembly
  client has no use for it. Everything is sent in an envelope over the session connection, which
  is what this sample did anyway.
- **Shared types are not copied.** `DefaultMultiplayerEntityActiveEntityInfo` (typecoded, would
  collide) and `EntityChecksumMode` / `EntityConsistencyChecks` (plain types the match actor passes
  to the SDK, where a duplicate would be a different type the SDK rejects) come from the SDK.

Nothing else is changed. `PersistedMultiplayerEntityActorBase` is not copied — this sample's match
entity is ephemeral.

A diff against the SDK's own copy should show only the hunks marked `VENDORED CHANGE:` plus the header and
namespace on each file. That is worth checking after any re-sync.

## Rules while it is here

- **Do not edit these files** except to re-sync them with the SDK. Game behaviour belongs in
  `Backend/Server/Match/`.
- **Do not copy this pattern into your own game.** Vendoring SDK internals to reach an unreleased
  API is a deliberate, dated workaround for shipping this sample against a released SDK — not a
  technique to reach for. On any SDK version that has `ExecuteActionPerMember`, just call it.
