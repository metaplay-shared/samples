# Ninja Time Trial

A [Metaplay SDK](https://metaplay.io) sample: a one-button ninja autorunner that plays in the browser. Its
purpose is to show how a conventional real-time game connects to a Metaplay backend. The run is simulated
on the client at 60 Hz with no server round trips; after the finish, the backend validates the run by
playing the recorded inputs through the same deterministic simulation, and only the time *it* computes is
recorded and rewarded.

> [!NOTE]
> **Built with AI.** The code, tests, docs and art were produced with AI tools (Claude Code, ChatGPT image generation),
> directed and reviewed by a Metaplay engineer, with the review focused on the shared game logic and the Metaplay SDK
> integration.

<p align="center">
  <img src=".github/screenshot.png" alt="The ninja running a course, with an opponent's ghost ahead" width="288">
</p>

**Play it in your browser:** a deployment of this sample runs at
<https://whole-impalas-strive-quickly-public.p2-eu.metaplay.dev/>. No install or account setup is needed.

## The game

The ninja always runs forward; the player has one button. Hold to charge a jump, let go to launch. A course
is a short climb up a hillside through pits, ledges, vines and slopes, divided into segments by torii gates,
and a run lasts about half a minute. Mistakes cost momentum rather than ending the attempt, so every run
reaches the finish.

Each run is raced against three ghosts drawn from that course's pool: other players' accepted runs, with a
house field of scripted runners filling a pool that is still short of them. Placement in that field at each
gate pays Orbs, which buy Skills; one Skill may be equipped at a time, and it changes the runner's movement
values. A new player runs as Masked Ninja: their runs pay them but enter no ghost pool until they buy the
Name Tag and choose a name. The server rotates which maps are in play; each map is composed from three
course parts.

## What the sample shows

- **Backend validation of a real-time run** — the client submits the attempt's ticket and everything the
  player pressed; the server runs that recording through the same shared simulation and records the time
  it computed. A run it cannot reproduce is turned down with a reason the player can read. See
  [How a run is validated](#how-a-run-is-validated).
- **A deterministic simulation shared by client and server** — fixed-step, fixed-point physics in
  `SharedCode/TimeTrial/`, compiled into both the WebAssembly client and the server.
- **Server-side entities beyond the player** — per-course ghost pools that collect accepted runs and pick
  each run's opponents, and a map registry that rotates the maps in play (`Backend/Server/`).
- **A game config pipeline** — tunable values as CSV sheets in [`GameConfigSource/`](GameConfigSource/README.md)
  and the course parts in `CourseParts.json`, built offline into an archive that refuses to build broken.
  Values travel in the archive rather than in a binary, so a running game can be retuned by publishing a new
  one from the LiveOps Dashboard.
- **A Blazor WebAssembly web client** — the menu, the canvas game view, and the connection handling.
  `?env=offline` plays the whole loop with no server, as practice. The client installs as a home-screen app.
- **Tests on both layers** — the simulation, the actions, the server logic and the config build in NUnit;
  the played run and its accepted result in Playwright, through the real client.

## Layout

| Path | What it holds |
|---|---|
| `SharedCode/` | Game logic both sides run: the player model and actions, the game configs, the time trial simulation. |
| `Backend/Server/` | The game server: the player actor that validates runs, the course ghost pools, the map registry, web client hosting. |
| `Backend/SharedCode.Tests/`, `Backend/Server.Tests/` | Unit tests. |
| `WebClient/`, `WebClientBase/` | The Blazor WebAssembly client: the game UI, and the game-agnostic connection framework under it. |
| `WebClient.Tests/` | End-to-end tests (Playwright). |
| `GameConfigSource/` | The config sheets and course parts. |
| `tools/` | The game config build, the WebAssembly serializer generator, the dev launcher and the server image build. |

## How a run is validated

The real-time run is tied to authoritative Metaplay state without turning every simulation frame into a
player action.

### Authority boundary

The run is a deterministic fixed-step simulation (`SharedCode/TimeTrial/`) shared by the web client and the
backend. It owns only transient state: movement, collisions, course progress and the timer. The client runs
it locally for immediate input and rendering, records input transitions against simulation steps, and shows
a provisional finish time.

The player model owns the durable facts: accepted best results, rewards, unlocks and participation. It does
not tick the run at 60 Hz, and it never takes a client-reported time as truth. The outcome is committed by
ordinary player actions, after the server has computed it.

### Run lifecycle

1. **Ticket.** The client asks for a run. The server picks the map from the set it is rotating, selects the
   opponents from that map's pool, and answers with a short-lived `TimeTrialTicket` and the field together.
   The ticket names an immutable course and ruleset, carries the Skill equipped at that moment, and uniquely
   identifies the attempt, so a recording cannot be submitted twice for rewards. Offline practice opens its
   own ticket with no opponents and nothing to submit to.
2. **Play.** The client simulates the run and records compact input changes — a `TimeTrialRecording`.
   Render frames, the device clock and derived positions are not evidence.
3. **Submit.** On finish, the client sends the ticket id and the recording (`TimeTrialSubmitRequest`). The
   displayed result stays provisional.
4. **Recompute.** `PlayerActor.HandleTimeTrialSubmitRequest` checks the cheap things first — an open ticket,
   a matching id, not expired, a supported ruleset, a well-formed and bounded recording, a course that
   resolves — then replays the recording through `TimeTrialSimulation.Replay` under the ticket's course and
   Skill.
5. **Commit.** The server's own finish time is the result. An accepted run is committed to the player
   exactly once through `PlayerAcceptTimeTrialResult` and, when the player has a name, recorded into the
   course's ghost pool; a rejected one awards nothing and reports a player-readable `TimeTrialRejectReason` through
   `PlayerRejectTimeTrialResult`.

Validation runs inline, inside the player's own actor: one run is a few thousand steps of fixed-point
arithmetic. If load tests showed that costs too much actor latency, the same lifecycle could hand the replay
to a bounded worker queue and notify the player later — a scaling choice, not a change in authority.

### Determinism and versioning

The physics use fixed-point numbers and a fixed update order, independent of rendering. Randomness is derived
from the course description, never from client-local state. A recording is meaningful only together with its
course and ruleset:

- **Ruleset.** `TimeTrialConstants.RulesetVersion` moves whenever the same presses would produce a different
  run. The server refuses a recording made under another version outright, and the ghost pools and the map
  registry reseed behind a bump, because a par measured under old physics is not a par. A bump is expensive on
  purpose.
- **Course.** A ticket names its course by id, and `CourseResolver` turns the id back into geometry on both
  sides, against the config archive the session is pinned to. A published course part is immutable: editing
  one in place changes what every stored recording on every map containing it means. The developer part
  editor's draft runs are the exception — the ticket carries the draft itself, and they are practice only.
- **Skill.** A Skill changes what an input does, not what an input is, so the recording format is
  untouched. Both sides derive the effective movement values from the published rules and the Skill named on
  the ticket; nothing about them is sent. A stored ghost carries its Skill for the same reason.
- **Tunables.** The values in `TimeTrialRules.csv` and `Skills.csv` are part of what a recording means, and
  the server recomputes a submitted run against the archive published at that time. Publishing a retune while
  runs are open changes what those runs are measured by; this sample accepts that gap.

### What recomputation does and does not prove

The server calculates the result; it never compares its simulation to positions the client claims.
Malformed or inconsistent recordings — unsupported inputs, impossible step ordering, excessive duration or
event count, expired or mismatched tickets, duplicate submissions — are rejected, and bounding a submission's
size is part of correctness, since the server processes whatever the client sends.

Recomputing confirms a recording is internally consistent. It does not confirm that a person produced those
inputs in real time: because the same rules run on both sides, a client could submit a valid recording it did
not play by hand. Short-lived, single-use tickets raise the effort for that; the sample deliberately stops at
result integrity rather than trying to detect automated play.

### Connectivity

A run keeps going through a brief disconnect, because it is simulated locally, and submits when the
connection returns while its ticket is still valid. A run that cannot be validated before its ticket expires
still shows its local time but affects no rewards or rankings. How long a ticket stays open is the published
`TimeTrialTicketLifetimeSeconds` in `GameConfigSource/Global.csv`. The client distinguishes provisional,
validating, accepted and rejected results; offline play submits nothing and says so on the result.

## Run locally

From the repository root, start the watched Metaplay server and watched WebClient together:

```bash
dotnet run tools/DevStack.cs
```

Both log streams stay in that terminal. Server and client source changes rebuild automatically, and if
either watcher exits the launcher stops the other one. Ctrl+C stops both complete process trees; on Windows
the launcher also uses a kill-on-close Job Object so closing the launcher cannot orphan either watcher or
its descendants. Open `http://localhost:5290` once the client is ready. If the client gets built from
outside the watcher — an IDE, a second shell — the launcher restarts the web dev server, which would
otherwise keep handing out a client it can no longer serve; reload the page when it says the server is back.

To run the build players get — the Release publish, served by the game server itself on port 5560 and
reachable from a phone on the same network — use `dotnet run tools/DevStack.cs -- --release` instead. It
publishes the client, then runs the server alone with nothing watched, which is the build to measure
performance on; the watched Debug build is not.

The separate commands and complete build/test instructions remain in [`AGENTS.md`](AGENTS.md).

## More

- [`AGENTS.md`](AGENTS.md) — how to build, run, test and develop in this repo.
- [`WebClient/README.md`](WebClient/README.md) — the Blazor WebAssembly client.
- [`GameConfigSource/README.md`](GameConfigSource/README.md) — how a config sheet is written.
