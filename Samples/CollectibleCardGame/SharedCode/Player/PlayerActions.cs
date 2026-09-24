using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// Game-specific player action base class, which attaches all game-specific actions to <see cref="PlayerModel"/>.
    /// </summary>
    public abstract class PlayerAction : PlayerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Game-specific <see cref="PlayerUnsynchronizedServerActionCore{TModel}"/>
    /// </summary>
    public abstract class PlayerUnsynchronizedServerAction : PlayerUnsynchronizedServerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Game-specific <see cref="PlayerSynchronizedServerActionCore{TModel}"/>
    /// </summary>
    public abstract class PlayerSynchronizedServerAction : PlayerSynchronizedServerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Registry for game-specific ActionCodes, used by the individual PlayerAction classes. Action codes must
    /// be unique within the game.
    /// </summary>
    public static class ActionCodes
    {
        public const int PlayerSetDisplayName = 5001;
        public const int PlayerClaimCardGift = 5030;
        public const int PlayerDismissCardGift = 5031;
        public const int MatchDebugWin = 5140;

        public const int PlayerSaveDeck       = 5010;
        public const int PlayerRenameDeck     = 5011;
        public const int PlayerDeleteDeck     = 5012;

        public const int PlayerSetLockSlot    = 5020;
        public const int PlayerDevSetCardRank = 5021;
        public const int PlayerSetNewcomerShieldWaived = 5022;

        // 5100-5149: the match block. The 51xx actions on the match timeline are leader-synchronized and
        // server-issued; the 511x ones are player actions. Both live in this one flat registry, which is
        // separate from the MetaMessage codes in MessageCodes and from the rules' own event and intent
        // registries. Retired codes are recorded and never recycled
        // (Docs/protocol.md, "Message codes and namespaces").

        // ---- table actions: the actor's own state, not the game ----
        // 5100 retired (MatchPublishStep)
        public const int MatchSetSeats        = 5101;
        // 5102 retired (MatchSetPacing)
        public const int MatchSetPhase        = 5103;
        public const int MatchSetResult       = 5104;
        public const int MatchSetResultAck    = 5105;
        public const int MatchSetGrace        = 5106;
        // 5107 retired (MatchSetTimings)
        public const int MatchPushClocks      = 5108;
        public const int MatchArmHeistDeadline = 5109;
        // The table-action slots are spent. A tenth would need a new block.

        // ---- player actions ----
        public const int PlayerStartPracticeMatch = 5110;
        public const int PlayerClearMatchPointer  = 5111;
        public const int PlayerApplyMatchResult   = 5112;
        public const int PlayerNoteMatchGone      = 5113;
        public const int PlayerDismissMatchGone   = 5114;
        public const int PlayerNoteDeckPlayed     = 5115;
        public const int PlayerEnqueueForRankedMatch = 5116;
        public const int PlayerCancelMatchmaking     = 5117;
        public const int PlayerDevWaiveNewcomerShield = 5118;
        // 5119 retired.

        // ---- rule actions: the rules themselves, executed by the actor and re-executed by every follower ----
        public const int MatchMulliganSubmit  = 5120;
        public const int MatchMulliganResolve = 5121;
        public const int MatchPlayCard        = 5122;
        public const int MatchAttack          = 5123;
        public const int MatchEndTurn         = 5124;
        public const int MatchEffectChoice    = 5125;
        public const int MatchExtendReserve   = 5127;

        // ---- addressed actions: delivered to one seat alone, never executed by the server ----
        public const int MatchOwnCardGained   = 5128;
        public const int MatchOwnPeekRevealed = 5129;
        public const int MatchOwnCardLost     = 5130;
        // 5131-5139 reserved (rule actions). The Heist pick needed none: it rides a seat intent and the phase
        // is the table's rather than the game's.
        // 5141-5149 reserved.
    }

    /// <summary>
    /// Game-specific <see cref="MetaActionResult"/> values. Descriptive names help diagnose failures in logs
    /// and the LiveOps Dashboard.
    /// </summary>
    public static class ActionResults
    {
        public static readonly MetaActionResult CardGiftNotReady = new MetaActionResult(nameof(CardGiftNotReady));
        public static readonly MetaActionResult InvalidDisplayName  = new MetaActionResult(nameof(InvalidDisplayName));

        public static readonly MetaActionResult InvalidDeckName     = new MetaActionResult(nameof(InvalidDeckName));
        public static readonly MetaActionResult UnknownDeck         = new MetaActionResult(nameof(UnknownDeck));
        public static readonly MetaActionResult TooManySavedDecks   = new MetaActionResult(nameof(TooManySavedDecks));

        /// <summary> A deck choice with neither half set, or both. No picker produces one. </summary>
        public static readonly MetaActionResult MalformedDeckChoice = new MetaActionResult(nameof(MalformedDeckChoice));
        /// <summary> The choice names a starter deck the config does not carry. </summary>
        public static readonly MetaActionResult UnknownStarterDeck  = new MetaActionResult(nameof(UnknownStarterDeck));

        // One result per way a deck list can be illegal, so a refusal names the rule it broke in the logs and
        // in the LiveOps Dashboard rather than arriving as a single opaque "illegal deck".
        public static readonly MetaActionResult DeckWrongSize       = new MetaActionResult(nameof(DeckWrongSize));
        public static readonly MetaActionResult DeckDuplicateCard   = new MetaActionResult(nameof(DeckDuplicateCard));
        public static readonly MetaActionResult DeckUnknownCard     = new MetaActionResult(nameof(DeckUnknownCard));
        public static readonly MetaActionResult DeckNotCollectible  = new MetaActionResult(nameof(DeckNotCollectible));
        public static readonly MetaActionResult DeckTooManyClans    = new MetaActionResult(nameof(DeckTooManyClans));
        public static readonly MetaActionResult DeckCardNotOwned    = new MetaActionResult(nameof(DeckCardNotOwned));
        /// <summary> A deck refused for a reason with no result of its own. See <see cref="ForDeckError"/>. </summary>
        public static readonly MetaActionResult IllegalDeck         = new MetaActionResult(nameof(IllegalDeck));

        public static readonly MetaActionResult InvalidLockSlot     = new MetaActionResult(nameof(InvalidLockSlot));
        public static readonly MetaActionResult CardNotOwned        = new MetaActionResult(nameof(CardNotOwned));
        /// <summary>
        /// The card sits below <c>Global.MinLockRank</c>, so there is nothing to freeze: the rank floor already
        /// protects it and locking it would only take it off a winner's Heist menu for free.
        /// </summary>
        public static readonly MetaActionResult CardRankTooLowToLock = new MetaActionResult(nameof(CardRankTooLowToLock));
        /// <summary> A rank outside <c>Global.RankMin</c>..<c>RankMax</c>. Development-only actions only. </summary>
        public static readonly MetaActionResult InvalidCardRank      = new MetaActionResult(nameof(InvalidCardRank));
        /// <summary> The card is locked, and a locked card can neither lose a rank nor gain one. </summary>
        public static readonly MetaActionResult CardIsLocked         = new MetaActionResult(nameof(CardIsLocked));

        /// <summary>
        /// One entry per player: pressing Play twice must seat the account once
        /// (<c>Docs/matchmaking.md</c>). The only in-match lockout — a deck or
        /// a lock edit is refused nothing, because a match's stakes were snapshotted at formation.
        /// </summary>
        public static readonly MetaActionResult AlreadyInMatch      = new MetaActionResult(nameof(AlreadyInMatch));

        /// <summary> A match action arrived with a payload it cannot apply. Never player-reachable. </summary>
        public static readonly MetaActionResult MalformedStep       = new MetaActionResult(nameof(MalformedStep));

        /// <summary> The refusal that names <paramref name="error"/>. </summary>
        public static MetaActionResult ForChoiceError(DeckChoiceError error)
        {
            switch (error)
            {
                case DeckChoiceError.Malformed:          return MalformedDeckChoice;
                case DeckChoiceError.UnknownStarterDeck: return UnknownStarterDeck;

                // A saved-deck id the account no longer holds is the refusal UnknownDeck already is, so it
                // keeps that name rather than gaining a second one for the same thing.
                default:                                 return UnknownDeck;
            }
        }

        /// <summary> The refusal that names <paramref name="error"/>. </summary>
        public static MetaActionResult ForDeckError(DeckValidationError error)
        {
            switch (error)
            {
                case DeckValidationError.WrongSize:      return DeckWrongSize;
                case DeckValidationError.DuplicateCard:  return DeckDuplicateCard;
                case DeckValidationError.UnknownCard:    return DeckUnknownCard;
                case DeckValidationError.NotCollectible: return DeckNotCollectible;
                case DeckValidationError.TooManyClans:   return DeckTooManyClans;
                case DeckValidationError.NotOwned:       return DeckCardNotOwned;

                // Never Success. This is only reached for a list the validator already refused, so a rule
                // added to DeckValidationError without a result here must still refuse — reporting a save
                // that did not happen is the one answer that cannot be recovered from.
                default:                                 return IllegalDeck;
            }
        }
    }

    /// <summary>
    /// Set the player's display name. The minimal end-to-end example of a player action: the client executes
    /// it optimistically and the server re-runs it authoritatively, and the validation lives in the shared
    /// code so both sides refuse the same inputs.
    /// <para>
    /// It is the client-side counterpart of the SDK's own <c>PlayerChangeName</c>, which the dashboard's
    /// change-name control posts server-side. Both write <see cref="PlayerModel.PlayerName"/> — there is one
    /// name — so a rename from either side reaches the menu, the match plaques, the dashboard's player list
    /// and the name search index alike.
    /// </para>
    /// <para>
    /// Both paths enforce the same rule: this action asks the SDK's <c>PlayerRequirementsValidator</c> (5 to 20
    /// characters, no control characters, no semicolon, overridable by the game), exactly as the dashboard's
    /// path does. The rule is asked, never copied.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerSetDisplayName)]
    public class PlayerSetDisplayName : PlayerAction
    {
        public string DisplayName { get; private set; }

        public PlayerSetDisplayName() { }
        public PlayerSetDisplayName(string displayName) { DisplayName = displayName; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            // Trimmed before it is judged, so both sides agree on the string that gets stored.
            string displayName = DisplayName?.Trim();
            if (string.IsNullOrEmpty(displayName) || !IntegrationRegistry.Get<PlayerRequirementsValidator>().ValidatePlayerName(displayName))
                return ActionResults.InvalidDisplayName;

            if (commit)
            {
                player.DisplayName = displayName;
                player.ClientListener.Root?.GenericPropertyChanged(nameof(PlayerModel.DisplayName));
                player.ServerListener.OnDisplayNameChanged(player.DisplayName);

                // The core listeners the SDK's own PlayerChangeName fires, because this action writes the same
                // member and an SDK integration watching for a rename must not care which action did it. They
                // are ordinary public properties on the model, never null (they default to the Empty
                // implementations), so a game action may raise them. Guild member data is not propagated the
                // way the core action does: this sample has no guilds.
                player.ServerListenerCore.OnPlayerNameChanged(displayName);
                player.ClientListenerCore.OnPlayerNameChanged(displayName);
            }

            return MetaActionResult.Success;
        }
    }
}
