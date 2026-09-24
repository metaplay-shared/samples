# Hidden Information

Keeping each player's hand genuinely secret in a game that deliberately opens almost everything else.

## The invariant

> **The server holds the complete state, including both hands and the deal that produced them. A client receives only
> what its seat may see, and cannot reconstruct the rest. Nothing secret is sent and then hidden.**

The second clause matters as much as the first, and it is the one that is easy to leave out. A design that transmits
nothing secret can still be wide open if the secret is *derivable* from what it does transmit. This game leans on open
information as a design pillar — open graveyards, open deck contents, a public Weather — which makes derivability the
failure mode to watch, not accidental transmission. [The subtraction
problem](#public-deck-contents-and-the-subtraction-problem) is what taking the invariant literally finds.

## The seed is part of the secret

The deal is a deterministic function of a seed. Both clients know the match's entity id, and can know the time the match
was created. So:

> **The deal seed must be cryptographically random.** A seed derived from the entity id, a tick count, a timestamp, or a
> process-wide random source lets either client recompute both shuffles and read the opposing hand and the whole deck
> order — and the transmission invariant above reads as perfectly satisfied the whole time.

The RNG **position** is secret for the same reason. It is held under the secret member and never leaves the server,
including in a per-seat payload: the stream can be replayed forward in closed form, so a position plus a seed is the
rest of the deal.

The Weather is drawn from the same stream and is immediately public, which looks like a leak and is not — but the
reasoning has to be done rather than assumed. A pool of six Weathers reveals under three bits of a cryptographically
random seed, which is not a search anybody can run. What follows is a rule rather than a case-by-case judgement:

> **No public value may be a raw draw from the RNG stream.** A public value derived from the stream is fine when the
> derivation destroys nearly all of it — one of six Weathers does. A published die roll, a published shuffle index, or
> a published "random card" identity does not, because it hands back the draw itself and narrows the seed by exactly as
> much as it is worth.

The game's randomness is input-shaped by design — singleton draws and a public Weather, never dice on outcomes — so this
rule costs nothing today. It is written down because the first card effect that wants to flip a coin in public is the
one that would quietly break it.

**Who goes first is the second exemption, on the same reasoning.** It is a raw draw and it is published immediately,
but one bit is not a search: it narrows a sixty-four-bit seed by half. Both exemptions are listed rather than derived
per case, because the next public value somebody wants will be argued by analogy with these, and the analogy only holds
while the published quantity is a handful of bits wide.

### Handing the seed to a policy

The bot policy takes a seed for its profile's seeded imperfection ([`bots.md`](bots.md#personalities)), and a host
that passed the *deal* seed would hand the game's one real secret to an object whose whole purpose is to be called from
everywhere. So the host draws an **independent bot seed** from the same cryptographic source and stores it beside the
deal seed; the policy never holds the deal even in principle. Two further things keep that from resting on discipline:
the substream derivation is **deliberately lossy** — every other input to a substream key (seat, decision kind, action
count) is public, so the key is folded in half and has about four billion preimages — and the policy
structurally cannot reach the deal, because it reads the board through a seat view.

## The state splits three ways

Metaplay replicates a multiplayer entity's timeline identically to every subscriber and checksums it, so a member whose
value differs per viewer cannot live *on* it. It can nonetheless be carried *by* it, addressed to one member
([`protocol.md`](protocol.md#what-replication-actually-guarantees)).

| What | Where it lives | Who receives it |
|---|---|---|
| The public board — Weather, stakes, seats, Den HP, mana, board critters and their full state, both graveyards, both unseen pools, hand and deck **counts**, both players' **locked cards**, phase, turn, action count, deadlines, the action history, the Heist eligibility lists | The replicated timeline | Both clients, identically |
| The authoritative game — both hands, both deck **orders**, the RNG position, the bot seed, the hidden half of the card registry and a held peek's reveal | The one `ServerOnly` submodel tree, `MatchModel.Secret`: held only in the authoritative copy, never sent, never checksummed | Nobody |
| A seat's own hand, and what a held peek is showing it | A client-only view beside the model, **carrying no serialization attribute at all**. The baseline arrives as subscribe-time private state; every change after it as an **addressed timeline operation** | That one client |

The second and third rows are mirror images. The secret is held by the server and by no client; the client's own view
is held by one client and never by the server. The first is `ServerOnly` — stripped from the wire and from the
checksum. The second is not serialized at all, because it has nothing to be hidden *from*: it never travels on this
model's wire in either direction. What crosses between them goes one way only: the server reads the secret and
addresses what one seat may know to that seat.

## What is actually secret in this game

Less than in most card games, and being explicit about it is what keeps the design small:

- **A hand is secret** — its contents, not its size. The count is public and is on the board.
- **Deck *order* is secret.** Deck *contents* are not (with the qualification below). Singleton decks make "what is
  left" a knowable, plannable fact, and knowing it without knowing when it arrives is the decision the game wants.
- **A played card is public immediately.** There is no face-down commit, no simultaneous reveal, no bluffing layer.
- **Everything on the board is public** — damage, buffs, keywords granted in play, ranks and the stats they produce.
  Sneaky changes what may be *targeted*; it does not hide anything.
- **The mulligan is private while it is happening and irrelevant afterwards.** Both seats mulligan at once; neither
  learns what the other swapped, and the cards return to the unseen pool they were already in.
- **Locks are public, deliberately.** A secret lock would turn the stakes into a bluff, and the wager has to be legible
  before it is agreed. Locking says only that a card matters to its owner, which the deck list already suggested.

The consequence is that **a normal move leaks nothing**: its address — this card, that critter — becomes public at the
moment it is made. Moves are nonetheless sent as **directed messages** rather than as actions on the shared timeline,
for robustness ([`match.md`](match.md#the-actor-is-the-only-writer)). That is not a secrecy argument, but it is also
what would keep a future face-down or choose-in-secret mechanic honest.

### Visibility is not stealability

The design's most-quoted line about information is that your whole deck is knowable but only what you *play* is
stealable. Both inputs to the second are public anyway: the Heist's eligibility list is a projection of the action
history minus the loser's locked set, so the winner learns nothing at the Heist screen that they could not have read off
the board an hour earlier. **Nothing about the Heist is a hidden commitment**, and no part of its UI should be built as
though something were.

## Public deck contents and the subtraction problem

Taken literally, "hands hidden" and "the remaining deck is open" contradict each other, and the way they do is exactly
the derivation the invariant's second clause exists to catch:

> If the remaining *deck* is published as its own list, then every draw removes exactly one card from a public list and
> adds it to a hidden one. Diffing the published list across a turn boundary names the drawn card. Over a match, that is
> the whole hand.

Nothing about the transmission is wrong — no hand is ever sent — and the leak is total anyway. It is also not a leak an
implementation would notice, because both halves look correct in isolation. The only self-consistent form of the rule
is one public pool per player:

> **Each player has a public *unseen pool*: the multiset of their cards that have not yet become public.** It is the
> deck plus the hand. A card leaves the pool when it becomes public — played, discarded, destroyed, or revealed by an
> effect — and never when it merely moves between the deck and the hand. The deck count and the hand count are public
> separately, so a player can still see how close their opponent is to Tuckered Out.

You still know what is left in their twenty-five and cannot know when it comes; what you lose is the ability to diff a
draw, which was never something the design offered on purpose. Three consequences change what an implementation writes:

- **A card revealed to one player only moves nowhere.** An effect that shows a seat the top cards of its own deck must
  not touch the pool: what the seat saw is addressed to it alone. A card revealed *publicly* leaves the pool for both
  viewers at once.
- **The pool's contents and the two sizes are public**, so Tuckered Out timing is public information — unavoidably and
  by choice.
- **The public card entry says `Unseen` for anything in a deck or a hand that has not become public**, naming no card
  and no rank. This is a **type** rule rather than a projection rule: the card registry is replicated, so an entry that
  distinguished "in the deck" from "in the hand" would split the pool and bring the subtraction back with nothing to
  report it.

The next zone somebody adds — an exile pile, a face-down set, a peek — poses the same question and deserves the same
test: *does anything become derivable by diffing this zone across a change?*

## A card is addressed by its match instance identity

An intent names a **match instance**: a per-match identity minted for every card when it enters the match. It never
names a position in a hand, a slot on the board, or a card's catalogue identity.

**Hand positions are unstable.** A hand is a list that changes one card at a time from both ends of the game, so an
index into the drawn row is not a stable address for the card under the player's finger. The failure reads as "tapping
the rightmost card played a different one", which looks like a rendering bug and is not.

**Catalogue identity is not unique either.** Decks are singleton *per deck*, not per match: both players may run the
same card, and effects that copy a card out of the enemy graveyard put a second instance of somebody else's card into a
hand that may already hold its own — exactly the cases the Moonlight Raccoons are built around.

> **An instance identity is an address, and an address that encodes where the card sat in the shuffled deck is the deck
> order in disguise.**

So instance identities are minted in a **canonical, shuffle-independent order** — over the deck list as authored,
before it is shuffled — never in draw order and never from a counter advanced as cards are dealt. Instances created
during play (tokens, copies) take identities from a counter, which is safe because they are created by public events in
a public order.

## Delivering a hand

A hand reaches its client two ways, and the split is by *when*:

- **At subscribe** — the SDK's per-subscriber private state carries the seat's whole current hand. This covers the deal
  and every reconnect, with no request/response and no race against the channel becoming known. It is the
  **baseline**, and the only time a hand travels whole.
- **Thereafter** — one **addressed timeline operation per card**: `MatchOwnCardGained` when one arrives,
  `MatchOwnCardLost` when one leaves, and `MatchOwnPeekRevealed` for what a held peek is showing. The addressed member
  receives the operation and every other subscriber a no-op at the same position, under the same checksum; the server
  runs the no-op too, because it reads hands out of the secret. The SDK call behind this is `ExecuteActionPerMember`,
  which this sample takes from a vendored copy of an SDK change not yet in a release
  (`Backend/Server/SdkPreview/README.md`); [`protocol.md`](protocol.md#what-replication-actually-guarantees) has the
  mechanics.

That is not a short list of events: the mulligan, the start-of-turn draw on *every* turn, every draw or tutor effect,
every bounce to hand, every card copied into a hand, hand overflow returning a card to the deck, a lapsed deadline
played out by the bot policy, a covering bot's turn, and a reclaim. **Departures are carried the same way as
arrivals** — a card the seat played is removed by the same kind of record a drawn card is added by, never inferred from
a confirmed play.

**The operation is queued on the statement that moves the card.** Every write to a seat's hidden hand goes through one
add and one remove in `SecretOps`, each of which appends to `MatchModel.Outbox`, so what the owner is told cannot
disagree with what happened. The host drains the outbox in `StageMatchAction` — the one path a match action takes onto
the timeline — so each addressed operation is staged directly after the operation that produced it, and no host path
can stage an action and forget to deliver what it queued. A seat with no session has its operations dropped and gets a
fresh baseline when it next subscribes. The queue is ordered, which keeps a hand a list rather than a set: a card can
leave and re-enter within one action and land where the secret side put it.

**The outbox is not state, and it is not secret.** It is a plain field with no serialization attribute — never sent,
never checksummed, never in a copy — because it is drained inside the actor turn that fills it. What the server *knows*
and what it still *owes a client* are different things, and only the first belongs under the secret.

**There is no position stamp and nothing is held.** A per-viewer message is dispatched immediately while a timeline
change flushes at the end of the actor turn, so a hand sent beside the timeline would routinely arrive before the
operations it explains, and would need a stamp and a client-side hold — plus an exception for a payload carrying an
interactive question, which must not wait. An operation that rides the same flush cannot outrun them.

**Why deltas rather than the whole hand.** Resending the hand is O(private state) per change; a delta is O(change) and
never worse. Nine cards would survive either, but a per-viewer inventory of several hundred entries resent because one
moved would not, so the sample shows the shape that scales.

**What a delta must not carry.** `MatchOwnCardGained` names the instance, the card and the rank, and *not* what the
card costs to play. A cost is derived from public state — the rank, the Weather, how many tricks the seat has cast this
turn — so a delivered copy would be stale the moment the Weather turned; it is computed where it is shown, through the
same rule the server uses. The general rule: **a payload that maintains state by deltas must not carry anything derived
from state it does not maintain.**

**Routing is not payload.** An addressed operation names no seat: the SDK hands it to one member, so a client holding
one is the addressee by construction. Which member to address rides the outbox entry beside the operation, and it comes
from the same instance lookup that found the card in the secret, so an operation cannot name a seat that does not own
the card it describes.

## The legality promise, stated honestly

The client computes what is playable — affordable, targetable, attackable — from its own hand, the public board and the
Weather, using **the same shared functions** the server validates with. That is not by itself a guarantee they agree,
because the two calls have different *inputs*: the server passes the authoritative hand and the client the copy it was
delivered. The addressed operation is what keeps the inputs together, and because it rides the timeline, the hand and
the board it is judged against move at the same position:

> One legality implementation, and one authority. The client's hand is updated, in timeline order, whenever the server
> changes it — so the highlight is trustworthy in every state the design produces, and a refusal, when one happens, is
> reported to the client rather than swallowed.

The board half of the input needs no qualification: it is on the replicated timeline and checksummed, so the two sides
cannot disagree about it without the session ending.

## What keeps it honest

Secrecy that depends on nobody writing the wrong field is not secrecy.

- **The secret is one member, reached through one file.** `MatchModel` has exactly **one** `ServerOnly` member — the
  secret tree, which only a host has — and every read and write of it lives in `SecretOps`, whose methods all begin by
  resolving the accessor and returning when it is null. A new field on the model is **public unless somebody puts it
  under the secret**, which is the opposite default from a projection that names each public field. That is enforced
  by tests rather than by a type: a shape test asserts that the secret is the only `ServerOnly` member anywhere in the
  model's tree, another that the client's own view carries no serialization attribute, and a source-level test that no
  file but the three entitled to it touches the secret. It is a real trade — a test can be deleted, and a type could
  not be worked around — and the last section says when it would be the wrong one.
- **The delivery of a hand is checked against the hand it describes.** A dropped, doubled or reordered operation leaves
  the public model identical and the client's hand wrong, so self-play replays the operations against a mirror after
  every action and compares it with the authority's hand. An addressed operation is also asserted to carry only what
  happened — instance, card and rank, or an instance — so it cannot grow a member about the other seat; and the
  per-member substitution itself is tested here, because the sample carries the SDK copy that performs it.
- **What none of them checks is the choice of recipient**, and that is a deliberate, narrow trust: one expression in
  one place, derived from the instance lookup above.
- **The bot policy reads a seat view**, a type that names the public members and nothing else, pinned by a reflection
  test. The bots are its most demanding consumer, because a bot that plays well by cheating is indistinguishable from
  one that plays well.
- **Indistinguishability, not just absence.** Self-play asserts that a seat's delivered payload and the model's actual
  wire form — `SerializeTagged` under the network mask — are **byte-identical across two deals that seat cannot tell
  apart**. Two deals are indistinguishable to a seat when they agree on that seat's own hand, both boards, both
  graveyards, both **unseen pools as multisets**, both counts, the Weather and the action history, while differing
  freely in deck order and in how the opponent's pool is split between deck and hand. That last clause is what turns
  "deck contents are public" from a hole into a testable statement.
- **The rules are re-executed against a stripped model.** Every action of every self-play game runs a second time
  against a clone round-tripped through the network mask, comparing the SDK's checksum bytes and the emitted events.
  That is a replication-equality check, and it covers the gap the indistinguishability sweep leaves: a **stored** public
  member written from hidden state is a checksum difference on the first action that writes it.

Every one of these carries negative controls — a hand deliberately made public, an identity minted in draw order, a
pool deliberately split, an action that reads a hand for a public number — and a suite whose negative controls pass is
broken no matter what its positive cases say. The catalogue is in [`bots.md`](bots.md#negative-controls).

## Where the line is that would flip this decision

This game keeps its whole hidden surface in two hands, two deck orders and one seeded stream, and every rule that
touches one of them reduces to a count. That is what makes "the model is the game" safe here. Three mechanics would flip
it, and a game with any of them should build a per-viewer projection instead:

- **Face-down cards.** A card in play whose identity is secret puts a hidden value inside the *public* board — the one
  structure that has to be identical on both clients. A projection can show a back and keep the identity in the
  authoritative half; a replicated model cannot, because the checksum covers what both clients hold.
- **Secret choices with a public consequence.** Anything simultaneous — a committed bid, a chosen attacker revealed at
  once — means an action whose public mutation depends on a hidden input, which is exactly the prohibition the rules
  rest on ([`rules.md`](rules.md#the-rules-run-twice)). The mulligan works because all it makes public is a
  count; nothing makes a hidden input drive a public outcome work.
- **On-draw triggers.** The moment "what you drew" changes public state, a draw becomes a reveal and every draw needs a
  payload. One such card is survivable; a pool of them means every action carries an identity, and then the projection
  is simpler and safer.

The tell, in one sentence: **if a hidden input has to drive a public mutation, project. If every hidden input reduces to
a count or arrives in a payload, the model can be the game.**

## See also

- [`rules.md`](rules.md) — the rules that obey all of this, and the discipline that keeps them obeying it.
- [`protocol.md`](protocol.md) — the SDK mechanics behind the private channels.
- [`bots.md`](bots.md) — the seat view, and the self-play checks and their negative controls.
- [`match.md`](match.md) — the entity this shapes.
- [`client.md`](client.md) — what the receiving end does with a hand.
- [`game-design.md`](game-design.md) — the game's own statement of what is public.
