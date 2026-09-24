using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Runtime.Serialization;

namespace Game.Logic
{
    /// <summary>
    /// State and per-player logic for a single player. This is the heart of a Metaplay game: it is kept in
    /// sync between client and server, persisted in the database, and evolved over time via
    /// <see cref="PlayerAction"/>s and per-tick logic in <see cref="GameTick"/>.
    /// <para>
    /// Everything an account owns lives here: which cards it holds and at what rank, its saved decks, which
    /// cards it has locked, and its record. All of it changes only through validated
    /// actions — the account's state is authoritative because it is valuable, the way the match's is
    /// authoritative because it is secret (<c>Docs/meta.md</c>).
    /// </para>
    /// </summary>
    [MetaSerializableDerived(1)]
    [SupportedSchemaVersions(1, 1)]
    // 110 was DisplayName (now an alias of PlayerName); 127 was LastPlayedDeckId (now LastPlayedDeck at 133).
    [MetaBlockedMembers(110, 127)]
    public class PlayerModel : PlayerModelBase<PlayerModel, PlayerStatisticsCore>
    {
        public const int TicksPerSecond = 10;
        protected override int GetTicksPerSecond() => TicksPerSecond;

        // External services, not serialized or PrettyPrinted.
        [IgnoreDataMember] public new SharedGameConfig       GameConfig     => GetGameConfig<SharedGameConfig>();
        [IgnoreDataMember] public IPlayerModelServerListener ServerListener { get; set; } = EmptyPlayerModelServerListener.Instance;
        [IgnoreDataMember] public IPlayerModelClientListener ClientListener { get; set; } = EmptyPlayerModelClientListener.Instance;

        // Player profile.
        [MetaMember(100)] public sealed override EntityId           PlayerId    { get; set; }
        [MetaMember(102)] public sealed override int                PlayerLevel { get; set; }

        /// <summary>
        /// The account's one name. This is the member the SDK itself reads, and there are three readers worth
        /// knowing about before writing anything else: the LiveOps Dashboard's player header and player list,
        /// the <c>PlayerNameSearches</c> index <c>PlayerActorBase</c> maintains at every persist (it compares
        /// this against the name it last indexed), and the dashboard's built-in change-name control, which
        /// posts the SDK's own <c>PlayerChangeName</c> action to write it.
        /// <para>
        /// <c>NoChecksum</c> is what lets two writers share it. <c>PlayerChangeName</c> is an
        /// <i>unsynchronized</i> server action: the server runs it first and the client at some later point, so
        /// the two timelines hold different strings in between. Outside the checksum, that is a label a moment
        /// stale rather than a desync — and the same exclusion is why <see cref="PlayerSetDisplayName"/> may
        /// predict its write on the client.
        /// </para>
        /// </summary>
        [MetaMember(101), NoChecksum] public sealed override string PlayerName  { get; set; }

        /// <summary> The name shown on the menu and on the match plaques: an alias of <see cref="PlayerName"/>. </summary>
        [IgnoreDataMember] public string DisplayName { get => PlayerName; set => PlayerName = value; }

        /// <summary>
        /// The bounds the menu's rename form reads, from the SDK's <see cref="PlayerRequirementsValidator"/> that
        /// <see cref="PlayerSetDisplayName"/> asks.
        /// </summary>
        public static int MinDisplayNameLength => IntegrationRegistry.Get<PlayerRequirementsValidator>().MinPlayerNameLength;
        public static int MaxDisplayNameLength => IntegrationRegistry.Get<PlayerRequirementsValidator>().MaxPlayerNameLength;

        // Collection, decks and locks.

        /// <summary>
        /// Every card this account holds, and at what rank. A key that is absent is a card the player does not
        /// own; a rank is always within <c>Global.RankMin</c>..<c>Global.RankMax</c>. Cards are never removed —
        /// the Heist moves ranks between accounts, never the card itself.
        /// </summary>
        [MetaMember(120)] public MetaDictionary<CardId, int> Collection { get; private set; } = new MetaDictionary<CardId, int>();


