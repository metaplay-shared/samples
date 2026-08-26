# Blazor WebAssembly client sample

Runs the Metaplay game client entirely inside the browser as a Blazor WebAssembly app — no external
C# process. It is the same non-Unity client as `RawClient`, with three browser-sandbox adaptations,
all selected by the `net10.0-browser` target framework and the `BROWSER` symbol it defines (which is
framework-neutral — it applies to any managed-WASM host, not just Blazor):

- **Transport:** `ClientWebSocket` (via `WebSocketMessageTransport`) instead of raw TCP — browsers can't
  open TCP sockets. Connects to `ws://localhost:9380/ws`.
- **Serializer:** loaded from a pre-built `Metaplay.Generated.Browser.dll` instead of being generated
  at runtime via Roslyn (mono-wasm has no `Reflection.Emit`).
- **HTTP:** `System.Net.Http.HttpClient` (already the non-Unity default), backed by browser `fetch`.

This sample is specifically the **Blazor WebAssembly** hosting model. Blazor *Server* runs C# in a server
process and needs none of this (TCP and runtime codegen work there) — it would just be `RawClient`.

AOT compilation is out of scope; this targets the default interpreted WASM runtime.

## Projects

- `GameLogic/` — shared game-logic assembly (the `SharedCode` types). Referenced by both the generator
  and this app so the generated serializer targets a matching assembly identity. Multi-targets
  `net10.0;net10.0-browser` so each consumer resolves the matching build of the SDK.
- `SerializerGen/` — build-time tool that emits `Metaplay.Generated.Browser.dll`.
- `Client/` — this app.

## Build & run

1. **Start the server** (listens for WebSocket connections on `ws://localhost:9380/ws`):

   ```
   cd Samples/HelloBlazorWasm/Backend/Server
   dotnet run
   ```

2. **Generate the serializer assembly** (re-run whenever `[MetaSerializable]` game types change):

   ```
   dotnet run --project Samples/HelloBlazorWasm/SerializerGen -- Samples/HelloBlazorWasm/Client/Serializer
   ```

3. **Run the Blazor app:**

   ```
   cd Samples/HelloBlazorWasm/Client
   dotnet run
   ```

   Open the printed URL in a browser. The page shows the connection status and, once the session starts,
   the `PlayerId` plus a click counter with **Click** and **Buy auto-clicker** buttons.

   > The client targets `net10.0-browser`, which propagates over the project references and selects the
   > browser build of the whole graph (the ClientWebSocket transport, no runtime codegen). The Blazor WASM
   > SDK does not imply the browser platform on its own, so this has to be set explicitly — but nothing
   > needs to be passed on the command line, and building from an IDE or another directory works the same.

To deploy the static build instead of running the dev server, publish it (serve the resulting `wwwroot`
over HTTP — a Blazor WASM app cannot be opened via `file://`):

   ```
   dotnet publish Samples/HelloBlazorWasm/Client -c Release
   ```

## Notes

- The client reports `ClientPlatform.WebGL` to the server automatically in this managed-WASM build
  (the `BROWSER` define drives `ClientPlatformUnityUtil.GetRuntimePlatform()`), so both the
  handshake `ClientHello.Platform` and the in-session `ClientDeviceInfo.ClientPlatform` carry `WebGL` —
  the same coarse "in-browser" value the Unity WebGL client reports.
- The pre-built serializer assembly (`Metaplay.Generated.Browser`) is loaded by name via the game-engine
  integration's `GeneratedSerializerAssemblyName` (see `DefaultGameEngineIntegration`), independently of
  the reported `ClientPlatform`.
- Client storage persists across page reloads, split by sync-need exactly as the Unity WebGL client does:
  - Credentials and the device GUID use the `AtomicBlobStore` browser variant
    (`MetaplaySDK/Client/ClientCore/AtomicBlobStore.Web.cs`), backed by browser `localStorage` via
    *synchronous* `[JSImport]` calls (they must be writable while the tab is closing, when async writes
    aren't guaranteed to flush), so the same guest account is reused on reload.
  - The game-config cache and incident reports use the async `FileUtil`/`DirectoryUtil` browser variant
    (`MetaplaySDK/Client/Core/Base/FileUtil.Web.cs` + `DirectoryUtil.Web.cs`), backed by
    browser `IndexedDB` (larger quota, binary, async). The IndexedDB logic is the same JavaScript the Unity
    WebGL client runs, served by the SDK as static web assets from `_content/Metaplay.Client/` and loaded via
    `JSHost.ImportAsync` — nothing for the app to configure. Data is stored in the per-project database
    `{ProjectId}/MetaWebBlobStore`.
- As on Unity WebGL, **only asynchronous file IO is supported** — there is no synchronous file IO (and no
  in-memory MEMFS fallback). The synchronous `FileUtil` read APIs and the synchronous config folder-encoding
  build utilities are compiled out of the WebAssembly build (via `BROWSER` guards), mirroring
  Unity WebGL. Consequently, built-in localizations (which require the language list synchronously at startup)
  are not supported on this platform yet; the sample disables the `EnableLocalizations` feature flag.
- The serializer is generated in Mono trampoline mode (`isMono` / `useMemberAccessTrampolines` = `true` in
  `SerializerGen/Program.cs`). This is required: the wasm interpreter enforces member accessibility, so the
  default direct private-field access throws `FieldAccessException` during deserialization (e.g. on
  `MetaActivableSet._activableStates`). The trampolines route member access through generated accessors.
