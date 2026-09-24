# Player State and the Meta Screens

The account behind the out-of-match loop: what it is made of, why the server owns every part of it, and how the
starter grant, locks and card gifts work. The screens themselves — Home, Collection, Deckbuilder and the Locks
affordance — are [`client.md`](client.md)'s.

## What the account owns, and why the server owns it

An account is: which cards a player has and at what rank, the decks they have saved, which cards they have locked, a
ranked record and rating, and card-gift progress. All of it lives on the player model and changes only through
server-validated player actions.

The match's state is authoritative because most of it is *secret*. The account's is authoritative because it is
*valuable* and mostly *open*. A card's rank is real property the Heist moves between two accounts; a deck's legality
and Power Score decide who a player is matched against and what a match pays; a lock is the one exemption from the
wager. None of that is hidden, but all of it has to be a fact the server can vouch for: a client that could assert its
own rank, legality or lock would forge the property and the stakes the rest of the game is built on.

Because the account is open, it is also safe to put in front of an operator, and the LiveOps Dashboard shows the
collection with its ranks and padlocks and the saved decks with their Power Scores. One consequence is visible there:
**a rank above the floor is the only trace a Heist leaves.** Nothing records which cards were taken from whom — the
picks ride the result and are applied and gone.

The account needs no protocol beyond what player actions give it: one player, one model, one channel. The client runs
the same shared validation to predict an action, the server re-runs it as the authority, and shared code keeps the two
from disagreeing.

## The starter grant and starter decks

A new account receives the cards marked `InStarterCollection` in the card config, at the floor rank, in the hook the
SDK runs exactly once when a player model is first created — so the grant needs no flag and cannot double-apply on a
retry, a reconnect or a resumed session. Which cards make up the grant is content, not code.

Alongside it the config authors a set of **starter decks**, every card of which is in the grant (the config build proves
it), so a new account can play any of them on its first login and the deckbuilder is optional on day one. A starter
deck is a card list and nothing more: seated by a veteran it carries that veteran's ranks and scores their Power Score.

An account created before a card existed never runs the grant again, so it lacks that card, and a starter deck naming
it is refused like any other deck with a card the account does not hold. The sample's answer is to reset development
data — there are no real accounts to preserve.

## Deck legality and Power Score

A deck is exactly 25 cards, singleton, from at most two clans. Legality and Power Score (the sum of the deck's card
ranks) are pure shared functions of a card list and the game config, called by the client to paint legality live as a
deck is edited and by the server when a save commits. Power Score is the account-side half of a promise made across
three systems: matchmaking pairs on it, the stakes tier is set by the gap between two decks' scores, and the pre-match
panel shows it to both players ([`matchmaking.md`](matchmaking.md)).

## Locks

A lock is plain account state, changed by a validated action at any time. A match's stakes are set once, from a
snapshot of the deck and the lock set taken when the player commits to the queue, so nothing that happens to the
account afterwards — including a lock changed mid-search — can move a wager a match has already agreed to.

The slot count is account state rather than a constant, so it can grow with progression without a shape change.
**A card has to be worth locking**: `Global.MinLockRank` is the lowest rank the lock action accepts, because at the
floor there is no growth to freeze and the floor already protects the card. Clearing a slot is never refused by that
rule, so a lock set before a threshold moved can always be undone.

## Card gifts

Completed matches bank gift progress through the same idempotent result delivery that applies the match, and every
configured number of matches (`Global.MatchesPerCardGift`; practice counts when `Global.CardGiftIncludesPractice`)
earns one uniformly random unowned collectible at rank 1. The meta screens claim a ready gift automatically, and its
notice persists on the account until acknowledged. A full collection earns nothing, and a gift never grants ranks: every
rank above the floor is won at the table.

## See also

- [`game-design.md`](game-design.md) — the collection, ranks, locks, decks, Power Score and acquisition.
- [`client.md`](client.md) — the screens, their interaction rules and the Locks UI.
- [`matchmaking.md`](matchmaking.md) — where Power Score and the frozen lock set are consumed.