        /// <summary> Completed qualifying matches banked toward the next new-card gift. </summary>
        [MetaMember(134)] public int CardGiftProgress { get; set; }
        /// <summary> Monotonic gift ordinal; part of the deterministic random selection seed. </summary>
        [MetaMember(135)] public int CardGiftsClaimed { get; set; }
        /// <summary> The already-granted card awaiting acknowledgement in the metagame. </summary>
        [MetaMember(136)] public CardId UnseenCardGift { get; set; }

        /// <summary> Saved decks, keyed by the server-assigned id <see cref="NextDeckId"/> hands out. </summary>
        [MetaMember(122)] public MetaDictionary<int, PlayerDeck> Decks { get; private set; } = new MetaDictionary<int, PlayerDeck>();

        /// <summary> The next id a created deck takes. Deck ids are server-assigned, never client-chosen. </summary>
        [MetaMember(123)] public int NextDeckId { get; set; } = 1;

        /// <summary>
        /// Which cards are locked, by slot index. Sparse: an absent key is an empty slot, so no sentinel card
        /// id is needed to mean "nothing here". A locked card can neither lose a rank nor gain one.
        /// </summary>
        [MetaMember(124)] public MetaDictionary<int, CardId> LockSlots { get; private set; } = new MetaDictionary<int, CardId>();

        /// <summary>
        /// How many lock slots this account has. Seeded from <c>Global.InitialLockSlots</c> at creation and
        /// account state from then on, because game-design.md commits to it growing through account progression —
        /// which is then an increment rather than a schema change.
        /// </summary>
        [MetaMember(125)] public int LockSlotCount { get; set; }

        /// <summary> Wins, losses and matches played. Written by the match and the Heist. </summary>
        [MetaMember(126)] public PlayerRecord Record { get; private set; } = new PlayerRecord();

        /// <summary>
        /// The deck this account was last seated with, or null when it has not played a match. Home defaults
        /// its deck picker to it, and it is public and checksummed, so a reload finds the answer the server
        /// has. Either kind of deck: one of this account's saved decks, or one of the config-authored starter
        /// decks.
        /// <para>
        /// Written by <see cref="PlayerNoteDeckPlayed"/>, which the actor issues where the match pointer is
        /// assigned — never from the entry action's commit body, which also runs on the client, where the
        /// entry refusal is invisible because the pointer deciding it is <c>ServerOnly</c>.
        /// </para>
        /// <para>
        /// It is a remembered choice, not a guarantee: the deck may have been deleted since, or dropped from
        /// the config, so every reader falls back rather than trusting it.
        /// </para>
        /// </summary>
        [MetaMember(133)] public DeckChoice LastPlayedDeck { get; set; }

        /// <summary>
        /// Whether this account's own newcomer shield is waived. Frozen onto the matchmaking ticket at enqueue
        /// beside <c>PlayerRecord.RankedMatchesPlayed</c>, and it lifts only this account's shield: the other
        /// seat's still shields the match, because what one player stands to win is what the other stands to
        /// lose (<c>Docs/matchmaking.md</c>, "The queue service").
        /// <para>
        /// Public and checksummed rather than <c>ServerOnly</c>, so a screen can show it later. That means it
        /// is written only by <see cref="PlayerSetNewcomerShieldWaived"/>, a synchronized server action, and
        /// never from a body that also runs unsynchronized on the client.
        /// </para>
        /// </summary>
        [MetaMember(128)] public bool NewcomerShieldWaived { get; set; }

        // The match pointer. Both members are ServerOnly: the client is never told to go find a match, it is
        // told which one it is in, by the association arriving on its match slot.

        /// <summary>
        /// The match this account is in, or <see cref="EntityId.None"/>. It re-seats a reconnecting player and
        /// keeps them out of matchmaking while a result is outstanding, and the same acknowledgement that
        /// applies the result clears it — recording the game and releasing the player are one errand rather
        /// than two mechanisms that can disagree (<c>Docs/match.md</c>, "The match pointer").
        /// </summary>
        [MetaMember(130), ServerOnly] public EntityId CurrentMatch { get; set; } = EntityId.None;

