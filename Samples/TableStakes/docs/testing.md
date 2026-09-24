# Testing

[Documentation index](../README.md#documentation) · Read first: [`architecture.md`](architecture.md)

This doc describes the test layers and what belongs in each, how the end-to-end (E2E) harness and suite work, and the
rules that keep tests deterministic. The commands to run each layer and the harness are in
[`AGENTS.md`](../AGENTS.md#tests).

## Test layers

| Layer | Project | Needs |
|---|---|---|
| Shared-code unit tests | `Backend/SharedCode.Tests` | Nothing |
| Server unit tests | `Backend/Server.Tests` | Nothing |
| Pure client tests | `WebClient.Tests` | Nothing |
| Render tests (bUnit) | `WebClient.Tests` | Nothing |
| E2E tests (Playwright) | `WebClient.Tests` | Chromium, the web client, and a game server for most fixtures |

Test at the cheapest layer that can prove the behavior. The bot client is not a test layer. It asserts nothing, but it
puts the server under traffic that no test produces.

### Rules

- Add or update tests in the same change as the feature. Every feature needs at least shared-code unit tests and E2E
  tests.
- Use these fixtures as templates: `MatchHistoryRulesTests` (shared-code unit), `NextActionPolicyTests` (pure client),
  `DailyRewardRenderTests` (render) and `ShellPageTests` (E2E).
- Name a bUnit fixture `*RenderTests`. The render-test filter in [`AGENTS.md`](../AGENTS.md#tests) matches on that
  suffix.
- In Playwright tests, find elements by role and text. Add a `data-testid` only for an element with no accessible
  role.
- After changing `PlayerActor`, `MatchActor` or `MatchmakerActor`, run the bot client against a local server and check
  the server log for errors.

### Shared-code unit tests

`Backend/SharedCode.Tests` runs against the server's build of `SharedCode/`. Its game config is built in code
([`TestGameConfig.cs`](../Backend/SharedCode.Tests/TestGameConfig.cs)), not read from the archives. This layer
covers the match rules and turn flow, the bot and matchmaking policies, the player model with every live-service
feature, game config parsing and validation, and the analytics contract.

### Server unit tests

`Backend/Server.Tests` holds pure decisions that use a server-only type, such as the player actor's in-flight claim
tracking, and checks that need the server assembly loaded, such as the comparison of the committed config archives with
the sheets ([`game-config.md`](game-config.md#checking-the-committed-archives)). Logic that needs no server type belongs
in `SharedCode/` and the layer above.

### Pure client tests

A `net10.0` test project cannot reference the `net10.0-browser` projects `WebClient` and `WebClientBase`, so
`WebClient.Tests` compiles their sources in (see [`WebClient.Tests.csproj`](../WebClient.Tests/WebClient.Tests.csproj)).
The pure tests cover the meta shell's policies and fixtures ([`meta-shell.md`](meta-shell.md)) and the connection
trouble policy.

### Render tests

Fixtures named `*RenderTests` render the app's real components under bUnit. Page fixtures derive from
[`BunitPageTest`](../WebClient.Tests/BunitPageTest.cs), whose client service never connects, so screens draw the
fixture picture for a `?meta=` scenario. Table fixtures hand the `Table` page a `MatchModel` directly.

This layer has limits. A screen the app gates on a session renders its gate, not its content. Only the services the
app's own components inject are registered. A component missing from the test project's compile items renders as an
unknown HTML element, with build warning `RZ10012`, so the page draws in a state the app never shows. Add the
component's file to the compile items in [`WebClient.Tests.csproj`](../WebClient.Tests/WebClient.Tests.csproj). Render
assertions belong in this layer, so one that fails in the E2E suite moves here.

### E2E tests

Every browser fixture derives from [`PlaywrightPageTest`](../WebClient.Tests/PlaywrightPageTest.cs), which derives
from Playwright's `PageTest`. Fixtures that exercise only the client run against offline mode and need only the web
client. The rest need a live server. The suite covers what needs a real browser (computed CSS, contrast, layout, focus
order, animation timing, refresh and restore) and what the server decides (matchmaking, bot fill, reconnects,
purchases, and each feature's actions).

## The E2E harness

The default ports belong to the main checkout. A second stack on the defaults would fail to bind, or take the ports so
that browser tabs on the main checkout's client talk to a feature branch. The direct transport's UDP port fails
silently: it binds with `SO_REUSEADDR`, so two local servers both bind it and only one receives datagrams. So a
worktree runs the suite through [`tools/run-e2e.sh`](../tools/run-e2e.sh). It wraps the harness,
[`tools/run-e2e.py`](../tools/run-e2e.py), which refuses to run in the main checkout.

A run builds the server, the web client and the test project in one build, starts a server and a client on free ports
with a fresh database, runs the suite, and stops them. A port that another process takes before the server binds it
is retried with new ports.

**Tests find the stack through environment variables** that the harness sets and `PlaywrightPageTest` reads. The
client reads its server ports from the page URL at boot, and in-app navigation drops them. Build every URL with
`ClientUrl(route, query)` and reload with `ReloadOnStackAsync(page)` instead of Playwright's `ReloadAsync`. A URL
built by hand boots a client that connects to whatever holds the default ports.

**Pacing.** The harness shortens a few server windows on the command line, such as the bot think delay, the trick
pause and the matchmaking fill wait. No fixture asserts their values. A fixture that has to wait out the fill wait
reads the value the harness exports to the tests (`PlaywrightPageTest.FillWaitMs`) and sizes its wait from it. So
shortening them does not change what a fixture proves. Windows that a fixture measures itself against keep their
shipped values: the move deadline, the grace windows and the join window. Two guards enforce this.
`SANCTIONED_PACING_KEYS` in `run-e2e.py` lists the options the harness may shorten, and the harness exits with an
error if it sets any other. `ShippedTimingDefaultsTests` pins the shipped defaults, so a changed default fails there
instead of silently changing what a fixture measures. To shorten another window, first export its value to the tests
the same way, and size the fixture from it. The suite passes at either pacing.

**Admission and workers.** The harness limits how many runs are active on one host at once, because a cold WASM boot
starved of CPU fails with "An unhandled error has occurred". It sizes the NUnit worker count from the host's cores and
the number of active runs. Concurrent runs belong in different worktrees, because runs from one worktree share build
output.

**Trimmed mode** publishes the client and serves the published files, so it tests a Release, IL-trimmed build
([`web-client.md`](web-client.md#the-published-client)). A failure there can come from either.

## How the E2E suite runs

### Parallel execution

The suite runs test cases in parallel, not only fixtures, with a new fixture instance per test case, because
`PageTest` keeps its page, context and browser in instance fields. A `[OneTimeSetUp]` must therefore be `static`.

### Checks around every test

- **Client identity.** [`ServedClientBuildCheck`](../WebClient.Tests/ServedClientBuildCheck.cs) compares the served
  client with the one this tree built, and fails the fixture on a mismatch, so a test never runs against an old or
  foreign client.
- **Client errors.** A teardown fails a passing test if Blazor's error bar is showing, because the app stops rendering
  once it throws. For a failed test it prints the client's console errors, page exceptions and failed requests.

### Locks for shared server resources

A live server has one matchmaking queue and one tournament group. Two taps inside one fill wait share a table, and two
joins in one season share a group. [`LiveServerLocks`](../WebClient.Tests/LiveServerLocks.cs) serializes only the step
that touches the resource: `QueueAsync` around a Play tap through the seat landing, and `LeagueAsync` around a
tournament join and any standings read a concurrent join would disturb. A case that needs both takes the league lock
first. Do not use `[NonParallelizable]` for this, because it serializes the whole fixture. The harness reports how long
each lock was held and waited for.

### Weekly event fixtures

The server seeds weekly events at startup ([`weekly-event.md`](weekly-event.md)), so a live-server player always has a
weekly event. One fixture reads the seeded events, and a later one creates an event, which concludes the seeded events
it overlaps. Their order holds only because both are `[NonParallelizable]` and ordered inside NUnit's non-parallel
group. Removing `[NonParallelizable]` from either removes the ordering without an error. Prefer fixtures that do not
depend on the timeline over new ordering.

### Widths and routes

Check several viewport widths inside one test, and name the width in each assertion message. A resize is a reflow,
while a new test case is a new browser context and a cold boot. Give each route its own test case, so each route gets a
cold boot, which catches a screen that fails only on a cold deep link.

## Writing deterministic tests

### Forcing timers

Force a window instead of waiting for it. To observe a state, set the window that would end it far longer than the
test.

- **Query-string knobs.** Timers and animation windows that the client or the offline host owns are set from the page's
  query string, parsed in one place, [`TestQuerySettings.cs`](../WebClient/Integration/TestQuerySettings.cs). Offline,
  they set the host's timers, the bot think delay and the trick pause, and can fix the deal or seat the player as never
  having arrived. Offline or live, they set the client's own animation windows. Knobs on `MetaplayClientService` also
  tamper with or skip steps of a demo purchase ([`offers.md`](offers.md)).
- **The test force-expire endpoint.** Against a live server, `POST test/match/{matchId}/expire` on the PublicWebApi
  ([`MatchTestController.cs`](../Backend/Server/Match/MatchTestController.cs)) moves every deadline a table is waiting
  on to now and runs the table. It answers only on a server started with `--TestRoutes:Enabled=true`, which the
  harness does ([`architecture.md`](architecture.md#http-endpoints)). It returns how many stamps it moved with the
  table's phase and play index, so a test polls a result instead of sleeping. It does not bypass rules: expiring the
  move deadline plays a card for the seat on turn.

### Seeds

Unit tests state the deal (`MatchTestDeals`). In offline mode a knob fixes the deal, the bot opponents and their
moves. A live table's seed is unguessable ([`match.md`](match.md#the-deal-seed)), so a live test cannot fix it. A test
that involves a deal names its seed or deal, so a failure reproduces from its message.

### Live-server tests

- **Wait for the session before the first read or tap.** Pages show placeholder data before the client has connected
  ([`meta-shell.md`](meta-shell.md#the-session-marker)), and a tap at that point sends nothing. Call
  `WaitForLiveSessionAsync` first.
- **Wait for the server before a reload.** The client applies an action immediately but sends it to the server
  shortly after, so a reload right after the action can lose it. Before any step that reloads the page, call
  `ReadPlayerNameAsync` on the Home page, then `WaitForServerToHaveEventAsync`.
- **Declare every part of a `?meta=` scenario.** A part of the page that the scenario does not declare in
  `MetaFixtures.Scenarios` shows real player data instead of placeholder data
  ([`meta-shell.md`](meta-shell.md)).

A test that needs the server to refuse a purchase, such as one with a wrongly signed receipt, needs a live server,
because the offline server accepts every purchase.
