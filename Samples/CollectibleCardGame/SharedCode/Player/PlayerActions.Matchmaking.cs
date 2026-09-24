using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// Enter the ranked queue with a chosen deck. A <see cref="PlayerAction"/> rather than a message on some
    /// entity channel, and for the same reason <see cref="PlayerStartPracticeMatch"/> is one: the validation
    /// discipline — the client predicts, the server re-runs the same shared rule authoritatively — only
    /// applies to actions on the player timeline. The queue entry itself is the actor's, through
    /// <see cref="IPlayerModelServerListener.EnqueueForRankedMatch"/>.
    /// <para>
    /// <b>The commit body writes nothing that depends on the payload.</b> Both refusals a second entry can
    /// hit are decided by state the client cannot see — <see cref="PlayerModel.CurrentMatch"/> is
    /// <c>ServerOnly</c> and the search phase is actor-local, so it is not model state at all — which means
    /// the predicted run always reaches the commit body while the authoritative one may refuse. A
    /// payload-dependent write to public, checksummed state would therefore diverge the two models on any
    /// refused entry, and a checksum mismatch ends the session. The deck this account is seated with is
    /// recorded by <see cref="PlayerNoteDeckPlayed"/>, from the seating path, exactly as practice does it.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerEnqueueForRankedMatch)]
    public class PlayerEnqueueForRankedMatch : PlayerAction
    {
        public DeckChoice Deck { get; private set; }

        public PlayerEnqueueForRankedMatch() { }

        public PlayerEnqueueForRankedMatch(DeckChoice deck)
        {
            Deck = deck;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            DeckChoiceResult resolved = DeckChoiceResolver.Resolve(Deck, player);
            if (!resolved.IsValid)
                return ActionResults.ForChoiceError(resolved.Error);

            // The deck the queue matches on is the deck the match is dealt from, so it is validated here and
            // frozen on the ticket a moment later. A list that is illegal now is refused before anybody is
            // paired against its Power Score. A starter deck runs the identical check.
            DeckValidationResult legality = DeckValidator.ValidateForPlayer(resolved.Cards, player.GameConfig, player.Collection);
            if (!legality.IsValid)
                return ActionResults.ForDeckError(legality.Error);

            // One entry per player is deliberately NOT refused here, and the difference from
            // PlayerStartPracticeMatch is the whole point of the directed channel. Both halves of
            // it are decided by state the client cannot see — CurrentMatch is ServerOnly and the search phase
            // is actor-local — so an action result would be a refusal nobody could observe: the model does not
            // change, and the SDK has no route for an action's result. The
            // actor refuses instead, and answers with MatchmakingEnded(Refused), so the searching dialog
            // closes on an answer rather than on its own guard.

            if (commit)
            {
                // A constant, so a refused entry leaves the two models agreeing on it. Starting a search is
                // what makes the last match's obituary stale, and the searching dialog must not go up over a
                // notice about a match two ago — the same clear PlayerStartPracticeMatch makes, for the same
                // reason.
                player.MatchGoneUnseen = false;
                player.ClientListener.Root?.GenericPropertyChanged(nameof(PlayerModel.MatchGoneUnseen));

                player.ServerListener.EnqueueForRankedMatch(Deck);
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Waive or restore this account's own newcomer shield, from the LiveOps Dashboard's Admin Actions card.
    /// <para>
    /// <b>A live LiveOps lever, not a development-only one.</b> The SDK enforces
    /// <c>[DevelopmentOnlyAction]</c> only on actions arriving from a client, so the gate here is the dashboard
    /// permission of <c>AdminActionPlacement.Disruptive</c>: lifting the shield lets the next ranked match move
    /// the account's rating and ranks for real. A synchronized server action, because the flag is public and
    /// checksummed; it is read at enqueue, so a waiver applies from the next queue entry.
    /// </para>
    /// <para>
    /// <b>Both directions are reachable from the generated form, and that takes two things.</b>
    /// <see cref="IPlayerDashboardAction{TModel}"/> is what makes the modal open on the account's real value
    /// rather than on <c>false</c> for every account, and <c>requireInputBeforeAllowingConfirm: false</c> is
    /// what allows confirming a value equal to the one the form opened on. Without the first the form lies
    /// about a waived account; without the second the value that restores a shield is refused as "no changes"
    /// precisely because it now agrees with the model. Setting the flag to the value it already holds is
    /// idempotent — the commit body writes the payload and nothing else — so nothing is lost by allowing it.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerSetNewcomerShieldWaived)]
    [PlayerDashboardAction(
        "Waive newcomer shield",
        "Lifts this account's own newcomer shield, so its ranked matches are tiered by Power Score from the "
        + "next queue entry onwards. The opponent's shield, if they still have one, shields the match anyway.",
        AdminActionPlacement.Disruptive,
        requireInputBeforeAllowingConfirm: false)]
    public class PlayerSetNewcomerShieldWaived : PlayerSynchronizedServerAction, IPlayerDashboardAction<PlayerModel>
    {
        public bool Waived { get; private set; }

        [MetaDeserializationConstructor]
        public PlayerSetNewcomerShieldWaived(bool waived)
        {
            Waived = waived;
        }

        /// <summary>
        /// Seed the generated form from the account's current waiver, so the modal states the truth about the
        /// account it was opened on. Read-only on <paramref name="model"/>, as the interface requires.
        /// </summary>
        public void InitializeDefaultStateForDashboard(PlayerModel model)
        {
            Waived = model.NewcomerShieldWaived;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            // Nothing on screen reads the flag, so there is no listener to notify.
            if (commit)
                player.NewcomerShieldWaived = Waived;

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Lift this account's own newcomer shield, from the client.
    /// <para>
    /// <b>Development-only</b>, and it exists for one reason: the shield covers an account's first
    /// <c>Global.NewcomerShieldMatches</c> ranked matches and a shield on <em>either</em> seat shields the
    /// match, so two fresh accounts — which is what a browser fixture, a load run and a two-device playtest
    /// all have — pair at a tier that moves no ranks and can never reach the Heist at all. The real control
    /// is the dashboard's <see cref="PlayerSetNewcomerShieldWaived"/>, and a client cannot send that one: it
    /// is a server action, deliberately.
    /// </para>
    /// <para>
    /// The same shape as <see cref="PlayerDevSetCardRank"/> and for the same kind of reason — that one is the
    /// only route in the build to a rule that turns on a card's rank, and this is the only route to a rule
    /// that turns on the stakes tier. Query-string gated at its affordance, refused outside a development
    /// environment by the SDK, and its body writes a constant to a public member, so the predicted run and the
    /// authoritative one reach the same state.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerDevWaiveNewcomerShield)]
    [DevelopmentOnlyAction]
    public class PlayerDevWaiveNewcomerShield : PlayerAction
    {
        public PlayerDevWaiveNewcomerShield() { }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                player.NewcomerShieldWaived = true;
                player.ClientListener.Root?.GenericPropertyChanged(nameof(PlayerModel.NewcomerShieldWaived));
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Leave the queue. The predicted run is a no-op that always succeeds: the client cannot know the search
    /// phase, so it never predicts a refusal, and the real defence against "cancelled but seated anyway" lives
    /// on the player actor where the seat reservation commits.
    /// <para>
    /// A cancel that arrives after the seat is committed is refused, silently — the match association is
    /// already inbound, and the client keeps waiting rather than being shown a menu it is about to be pulled
    /// off (<c>Docs/matchmaking.md</c>, "A cancel and a formation cannot both win").
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerCancelMatchmaking)]
    public class PlayerCancelMatchmaking : PlayerAction
    {
        public PlayerCancelMatchmaking() { }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
                player.ServerListener.CancelMatchmaking();

            return MetaActionResult.Success;
        }
    }
}
