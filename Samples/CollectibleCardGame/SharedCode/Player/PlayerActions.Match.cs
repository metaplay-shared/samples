using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Start a practice match against a bot. Practice does not touch the queue at all — it mints a bot table
    /// on the spot (<c>Docs/matchmaking.md</c>) — so this is the mode rather than a stand-in for
    /// the ranked queue, which deletes nothing.
    /// <para>
    /// The action validates the deck and refuses a second entry; the table itself is minted by the actor,
    /// through <see cref="IPlayerModelServerListener.StartPracticeMatch"/>. Creating an entity is an external
    /// side effect that cannot be un-created, so it cannot happen inside an <c>Execute</c> that the client also
    /// runs.
    /// </para>
    /// <para>
    /// <b>The commit body writes nothing that depends on the payload.</b> The entry refusal is decided by
    /// <see cref="PlayerModel.CurrentMatch"/>, which is <c>ServerOnly</c> and reads default on the client, so
    /// the predicted run always reaches the commit body and the authoritative one may not; a payload-dependent
    /// public write would diverge the two models on any refused entry. The deck is therefore recorded by
    /// <see cref="PlayerNoteDeckPlayed"/>, server-side, where the pointer is assigned.
    /// </para>
    /// <para>
    /// <b>The one write left, clearing <see cref="PlayerModel.MatchGoneUnseen"/>, is safe only because a live
    /// match pointer and an unseen match-gone notice never co-exist.</b> Every site that raises the notice
    /// clears the pointer just before it (<c>PlayerActor</c>'s session handshake, both branches, and its admin
    /// clear-pointer handler), so a run that reaches this body with the flag set is not in a match and both
    /// sides clear it together. A path that set the notice while a pointer was live would break this action.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerStartPracticeMatch)]
    public class PlayerStartPracticeMatch : PlayerAction
    {
        public DeckChoice   Deck       { get; private set; }
        public BotProfileId BotProfile { get; private set; }

        public PlayerStartPracticeMatch() { }

        public PlayerStartPracticeMatch(DeckChoice deck, BotProfileId botProfile)
        {
            Deck       = deck;
            BotProfile = botProfile;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            DeckChoiceResult resolved = DeckChoiceResolver.Resolve(Deck, player);
            if (!resolved.IsValid)
                return ActionResults.ForChoiceError(resolved.Error);

            // The same shared check the save ran, re-run at entry: a deck saved when the account owned every
            // card in it is still legal, but re-checking costs nothing and keeps one definition of legal. A
            // starter deck goes through the identical call — its ownership is not exempted, merely satisfied,
            // because the starter grant hands out every card a starter deck may name.
            DeckValidationResult legality = DeckValidator.ValidateForPlayer(resolved.Cards, player.GameConfig, player.Collection);
            if (!legality.IsValid)
                return ActionResults.ForDeckError(legality.Error);

            // Server-side only, by construction: CurrentMatch is a ServerOnly member and reads default on the
            // client, so the client's predicted run always passes here and the authoritative run is what
            // refuses a double-tapped Play.
            if (player.IsInMatch)
                return ActionResults.AlreadyInMatch;

            if (commit)
            {
                // A new match makes the last one's notice stale. Safe here only by the invariant in the doc.
                player.MatchGoneUnseen = false;
                player.ClientListener.Root?.GenericPropertyChanged(nameof(PlayerModel.MatchGoneUnseen));

                player.ServerListener.StartPracticeMatch(Deck, BotProfile);
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Remember the deck this account has just been seated with, so Home's picker opens on it next time.
    /// <para>
    /// <b>Synchronized server</b> for the same reason <see cref="PlayerNoteMatchGone"/> is: the member it
    /// writes is public and therefore checksummed, and an unsynchronized server action runs at a different
    /// timeline position on the two sides.
    /// </para>
    /// <para>
    /// It is issued by the actor at the moment the pointer is assigned rather than from
    /// <see cref="PlayerStartPracticeMatch"/>'s commit body, and that placement is the whole point: the entry
    /// refusal is decided by <c>ServerOnly</c> state, so a deck recorded in the predicted run would be a deck
    /// the server never recorded — a checksum mismatch, and the end of the session. Recorded where the seat
    /// is, it can only say a deck this account was actually seated with. Ranked entry issues
    /// this same action from its own seating path.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerNoteDeckPlayed)]
    public class PlayerNoteDeckPlayed : PlayerSynchronizedServerAction
    {
        public DeckChoice Deck { get; private set; }

        public PlayerNoteDeckPlayed() { }

        public PlayerNoteDeckPlayed(DeckChoice deck)
        {
            Deck = deck;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                player.LastPlayedDeck = Deck;
                player.ClientListener.Collection?.GenericPropertyChanged(nameof(PlayerModel.LastPlayedDeck));
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Forget the match this account is pointing at, without applying anything.
    /// <para>
    /// <b>Development-only</b>, and it exists for exactly one reason: an end-to-end test that has to get an
    /// account back into matchmaking without playing its table out. The real routes that clear a pointer are
    /// the result-delivery acknowledgement and the two lockout-breaking paths in the player actor, none of
    /// which a client can ask for. Clearing without applying is safe only because the table goes on
    /// re-offering and the account applies idempotently — what is given up is the proof, not the result.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerClearMatchPointer)]
    [DevelopmentOnlyAction]
    public class PlayerClearMatchPointer : PlayerAction
    {
        public PlayerClearMatchPointer() { }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
                player.CurrentMatch = EntityId.None;

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Fold one match's outcome into the account's record, <b>and move the ranks the Heist took</b>.
    /// Server-issued and <em>synchronized</em>, because <see cref="PlayerRecord"/> and
    /// <see cref="PlayerModel.Collection"/> are both checksummed state: an unsynchronized server action runs
    /// at different timeline positions on the two sides, which is exactly what a checksummed member cannot
    /// survive.
    /// <para>
    /// It touches nothing server-only, so its replay on the client is identical to its run on the server.
    /// Idempotence is not in here: the actor checks <see cref="PlayerModel.AppliedMatchResults"/> before
    /// issuing it, because that set is a server-only fact this action must not read. What that gate buys is
    /// worth stating plainly: <c>Collection[card]++</c> run twice would double a rank, and it is safe here
    /// only because it structurally cannot run twice.
    /// </para>
    /// <para>
    /// <b>The transfer is here rather than in the actor for the same reason the record bump is.</b> The
    /// collection is public and therefore checksummed, so only an action may write it — and the body being a
    /// pure function of (payload, public model) is what makes the client's replay agree. That is also what
    /// pays for the live lock re-read the design requires: <see cref="PlayerModel.LockSlots"/> is public, so
    /// the client reads the same set the server did, and the collection screen updates with no round trip.
    /// </para>
    /// <para>
    /// The payload therefore cannot carry a pre-computed new rank. The whole point of re-reading is that the
    /// table does not know what rank this account holds the card at, or whether it has been locked since the
    /// pick — a delivery is not simultaneous with the pick, and a retry lands seconds after it.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerApplyMatchResult)]
    public class PlayerApplyMatchResult : PlayerSynchronizedServerAction
    {
        /// <summary> How the match came out for <em>this</em> account, translated from its seat by the table. </summary>
        public MatchAccountOutcome Outcome   { get; private set; }
        public bool                WasRanked { get; private set; }

        /// <summary>
        /// What this account's rating moves by, already computed by the table from the two ratings frozen at
        /// formation. The number rather than the two ratings, because the opponent's is not this account's to
        /// know — and zero for anything unranked, so the gate below is the same one the counters use.
        /// </summary>
        public int                 RatingDelta { get; private set; }

        /// <summary>
        /// The cards the Heist took, or null when no phase ran. <b>Which side of the transfer this account is
        /// on is not a second field</b>: the Heist only runs on a decided match, so
        /// <see cref="Outcome"/> already says it, and a flag that could disagree with the outcome would be one
        /// more thing to keep in step.
        /// <para>
        /// The same list reaches both accounts, which is what makes the two applications independent: each one
        /// names only its own half and neither needs to know what the other found.
        /// </para>
        /// </summary>
        [MaxCollectionSize(2)]
        public List<CardId>        HeistPicks { get; private set; }

        public PlayerApplyMatchResult() { }

        public PlayerApplyMatchResult(MatchAccountOutcome outcome, bool wasRanked, int ratingDelta, List<CardId> heistPicks)
        {
            Outcome     = outcome;
            WasRanked   = wasRanked;
            RatingDelta = ratingDelta;
            HeistPicks  = heistPicks;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                PlayerRecord record = player.Record;

                if (WasRanked)
                {
                    // Floored at zero, so a run of losses cannot take an account below the bottom of the
                    // ordering. Deterministic from the payload and this account's own public state, which is
                    // what a synchronized server action needs it to be.
                    record.Rating = Math.Max(0, record.Rating + RatingDelta);

                    record.RankedMatchesPlayed++;
                    switch (Outcome)
                    {
                        case MatchAccountOutcome.Win:
                            record.RankedWins++;
                            break;

                        case MatchAccountOutcome.Loss:
                            record.RankedLosses++;
                            break;

                        default:
                            record.RankedDraws++;
                            break;
                    }
                }
                else
                    record.UnrankedMatchesPlayed++;

                if (WasRanked)
                    player.ServerListener.OnRankedRecordChanged();

                player.ClientListener.Root?.GenericPropertyChanged(nameof(PlayerModel.Record));

                ApplyHeist(player);
                if (player.GameConfig.Global.MatchesPerCardGift > 0
                    && (WasRanked || player.GameConfig.Global.CardGiftIncludesPractice)
                    && player.CardGiftProgress < int.MaxValue)
                {
                    player.CardGiftProgress++;
                    player.ClientListener.Collection?.GenericPropertyChanged(nameof(PlayerModel.CardGiftProgress));
                }
            }

            return MetaActionResult.Success;
        }

        /// <summary>
        /// This account's half of the transfer: the winner's copy of each pick gains a rank and the loser's
        /// loses one, both read live off this account's own public state and both floored, capped and frozen
        /// by <see cref="MatchHeistPolicy"/> rather than by arithmetic written here.
        /// <para>
        /// A pick that moves nothing — the ceiling, the floor, a card this account has locked since — is a
        /// mutation that does not happen rather than a payout in something else. The two sides are applied
        /// independently and the loser is told first, so a rank lost that was never gained is the direction a
        /// crash between the two errs in (<c>Docs/match.md</c>, "Two mutations, deliberately not one
        /// transaction").
        /// </para>
        /// <para>
        /// The underdog's two picks are applied in order and may name the same card twice — a loser who played
        /// two copies offers two — so each one re-reads the rank the one before it left.
        /// </para>
        /// </summary>
        void ApplyHeist(PlayerModel player)
        {
            if (HeistPicks == null || HeistPicks.Count == 0 || Outcome == MatchAccountOutcome.Draw)
                return;

            bool         won    = Outcome == MatchAccountOutcome.Win;
            GlobalConfig global = player.GameConfig.Global;
            bool         moved  = false;

            foreach (CardId card in HeistPicks)
            {
                if (card == null)
                    continue;

                bool owns   = player.Collection.TryGetValue(card, out int rank);
                bool locked = player.IsLocked(card);

                HeistRankMove move = won
                    ? MatchHeistPolicy.WinnerGain(owns, rank, locked, global)
                    : MatchHeistPolicy.LoserLoss(owns, rank, locked, global);

                if (!move.Moves)
                    continue;

                player.Collection[card] = move.To;
                moved = true;
            }

            if (moved)
                player.ClientListener.Collection?.GenericPropertyChanged(nameof(PlayerModel.Collection));
        }
    }

    /// <summary>
    /// A match this account was in stopped existing, so say so on the account itself.
    /// <para>
    /// <b>Synchronized server</b> for the same reason <see cref="PlayerApplyMatchResult"/> is: the member it
    /// writes is public and therefore checksummed, and an unsynchronized server action runs at a different
    /// timeline position on the two sides.
    /// </para>
    /// <para>
    /// It is issued from the session handshake, before the session exists, which is safe because an enqueued
    /// server action is durable and runs even with no client present — it simply does not run
    /// <em>synchronously</em>, so the flag reaches the client either in the initial state or a moment later
    /// as an ordinary timeline action. Either is in time for Home to draw it.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerNoteMatchGone)]
    public class PlayerNoteMatchGone : PlayerSynchronizedServerAction
    {
        public PlayerNoteMatchGone() { }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                player.MatchGoneUnseen = true;
                player.ClientListener.Root?.GenericPropertyChanged(nameof(PlayerModel.MatchGoneUnseen));
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// The player has read the notice. An ordinary player action, because dismissing it is the one thing
    /// about the notice a player does and there is nothing to refuse: a dismissal with nothing to dismiss
    /// leaves the flag exactly where it already was.
    /// </summary>
    [ModelAction(ActionCodes.PlayerDismissMatchGone)]
    public class PlayerDismissMatchGone : PlayerAction
    {
        public PlayerDismissMatchGone() { }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                player.MatchGoneUnseen = false;
                player.ClientListener.Root?.GenericPropertyChanged(nameof(PlayerModel.MatchGoneUnseen));
            }

            return MetaActionResult.Success;
        }
    }
}
