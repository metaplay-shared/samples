# Meta shell

[Documentation index](../README.md#documentation) · Read first: [`web-client.md`](web-client.md), [`player.md`](player.md)

The meta shell is the web client around the card table: Home, Events and its feature screens, Compete, Shop, Profile and Cosmetics. This doc covers how the screens are organized, how they get state, the shared reward flow, and where styles live. Feature rules are in each feature's doc. Connection, offline mode and hosting are in [`web-client.md`](web-client.md).

## Screens and routes

Pages are in [`WebClient/Components/Pages/`](../WebClient/Components/Pages/). The primary navigation has Home, Events, Play, Compete and Shop. Play is not a route: it enters matchmaking, and the matchmaking dialog opens over the screen the player is on. Profile opens from the identity row on Home. The card table is the one game route, `/table`.

[`MetaRoutes`](../WebClient/Meta/MetaRoutes.cs) derives everything from the route: the lit tab, the Back target and whether the route is the game. Back goes to the route's parent rather than through browser history, so a reload or a deep link behaves the same as a tap, and a deep-linked page still has somewhere to go back to. [`ScreenTransitions`](../WebClient/Meta/ScreenTransitions.cs) picks the entrance animation from the two routes.

## Layouts

The layout, not the screen, decides whether chrome shows.

- [`MetaLayout`](../WebClient/Components/Shell/MetaLayout.razor) draws the wallet HUD, one scroll surface, the navigation, and the layout-level matchmaking dialog, reward animation layer, connection shell and session marker. It also starts the connection, reports screen arrivals for analytics ([`analytics.md`](analytics.md)), sends a player seated at an unfinished match to `/table` (in offline mode, only from Home), and settles running wallet animations on navigation.
- [`GameLayout`](../WebClient/Components/Shell/GameLayout.razor) draws only the page and the connection shell.

The desktop phone frame belongs to `#app` in `app.css`, so it covers every route. Behavior that needs the DOM, such as modal focus trapping and mouse drag scrolling, lives in small scripts that `index.html` loads before Blazor. Each attaches to the document once and finds its targets by attribute, so re-renders do not affect it.

## How screens get state

### MetaSnapshot

Screens read a [`MetaSnapshot`](../WebClient/Meta/MetaSnapshot.cs): an immutable record with one view model per feature. Components draw views and hold no feature logic. Each view has an [`ActivityState`](../WebClient/Meta/ActivityState.cs) that says whether the surface is loading, actionable, in progress, done, unavailable, offline and so on.

The shell's decisions are pure functions over one snapshot, unit tested in `WebClient.Tests`:

- [`NextActionPolicy`](../WebClient/Meta/NextActionPolicy.cs) chooses Home's one Next-up card from a ladder of feature states, and always chooses something.
- [`FeatureOrdering`](../WebClient/Meta/FeatureOrdering.cs) orders the Events hub: actionable first, then the nearest deadline.
- [`BadgePolicy`](../WebClient/Meta/BadgePolicy.cs) decides which tabs carry a badge.

The policies ignore a surface that is not live, such as one still loading or in error.

### MetaStateService

[`MetaStateService`](../WebClient/Services/MetaStateService.cs) builds and caches the snapshot. It rebuilds on a clock tick that also drives countdowns and badges, when the player model changes, when `?meta=` changes, and after its own mutations. It ignores the client service's per-frame state change, which would rebuild the snapshot on every frame.

A build starts from the fixture for the current scenario and overlays the player's real state one slice at a time. Each slice is built from `PlayerModel` by the feature's view builder, several of them shared with the server, such as `DailyRewardPolicy` and `SpinWheelPolicy`. With no session the overlays change nothing, so a cold boot draws complete screens. Countdowns read the model's `CurrentTime` rather than the device clock, so they agree with the timeline that turns days and phases over.

Screens change state through `MetaStateService`. Request-and-response flows, such as the daily reward claim, a rename or a purchase, call `MetaplayClientService` directly. Both return nothing when there is no session.

### Fixtures and ?meta= scenarios

[`MetaFixtures`](../WebClient/Meta/Fixtures/MetaFixtures.cs) builds stand-in snapshots, selected with `?meta=<name>`. The scenarios cover each rung of the Next-up ladder, new and finished players, lapsed and unpublished features, and the loading, offline and error states. There is no UI switcher. Elapsed time is a parameter, so countdowns run in the app and are exact in tests. An unknown name builds the default scenario.

### Real state wins except for pinned slices

A slice the scenario does not pin shows the player's own state. A pinned slice keeps the fixture. With no `?meta=`, a live player sees real state everywhere.

- **Declare pins, never infer them from the fixture's value.** Designed states are often ordinary values, such as an actionable day with an owed reward, so the value cannot say who owns it.
- **Pin what a scenario silences as well as what it shows.** A rung is on top only while the rungs above it are quiet.

`ScenarioPinTests` checks the pins against the ladder.

### The session marker

The layout renders a hidden element with `data-testid="session"`. Its `data-state` is `fixture` until `PlayerModel` exists and `live` after, and its `data-authored` lists the pinned slices. A control tapped before the session starts is backed by a fixture and sends nothing, so browser tests wait for `live` ([`testing.md`](testing.md#live-server-tests)). With pins and a session, the page also shows a notice naming the scenario and its slices.

## The shared reward flow

Every screen that grants something uses [`RewardReveal`](../WebClient/Components/Meta/RewardReveal.razor), driven by a stage ([`RewardViews.cs`](../WebClient/Meta/RewardViews.cs)). Nothing is revealed before the grant commits on the client's model, and the stage alone decides the drawing, so a reveal can replay after a reconnect without granting twice. A failed grant offers a retry.

The wallet changes before the reveal closes, and the HUD is visible behind it. [`WalletBurstService`](../WebClient/Services/WalletBurstService.cs) holds each rewarded balance back by the amount that has not arrived yet. When the player collects, sprites fly to the HUD and the balances count up. The hold stops at the model's value when the burst ends, on navigation, and when the reveal is dismissed. Because the hold is a distance behind the live wallet, it always resolves to the model's value, and currencies outside the reward stay live. Under `prefers-reduced-motion` there is no hold. `WalletBurstPlan` and `WalletBurstBalances` compute the choreography without a browser.

## Styles and design tokens

The client uses no CSS framework. `index.html` loads the web fonts and three stylesheets:

- [`app-base.css`](../WebClientBase/wwwroot/css/app-base.css) from `WebClientBase`: the element reset with the dark color scheme, the default text color and the page background, in a cascade layer so every unlayered rule in the app wins over it. It also holds the z-index layer scale, shared animations, the connection shell's styles, and the styles of `WebClientBase`'s debug layout and fallback pages.
- [`app.css`](../WebClient/wwwroot/app.css): the product's one token set, the phone frame and the table's styles.
- [`meta-shell.css`](../WebClient/wwwroot/meta-shell.css): the shell's styles, loaded last so they win over the game's own. Its `--m-*` properties are aliases of `app.css` tokens.

Change a color in `app.css`, where every other place refers to it. `WebClientBase` writes its theme variables (`ThemeColors`) into the page head, and its reset reads them for the page background and the default text color. [`GameTheme.cs`](../WebClient/GameTheme.cs) binds the background and border variables to the same tokens. The rest keep `ThemeColors`' defaults, which restate token values in C#. The boot screen's inline styles in `index.html` read the tokens with literal fallbacks, because they apply before the stylesheets load. `ColorContrastTests` reads the tokens to check contrast.
