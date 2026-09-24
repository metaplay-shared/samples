# The Client

The Blazor WebAssembly client: the screens, how an action reaches the server, and what the player sees when something
goes wrong. The match screen and the card faces follow [`art.md`](art.md), with an illustration for every card; the
server holds no animation, so the client paces every beat itself.

## Shape

`ClientBase/` is the game-agnostic half — connection lifecycle, the connection-trouble app shell, environment selection,
theme, UI primitives. `Client/` is Sticky Paws: the board, the collection, the Heist. Anything that knows a card from a
clan lives in `Client/`.

The screens:

- **Home** — the player's name and record, the selected deck's power, the leaderboard and activity counts
  ([`community.md`](community.md)), card-gift progress, and the entries into play: **Ranked**, **Practice**, and
  the way into the collection. It is the **only** queue entry point, so there is never a second Play button to keep
  consistent with the first, and it is also where the wait happens: Play raises the searching dialog over Home and
  navigates nothing.
- **Collection** — every card in the pool, owned and unowned, each with this account's rank on it. Filters by clan, type
  and rarity; a card opens into its detail view ([`art.md`](art.md)) — cost, clan badge, keywords in one line, rank
  stars, and the printed rank track. Unowned cards are shown, not hidden: an unowned card is a card you can heist, and
  knowing what is out there is part of the economy. It is also where cards are **locked** ([Locks](#locks)), which is
  the only protection this game has against a card being taken.
- **Deckbuilder** — 25 cards, singleton, at most two clans, with the deck's **Power Score** read out live as cards go in
  and out. The readout is the shared-code function matchmaking and the stakes tier use, so the number the player builds
  against is the number they will be matched and paid on. Legality is built into the editor — the player picks at most
  two clans first, so only cards those clans allow are offered, and a pick the rules refuse says why at once — and the
  save itself is still validated server-side, because the client's copy of the rules is a courtesy and never an
  authority. A refused save says why rather than snapping back. Locked cards are marked here too: which of the 25 are
  frozen is part of what the player is choosing to field.
- **Searching** — a **dialog over the screen the player pressed Play on** while [matchmaking](matchmaking.md) runs, not
  a screen of its own. **The page turns exactly once — at the moment there is a board to draw.** A flow that navigated
  on the press would spend the whole wait on a match page with no match on it. Its only job is to be honest and
  cancellable: it counts down the one bound the server can state, an upper bound so the wait can end early and the
  dialog says "any moment now" rather than restarting the number, and its **Cancel gives way to a waiting state once
  pressed**: a cancel that loses the race to a forming match is refused and a board appears instead, so the dialog waits
  for the answer rather than dismissing itself and having to un-dismiss. The dialog goes up on the press rather than on
  the server's answer, so it carries its own bound on silence — an optimistic screen with no timeout is a spinner
  forever whenever the request that raised it never arrived.
- **The pre-match reveal** — the Weather, both decks' Power Scores, and the stakes tier they imply, on a panel over the
  mulligan. Nobody discovers after winning that the Heist pays less than they assumed, and both players get to mulligan
  against a Weather they have read. A newcomer-shield match is a tier like any other and is named here as one: no
  ranks moving in either direction. This screen is where the wager is agreed; the board and the Heist then keep
  it in sight rather than restating it.
- **The match board** — the game, below.
- **The Heist** — the post-match steal, as its own full screen: the winner picks and the loser is asked for nothing. It
  is the moment the game is named after and it gets the whole window.

The client attaches to a match through a **match sub-client on its own client slot**: the server associates the match,
the SDK downloads and syncs the model and delivers the seat's hand as private state, and the client listens on the
match's entity channel for directed messages — rebound on every activation, before the buffered messages are flushed
([`protocol.md`](protocol.md#entity-channel-listeners-must-be-rebound-on-every-activation-before-the-buffer-is-flushed)).
**The client is never told to go find a match; it is told which one it is in.**

## The window it lives in

A landscape game laid out for a desktop browser, played with a mouse or a touchscreen. The board is one screenful with
**nothing to scroll**: a scrollbar on the board is a layout bug. The meta screens are the exception — a collection is a
list, and a list scrolls. The rooftop background fills the viewport; the playable frame fills its height and caps its
width at 16:9, compact landscape tightens the peripheral controls, and portrait shows a rotation note instead of an
unplayable board. `BoardChromeTests`, `BoardGeometryCssTests` and the `/dev/board` previews cover the geometry.

## The board

What sits where, with the reasons:

- **Two Dens, each a door in the middle of its own row**, carrying the owner's name and hit points on the door's panel.
  The Den is the whole win condition and never fights back, so it is a fixed landmark, and putting it in the row makes
  "the row is between the enemy and your Den" true on screen. Both seats draw the **same door at the same size**;
  ownership is an accent overlay, never a different geometry. When a Den is a legal target the door takes the press;
  when a Guard has closed it, it dims and says why.
- **Two critter rows facing each other**, up to six a side. Board order is cosmetic, so the rows re-flow as critters die
  and nothing the client sends names a position.
- **The hand, on an arc along the bottom**, the only faces drawn. The cards are points on one circle whose centre is far
  below the board, so tilt and lift follow from position; the card under the pointer grows out of the fan, and holding
  opens its full face. The opponent's hand is drawn as backs, in the public count.
- **Mana acorns, bottom-left** — the row grows by one every turn and never caps, so the row is the ramp made visible.
  Spent acorns empty rather than vanish.
- **Graveyards and unseen pools, at the far left** — a counter each, opening into an inspector over the board that any
  board action dismisses, with the turn clock still visible. Both are open information, which is only true if reading
  them is cheap. The deck counter doubles as the Tuckered Out clock.
- **Weather, mode and End turn, one column on the right.** The Weather is a rule in force, not a notification, so its
  name and effect are always in the DOM, revealed on hover and focus and held open through the mulligan. The mode word
  — PRACTICE or RANKED — is the one bit of the stakes a board owes mid-turn. End turn carries the seat's own turn
  count, the remaining time and a nudge when the turn still has something in it; its ring appears only as the deadline
  closes, and both are withheld while the board trails.
- **The event feed, bottom-right**: the last three lines, expanding in place.
- **What aiming would do, on the target itself.** A consequence — "it dies · yours takes 2" — on the *hovered* target;
  a heal preview on every legal one, because choosing where to put a Snack is a comparison. Both come from one pure
  helper over public state and the card in hand, so the board never promises what the rules would not deliver.

Critter state is drawn **on the frame**, not in a tooltip: a Guard wears a gold frame, a sleepy critter naps, a Sneaky
critter is translucent and dashed, a Bubble is intact until it pops, and damage is visible on the health it took.
Anything a player has to hover for is a state they will misplay.

That rule is about state, not vocabulary. What a keyword *means* is glossary text, and it travels with the keyword:
keywords are drawn **as their own elements carrying the description from their config row**, through one shared keyword
component, so a keyword explains itself the same way wherever the player meets it — card detail, hand fan, critter chips
and the event feed. Two display rules ride along with it. The **Hello label is suppressed on tricks and shown on
critters**: a trick's whole body is its Hello ([`effects.md`](effects.md)), so on a trick the label says nothing the
card's type does not, while on a critter it marks the battlecry-like minority. And the triggers that are engine
vocabulary rather than keywords — on attack, turn start, turn end — carry client-side help text instead of a config
description, because the seven player-facing keywords are a fixed set ([`game-design.md`](game-design.md)).

Art direction is [`art.md`](art.md).

## Legality is shown, never explained

This is the client's most important job, and it replaces the tutorial the game does not have.

- **In hand:** cards playable right now — affordable, with a legal target available, with room on the board — are lifted
  and ringed and take input. Everything else is dimmed and inert.
- **On the board:** selecting an attacker highlights every legal target. A Guard on the far side dims the enemy Den and
  leaves every critter attackable, which teaches Guard's exact rule — it protects the Den, not the team — without a line
  of text.
- **While targeting a trick:** the same highlight, from the same source.

Both are computed with **the same shared rules functions the server validates with**, and the client's hand is kept
in step by addressed operations in timeline order, so the highlight is trustworthy in every state the design produces
and a refusal, when one happens, is reported rather than swallowed
([`hidden-information.md`](hidden-information.md#the-legality-promise-stated-honestly)).

**Hand changes are a hot path, not an error path.** The server changes the hand on every turn-start draw, every draw
effect, every bounce and every overflow, and each change arrives as an operation addressed to this seat, in timeline
order; the client holds nothing and waits for nothing
([`hidden-information.md`](hidden-information.md#delivering-a-hand)). A client that assumed its hand only changes when
the player plays a card would be wrong on turn one.

Everything the client sends names cards and critters **by identity** — never a position in the hand or a slot on the
board. Both of those move under the player between the press and the send, and the failure reads as a rendering bug when
it is not one.

## Taking a turn

A turn here is not one move. The player plays critters, casts tricks, attacks with each ready critter and ends the turn,
in any order, and the board changes under them the whole time. The client's discipline through all of it is the same:

> **The server is the only writer. The client renders nothing the server did not confirm, and covers the round trip with
> feedback that is not game state.**

- **Playing a card** — mouse and keyboard select a card and then its target. On touch, a tap inspects and a drag
  plays: release on a highlighted legal target, or on the central "Release here to play" area for a card with none;
  releasing anywhere else cancels. An arrow follows the finger while the card stays raised in the hand, and the move
  still goes through the ordinary legality and intent path.
- **Attacking** — the same shape: select an attacker and a target, or drag a ready critter onto a highlighted critter
  or Den, with the damage preview shown before release. A cancelled touch or a late render can never turn an abandoned
  drag into a play.
- **Inspection** — hover or keyboard focus on desktop, a tap on touch. Tooltips disappear while dragging or choosing a
  target, and during the mulligan a tap marks a card for replacement instead.
- **Ending the turn** — an explicit button, and the only control that is worth a moment's hesitation. It never blocks
  and never asks a modal question: it carries a quiet mark while the turn still has something in it — unspent mana, a
  playable card, a ready critter that has not attacked — and a player deliberately holding mana still ends their turn in
  one press.
- **The mulligan** — the one place the client batches. The opening hand comes up under an already-revealed Weather,
  cards are toggled for replacement, and one confirm sends the whole set, because the server swaps it as one step.
  The panel sits over the board and leaves the hand uncovered, because the hand is what it asks the player to tap, and
  the fan offers every card: the second player's compensation card arrives when the mulligan ends rather than in the
  opening hand, so while the mulligan is open the hand holds only cards the table would take back.

**One intent at a time.** Each action is a directed intent; the input that raised it locks until the server confirms or
refuses, and no further intent is sent in between. Not a turn-long lock — a lock exactly long enough that a double-press
cannot send twice. The client does not pipeline several actions optimistically, and the reason is legality rather than
protocol: the second action's legality depends on the first's result — mana spent, a critter dead, a board slot taken —
so a queued second action is a choice offered against a board the server has already moved past. The round trip is
short; the queue would be a lie.

**A refusal is an explicit message, not a timeout.** The server sends one to the submitting client; the lifted card
falls back into the hand, the attack highlight clears, and input unlocks. Without it a card hangs lifted and the hand
stays locked for the rest of the match. In practice a refusal means an intent raced by something the server did first —
a deadline that ended the turn automatically, a covering bot's action. On a covered seat the refusal is the table
saying a bot has the seat, and the same intent is what asks for it back: the board says the seat is the player's from
the next turn ([`match.md`](match.md#a-covered-seat-is-handed-back-between-turns)).

This is the one place "render nothing until the server says so" is softened, and only in appearance: the lifted card is
not in play, the highlighted attacker has not attacked, and no game state moves until the server's update lands.

## Two clocks, and one rule

The client reads time from two places and must never mix them in one answer:

> **If a player can see it, it reads presented time. If it decides what the server will accept, it reads authoritative
> time. No surface may mix the two in one answer.**

The failure this prevents is a single method that reads the animated board for one branch and the authoritative model
for the next — the turn indicator lighting up in step with the board while prompting for a turn the board has not
reached. Stated per *answer*, not per surface, and enforced by making the two clocks separate types (`AuthoritativeTime`
and `PresentedTime`), so reaching for the wrong one is a compile error rather than a judgement call.

Consequences that are easy to get wrong:

- **The deadline ring is withheld while the board trails.** A ring draining for a turn the player has not been shown yet
  is worse than no ring.
- **So is the turn indicator, and for the same reason.** While a beat is still playing out the attack that ended the
  opponent's turn, nobody is on turn as far as the screen is concerned.
- **The terminal phase is withheld until the finish has played out.** The lethal blow lands, the critter that dealt it
  comes home, the Den's hearts run out and the board settles — *then* the Heist screens come up. This matters more here
  than a results panel would: the Heist takes the whole window, so a Heist that arrives early does not overlap the
  finish, it replaces it, and the player never sees the hit that won them the match.
- **Countdowns compare the model's clock against absolute stamps in the model**, both on the server's clock
  ([`protocol.md`](protocol.md#the-models-clock)).
- **A stamp the client writes itself is read back against the clock it was written from.** A beat's end is stamped
  from authoritative time, so the repaint that asks whether the beat is still running must ask the same clock. Mixing
  them there is the worst version of the failure, because the client's own beats are what withhold the terminal phase —
  a beat that never ends is a match that never shows its Heist.
- **An animation window is bounded by its own span, not only by its end.** A reading from before the window began can
  only mean the clock moved after the window was stamped, and the beat is then over rather than waiting to be caught up
  with. Ending an animation early is a dropped frame; failing to end it is a stuck board.

**Authoritative time is the model's clock** as the timeline delivers it — the server's clock, late by the delivery
latency, and never the device's, which is wrong by minutes often enough to decide real matches
([`protocol.md`](protocol.md#the-models-clock)). **A ring is only drawn for a deadline someone enforces**: "no
deadline in force" is a state the board carries, and the client then withholds the ring and keeps taking input.

## Pacing the board

The board animates *towards* each update rather than jumping to it, and each beat exists to make a number that changes
visibly caused by something.

- **A card played is a card leaving a hand.** It flies from the hand that played it into the row rather than appearing
  on the grass. The opponent's cards leave as backs and flip on the way; the player's own does not flip — they were
  looking at its face when they pressed it.
- **An attack is a lunge and a return**, with both sides taking their damage in the same instant, because the rules
  resolve combat simultaneously and an animation that shows one side hitting first teaches the wrong rule. Deaths
  follow, and **the bodies land in the graveyard**: the graveyard count that goes up is visibly the count of what just
  died, rather than a number changing on its own.
- **Damage lands before the Den's hearts change**, for the same reason.
- **Effects resolve in the rules' own FIFO order, one beat per step**, so a chain of Hello and Goodbye triggers reads
  as a sequence of things that happened rather than one board that rearranged itself.

The server publishes each resolved action promptly; it does not hold the rules gate for animation. Bot thinking and
human decisions remain genuine waits. The client animates the results it already knows, then settles while waiting
for another update. It never predicts an opponent's next move or stretches a flight across network silence.
Client animation lengths remain query-string configurable so tests can observe a beat without racing it.

**The client is allowed to trail, and must be able to catch up.** A turn in this game is a stream of updates, not one
move, and an opponent playing quickly can queue beats faster than they play. The queue therefore compresses as it grows
without discarding events or interrupting the active animation. When a local decision is available, the remaining
queue targets a two-second catch-up window. Trick reveals retain a short readable interval, so a burst of plays can
exceed that target rather than become invisible flashes. Newly armed turn, mulligan and effect-choice clocks include a
fixed three-second server allowance; this does not gate input, require a client acknowledgement, or extend the clock for
every card played. Slow rendering or a delayed network cannot suspend the authoritative deadline. While the board
trails, its controls still respect presentation and authoritative legality. Explicitly lengthened test beats keep their
requested timing rather than using decision catch-up.

When the player has asked for reduced motion, beats collapse to their end state. That changes the presentation and never
the doctrine: state still only moves on a server update, and the terminal phase is still withheld until the finish has
been shown, even when showing it takes one frame.

## The first frame

A fresh subscribe or a reconnect delivers a whole state with no prior frame, so **it is rendered directly, with no beats
replayed**. A match joined mid-play is drawn as it stands — both rows populated, both graveyards filled, the mana row
already long — rather than replaying six turns onto the screen for a player who missed none of it. An animation of
something the player did not miss is a flourish; one of something they did is a lie. The public play-and-action history
in the match model is what makes this possible: a client arriving on turn seven can render everything that has been
played rather than an empty meadow ([`match.md`](match.md)).

> **The signal for it is a sub-client activation, never the match's identity.** A reconnect to the same match carries
> the same entity id, so a page comparing identities skips its first-frame branch exactly when it is needed: a player
> who drops on turn three and returns on turn six would be shown beats for attacks they never saw. Activation fires on
> every attach *and* every reconnect, which is precisely the condition "a whole state arrived with no prior frame".

Reconnecting mid-match re-attaches the player through their account's match pointer, and the seat's hand arrives with
the fresh subscribe ([`hidden-information.md`](hidden-information.md)). Within the grace window the player picks up
where they were; past it their seat is covered by a bot, and **reconnecting asks for it back** — as does their next
action, which is what covers the player who never disconnected at all. Either way the bot finishes the turn it is
playing and the seat is theirs from the next turn boundary
([`match.md`](match.md#a-covered-seat-is-handed-back-between-turns)). A player who has just cold-booted back onto a
board is unambiguously present, and making them play a card to ask for their own seat would be a worse experience than
the one the cover exists to rescue. The board's covered-seat notice has three lines: the strike that makes the next
lapse cost the seat, the covered seat with its **Take back control** button, and — once asked — that the seat is the
player's from the next turn.

## Locks

A card in a collection can be **locked**, and a locked card can neither lose a rank nor gain one. That is the whole
trade and the UI states it in as many words rather than burying it behind a padlock nobody has a legend for: **a locked
card is safe, and it is done growing.** A player freezing their best card is taking it out of the economy, and that has
to be a decision they made knowingly rather than one they discover three matches later.

Locking is a **Collection** screen affordance. The slot count lives with the cards rather than in a settings page — two
slots to begin with, more as the account progresses — an empty slot reads as an empty slot, and locking a card with none
free asks which lock to move instead of refusing the press.

**Only a card that has grown can be locked** (`Global.MinLockRank`, and the reasoning is
[`game-design.md`](game-design.md)'s). A tile below the threshold draws no padlock at all, so a fresh account — every card
at the floor — sees no lock affordance anywhere; the card detail keeps a disabled one and says why, since that is
where a player asks. Unlocking is always offered on a locked card whatever its rank is now.

**Locks are open information.** The padlock rides the card frame wherever a card is drawn: the collection, the
deckbuilder, the hand, the board, and the Heist lineup — and the opponent's locks are drawn too. Both players can
therefore see, mid-match, which of the cards on the table are actually at stake. That is the same honesty the pre-match
reveal exists for, moved down to the individual card.

**Lock state freezes when the player enqueues, along with the deck.** The wager agreed on the pre-match screen cannot
move underneath either player, so the client makes that visible rather than letting someone believe a lock they changed
mid-search has taken effect: while a search or a match is live, the Collection screen shows locks as they were frozen
and says that a change applies to the next match.

## The Heist

The signature moment, and the only screen that takes the whole window without the board under it; the board has already
settled by the time it appears. **Only the winner has anything to do** — the loser protected what they protected before
they queued ([Locks](#locks)) — and **the way out is the screen's own**, on both sides and at both stages, because every
overlay covers the board chrome.

- **The winner picks.** The lineup is every card the loser played. Locked ones are in it, padlocked and unpickable,
  rather than quietly missing, so the lineup is the game the winner just watched. Each pickable card states what taking
  it would do — a rank gained, **NEW** where the winner does not own it, nothing where their copy is at rank 5 or
  frozen. Selecting spells out the transfer on both sides, *your copy rank 3 → 4, theirs 3 → 2*, and the commit is a
  deliberate press. **The transfer, read by both players, is the point of the screen**: hiding the cost in a for-keeps
  economy is the one dishonest thing this game could do.
- **A tier that steals nothing, or a lineup with nothing pickable, lands on the plain result panel** and says which
  case it was. An upset that pays double asks for its two picks here.

The pick carries a deadline ring with a default behind it. The ring shows for the whole pick rather than only as it
closes — there is nothing else on screen moving — and where the default was taken both clients say so. **A player who
leaves mid-pick is not waited for**: the picks a departing winner still owed are defaulted at once. The screen is a
**presentation of a server-side transaction**; the client shows the pick it sent and then the collection it is given
back ([`match.md`](match.md#delivering-the-result-and-the-heist)).

Matches with no stakes — practice and the ranked bot fallback — end on a plain result with **Play again** and
**Leave**, and say why nothing was at stake. Play again raises the searching dialog over the finished screen, and a
search the server refuses says so there. An `Abandoned` table clears both accounts' pointers, so its players meet Home's
own match-gone notice. **The client never skips the Heist**: it is what makes leaving cost exactly what staying would.

## When the connection drops

A server-authoritative board is dangerous when the connection dies, because it stays on screen looking interactive while
every press goes nowhere — and here the turn deadline is running the whole time, so the player is not merely confused,
they are losing the turn. The client closes this at the **app shell**, not per screen: the shell watches the transport's
own health and raises a modal that takes input away from the whole app. The board never checks connection state.

**One connection UI, and it belongs to the shell.** A screen that renders its own connecting spinner and its own retry
button does not add reassurance; it contradicts the shell, and its copy is the one the player never sees, because the
shell renders over it. A hidden "Try Again" wired to a reconnect is one layout change away from a kick loop. A screen
renders the data it has or says it has none, and nothing else.

**Health, not connection state, is the signal.** A socket that dies inside a live session leaves the SDK still reporting
a connection while it spends its resume budget trying to get the same session back. Waiting that out is exactly the
failure this exists to prevent, so what the shell reads is the connection's health, which the SDK clears the moment the
transport is gone.

**A cold start is not an outage until it gives up.** Until a session has started once there is nothing to have lost,
so the screen's own loading state is the honest thing to show; a first connection that exhausts its retries raises the
shell's "Can't reach the server" modal with **Try again**. The status line in the session menu reads Connected,
Connecting…, Connection lost or Offline, and there is no Reconnect anywhere but the shell's own modal.

Four cases, because they are not the same event:

- **A backgrounded tab is not an outage.** The browser throttles the client's update pump when the player switches away,
  so the link often lapses through nobody's fault and heals a beat after they return. For a short window after the tab
  regains focus, a lost link shows as a quiet pill and retries at once, rather than as a modal that reads like an error
  for what was really a resume.
- **A second tab is terminal.** Two tabs on the same browser profile log in as the same player and the newer session
  force-terminates the older. An older tab that auto-reconnected would kick the newer one, whose reconnect would kick it
  back, forever. So a session lost this way is not retried: the tab parks with a notice and an explicit "use this tab
  instead", and it offers to take the session back, because the newer tab may already be closed and a dead page with no
  way on is worse than a player who chooses which tab wins. What it must never do is choose for them.
- **A desync is its own reason.** The replicated model diverging from the server's is recoverable — the SDK drops the
  entity and pulls fresh state — and reads as "re-syncing your match", not as a network error. A desync is a bug, so the
  member-level detail belongs in a server-side report and never in the player's face; in development the match checksums
  every operation, so the diff is logged at the operation that diverged rather than arriving later as a mystery
  reconnect.
- **Everything else** is a plain link loss: a bounded retry loop, then a manual retry.

The SDK has **no error of its own** for the desync case. A mid-session mismatch on a *multiplayer entity's* timeline
goes through a default handler that closes the connection with a generic terminal error, which is wrong in both halves:
a shell classifying it reports an unreachable server, and *terminal* stops the reconnect that would fetch the fresh
copy. The game names it instead, as a transient error the shell recognizes — otherwise the one desync route this design
most expects is the one route that reads as an outage. A mismatch can come from a rules divergence as well as a
transport fault, so the game expects to meet it in development.

## The board is never simulated locally

There is **no offline match mode** ([`match.md`](match.md#two-layers-deliberately-separated)); the offline player loop
is development tooling for the meta screens. The client does hold the rules code and executes it — every action the
server issues is re-executed against this client's copy of the model — but that is replication, not simulation: the
client never chooses an action, never writes the timeline and never advances the game on its own, and if its replay
disagrees with the server's the session ends rather than the board drifting.

## Testability

Nothing here is observed by chance. The pure functions the client leans on — deck legality and Power Score, the hand
fan's geometry, which of a loser's played cards a Heist lineup offers, what the searching dialog says for a given status
and how its countdown rounds, and the shell's classification of a connection failure into the four cases above — are
unit tested away from the browser. Everything else is Playwright against a live server, with the client's beat knob
forced so a test watches an animation instead of racing it, and the match's own deadlines driven through the server's
timing options rather than by waiting.

## See also

- [`game-design.md`](game-design.md) — the game, and the UI it asks for.
- [`art.md`](art.md) — the card frame, clan and rarity languages, and the motion rules.
- [`match.md`](match.md) — what the client is a view of.
- [`hidden-information.md`](hidden-information.md) — hands, addressed changes, and the legality promise.
- [`protocol.md`](protocol.md) — sub-client, channel listeners, the model's clock, and the timing knobs.
- [`matchmaking.md`](matchmaking.md) — what the searching dialog is showing.
- [`bots.md`](bots.md) — covered seats, practice, and the ranked fallback.
- [`meta.md`](meta.md) — the account state the out-of-match screens read and write.