        /// <summary>
        /// Every match whose outcome this account has already folded in. Durability lives on the match's own
        /// lifetime and idempotence lives here: the table retries a result until it is acknowledged, and a
        /// result that arrives twice moves nothing.
        /// <para>
        /// It grows without bound. A bounded set (the last N ids, or pruned by age) is the known fix: a match id
        /// cannot be re-delivered once its actor is gone, so an older entry guards nothing.
        /// </para>
        /// </summary>
        [MetaMember(131), ServerOnly] public OrderedSet<EntityId> AppliedMatchResults { get; private set; } = new OrderedSet<EntityId>();

        /// <summary>
        /// Set when a match this account was in stopped existing — a redeploy, an eviction, a lost node.
        /// <b>Public</b>, because <see cref="CurrentMatch"/> is <c>ServerOnly</c> and reads as none on the
        /// client whether or not it was ever set: the clear itself is invisible there, so without a public
        /// fact a player whose game vanished would simply find themselves on Home with no explanation.
        /// <para>
        /// Home is the only reader. It is cleared by the player pressing OK on the notice, and again whenever a
        /// new match starts — because it is a persisted member, and a player who pressed Practice instead of OK
        /// would otherwise be told two matches later that their last one ended early.
        /// </para>
        /// </summary>
        [MetaMember(132)] public bool MatchGoneUnseen { get; set; }

        /// <summary>
        /// Whether this account is already at a table. <b>On the client this reads false always</b>, because
        /// <see cref="CurrentMatch"/> is a server-only member and is default there — so the client's predicted
        /// run of <see cref="PlayerStartPracticeMatch"/> passes the check and the server's authoritative run
        /// is what refuses. That is the correct division: only the server knows.
        /// </summary>
        public bool IsInMatch => CurrentMatch != EntityId.None;

        /// <summary>
        /// Whether this card sits in one of the lock slots, and therefore can neither lose a rank nor gain
        /// one. Public state, which is what lets the Heist's transfer re-read it live from inside a
        /// synchronized action: the client's replay reads the same set the server did.
        /// <para>
        /// The map is keyed by slot rather than by card — an absent key is an empty slot, so nothing needs a
        /// sentinel card id — so the question is a walk. It is asked from three places and this is the one
        /// answer.
        /// </para>
        /// </summary>
        public bool IsLocked(CardId cardId)
        {
            if (cardId == null)
                return false;

            foreach ((int _, CardId locked) in LockSlots)
            {
                if (locked == cardId)
                    return true;
            }

            return false;
        }

        /// <summary> How many decks one account may keep saved. Enforced by the action, on both sides. </summary>
        public const int MaxSavedDecks = 12;

        /// <summary>
        /// Seeds a brand-new account. The SDK guarantees this runs exactly once per account, at creation, so
        /// the starter grant needs no "already granted" flag: "first login" and "this model did not exist a
        /// moment ago" are the same event, and every reconnect or resumed session finds a model that already
        /// exists and does not run this path.
        /// </summary>
        protected override void GameInitializeNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name)
        {
            PlayerId   = playerId;
            PlayerName = name;

            SharedGameConfig config = (SharedGameConfig)gameConfig;

            // The starter collection is content, not a code path: every card the catalogue flags is granted, at
            // the rank floor. The config build proves that slice can assemble a legal deck on day one.
            foreach ((CardId cardId, CardInfo card) in config.Cards)
            {
                if (card.InStarterCollection)
                    Collection[cardId] = config.Global.RankMin;
            }

            LockSlotCount = config.Global.InitialLockSlots;

            // The queue bands on this, so an account that started at zero would sit a whole rating band away
            // from every other new account. It is seeded here rather than defaulted on the member because the
            // seed is content: a game that retunes it retunes it for accounts made afterwards.
            Record.Rating = config.Global.InitialRating;
        }

        protected override void GameTick(IChecksumContext checksumCtx)
        {
        }
    }
}
