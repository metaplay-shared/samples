using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Game.Logic
{
    /// <summary>
    /// State and logic for a single player, kept in sync between client and server and persisted in the database.
    /// The card game itself runs on the match entity (<c>docs/match.md</c>). This model holds the player's profile,
    /// match record and history, the table they are seated at, and the meta features' state. No player state changes
    /// with time alone, so <see cref="GameTick"/> and <see cref="GameFastForwardTime"/> are empty.
    /// </summary>
    [MetaSerializableDerived(1)]
    [SupportedSchemaVersions(1, 5)]
    public class PlayerModel : PlayerModelBase<PlayerModel, PlayerStatisticsCore, OfferGroupsModel>
    {
        public const int TicksPerSecond = 10;
        protected override int GetTicksPerSecond() => TicksPerSecond;

        // External services, not serialized or PrettyPrinted.
        [IgnoreDataMember] public new SharedGameConfig       GameConfig => GetGameConfig<SharedGameConfig>();
        [IgnoreDataMember] public IPlayerModelServerListener ServerListener { get; set; } = EmptyPlayerModelServerListener.Instance;
        [IgnoreDataMember] public IPlayerModelClientListener ClientListener { get; set; } = EmptyPlayerModelClientListener.Instance;

        // Player profile.
        [MetaMember(100)] public sealed override EntityId           PlayerId    { get; set; }
        [MetaMember(101), NoChecksum] public sealed override string PlayerName  { get; set; }
        [MetaMember(102)] public sealed override int                PlayerLevel { get; set; }

        // MetaMember ids 110-112 are retired. Do not reuse them.

        /// <summary>
        /// The table this player is seated at, or <see cref="EntityId.None"/>. Every session start re-creates the
        /// association to the match client slot from this value, which re-seats a player after a page reload or a
        /// lost connection.
        /// <para>
        /// It is <c>ServerOnly</c>, so the player actor can write it outside an action. The client learns its
        /// table from the association, not from this field.
        /// </para>
        /// </summary>
        [MetaMember(120), ServerOnly] public EntityId CurrentMatchId { get; set; }

        /// <summary>
        /// Tables whose seat assignment arrived while this player was already seated elsewhere, so the player
        /// never took the seat (<see cref="MatchHistoryRules.NoteDeclined"/>). Their results are not recorded.
        /// <c>ServerOnly</c>, because the actor writes it outside an action.
        /// </summary>
        [MetaMember(137), ServerOnly] List<EntityId> _declinedMatches = new List<EntityId>();

        [IgnoreDataMember] public List<EntityId> DeclinedMatches => _declinedMatches ??= new List<EntityId>();

        /// <summary>
        /// The player's lifetime match counters, shown on the menu. It is excluded from the checksum because the
        /// unsynchronized <see cref="PlayerRecordMatchResult"/> writes it, which the server and the client execute at
        /// different ticks (<c>docs/player.md</c>, "Which members are excluded from the checksum"). The value still
        /// replicates to the client, and no client action writes it.
        /// </summary>
        [MetaMember(121), NoChecksum] public PlayerRecord Record { get; private set; } = new PlayerRecord();

        /// <summary>
        /// The last <see cref="MatchHistoryRules.HistoryCapacity"/> finished games, oldest first. The oldest entry is
        /// removed when a new one is added at capacity. Excluded from the checksum for the same reason as
        /// <see cref="Record"/>.
        /// </summary>
        [MetaMember(122), NoChecksum] List<MatchHistoryEntry> _matchHistory = new List<MatchHistoryEntry>();

        /// <summary>
        /// When this player last renamed, or <see cref="MetaTime.Epoch"/> if never. The rename cooldown is
        /// measured from it. Excluded from the checksum because the unsynchronized <see cref="PlayerRenamed"/>
        /// writes it, as it does <see cref="PlayerModelBase{TModel, TStatistics}.PlayerName"/>.
        /// </summary>
        [MetaMember(123), NoChecksum] public MetaTime LastRenamedAt { get; private set; }

        /// <summary>
        /// The currency balances (<c>docs/economy.md</c>). Private and replaced as a whole, because only
        /// <see cref="ApplyWallet(WalletTransaction, bool, AnalyticsCorrelationId)"/> may write it.
        /// <para>
        /// It is checksummed, because every action that changes it runs at the same timeline position on the
        /// client and the server, so any difference in balance is a real bug.
        /// </para>
        /// </summary>
        [MetaMember(124)] PlayerWalletModel _wallet = new PlayerWalletModel();

        /// <summary>
        /// Whether this player has ever renamed themselves instead of keeping the generated name. Segments can
        /// target it through <see cref="PlayerPropertyHasCustomizedName"/>. A LiveOps Dashboard rename does not set
        /// it, because the SDK's admin path writes <c>PlayerName</c> without calling <see cref="ApplyRename"/>.
        /// Excluded from the checksum because the unsynchronized <see cref="PlayerRenamed"/> writes it.
        /// </summary>
        [MetaMember(125), NoChecksum] public bool HasCustomizedName { get; private set; }

        /// <summary>
        /// The number of this player's accepted renames. Only <see cref="ApplyRename"/> writes it, called by
        /// <see cref="PlayerRenamed"/> on its commit pass, so refused and unchanged requests do not change it.
        /// </summary>
        [MetaMember(126), NoChecksum] public int NameChangeCount { get; private set; }

        /// <summary>
        /// The demo products this player has been granted, which limits each demo product to one purchase per player
        /// (<c>docs/offers.md</c>). It is checksummed, like the wallet, because the client action
        /// <c>PlayerClaimPendingInAppPurchase</c> writes it on both sides and it decides whether the wallet changes.
        /// Receipt replay is refused earlier, by the SDK.
        /// </summary>
        [MetaMember(127)] List<InAppProductId> _demoPurchases = new List<InAppProductId>();

        /// <summary>
        /// This player's seasonal tournament state: attempts, points, claims and earned cosmetics
        /// (<c>docs/seasonal-tournament.md</c>).
        /// <para>
        /// It must be excluded from the checksum, because it is a match-completion observer and is written from
        /// the unsynchronized <see cref="PlayerRecordMatchResult"/> (<see cref="IMatchCompletionObserver"/>,
        /// rule 1). Claims that change the checksummed wallet run as a
        /// <see cref="PlayerSynchronizedServerAction"/>, which executes after the progress it reads.
        /// </para>
        /// </summary>
        [MetaMember(128), NoChecksum] PlayerTournamentState _tournament = new PlayerTournamentState();

        /// <summary>
        /// The daily and weekly missions (<c>docs/missions.md</c>). A match-completion observer, so it follows
        /// the rules in <see cref="IMatchCompletionObserver"/>: excluded from the checksum, written by the
        /// match-completion action and the player's claim, and never granting rewards itself.
        /// <para>
        /// The getter replaces null with an empty state, because an account persisted without this member
        /// deserializes it as null. This avoids a schema migration.
        /// </para>
        /// </summary>
        [MetaMember(130), NoChecksum] PlayerMissionState _missions = new PlayerMissionState();

        public PlayerMissionState Missions => _missions ??= new PlayerMissionState();

        /// <summary>
        /// The daily reward streak state (<c>docs/daily-rewards.md</c>). Private, because only
        /// <see cref="ApplyDailyRewardClaim"/> may write it.
        /// <para>
        /// It is checksummed, like the wallet it changes with. Both are written by a synchronized server action,
        /// which the client and the server execute at the same timeline position.
        /// </para>
        /// </summary>
        [MetaMember(129)] DailyRewardState _dailyReward = new DailyRewardState();

        /// <summary>
        /// This player's first-week event state (<c>docs/first-week-event.md</c>). A match-completion observer,
        /// so it follows the rules in <see cref="IMatchCompletionObserver"/>, like <see cref="Missions"/>.
        /// <para>
        /// The getter replaces null with an empty state, because an account persisted without this member
        /// deserializes it as null. <see cref="MigrateStartFirstWeek"/> then starts the event on that object.
        /// </para>
        /// </summary>
        [MetaMember(131), NoChecksum] PlayerFirstWeekState _firstWeek = new PlayerFirstWeekState();

        /// <summary>
        /// The first-week event state. Only the match-completion observer and
        /// <see cref="PlayerClaimFirstWeekReward"/> write it.
        /// </summary>
        [IgnoreDataMember] public PlayerFirstWeekState FirstWeek => _firstWeek ??= new PlayerFirstWeekState();

        /// <summary>
        /// The spin wheel's counters and last result (<c>docs/spin-wheel.md</c>). Private, because only
        /// <see cref="ApplySpinResult"/> and <see cref="AcknowledgeSpinResult"/> may write it. It is checksummed, like
        /// the wallet it changes with: a synchronized server action resolves a spin, and a later client action
        /// acknowledges it. The getter replaces null with an empty state, which equals a player who has never spun.
        /// </summary>
        [MetaMember(132)] SpinWheelState _spinWheel = new SpinWheelState();

        /// <summary>
        /// Whether this player allows segment-based offer targeting (<c>docs/offers.md</c>). Defaults to
        /// <c>true</c>, which is also the value for an account persisted without this member.
        /// <para>
        /// Only <see cref="SetPersonalizedOffersEnabled"/> writes it, from a client action. It is checksummed
        /// because segment conditions read it (<see cref="PlayerPropertyPersonalizedOffersEnabled"/>), and the
        /// client and the server must agree on which offers a player sees.
        /// </para>
        /// </summary>
        [MetaMember(133)] bool _personalizedOffersEnabled = true;

        /// <summary>
        /// This player's progress in the weekly themed events they hold (<c>docs/weekly-event.md</c>). A
        /// match-completion observer, so it follows the rules in <see cref="IMatchCompletionObserver"/>. The progress
        /// cannot be stored under the checksummed <see cref="PlayerModelBase.LiveOpsEvents"/>, so the SDK's event
        /// model holds only the schedule, phase, audience and content, and <see cref="WeeklyEventPlayerModel"/> has no
        /// state. The getter replaces null with an empty state, like <see cref="Missions"/>.
        /// </summary>
        [MetaMember(134), NoChecksum] PlayerWeeklyEventState _weeklyEvent = new PlayerWeeklyEventState();

        /// <summary>
        /// The weekly themed events' progress. Only the match-completion observer,
        /// <see cref="PlayerClaimWeeklyEventReward"/> and the handling of the SDK's phase change when a week
        /// concludes write it.
        /// </summary>
        [IgnoreDataMember] public PlayerWeeklyEventState WeeklyEvent => _weeklyEvent ??= new PlayerWeeklyEventState();

        /// <summary>
        /// The cosmetics this player owns, has equipped, and has not yet seen (<c>docs/cosmetics.md</c>). Private,
        /// so only the writers named on <see cref="Cosmetics"/> change it. It is checksummed, like the wallet it
        /// changes with, because every writer is a client action or a synchronized server action. The getter replaces
        /// null with an empty state, like <see cref="Missions"/>. Cosmetics recorded elsewhere need a migration, such
        /// as <see cref="MigrateEarnedCosmeticsIntoWardrobe"/>.
        /// </summary>
        [MetaMember(135)] PlayerCosmeticsState _cosmetics = new PlayerCosmeticsState();

        /// <summary>
        /// The checksummed copy of the profile values that segments and offer conditions read
        /// (<see cref="PlayerTargetingFacts"/>). Only <see cref="SettleTargetingFacts"/> writes it, called from
        /// the synchronized <see cref="PlayerTargetingFactsSynced"/> and from
        /// <see cref="MigrateSettleTargetingFacts"/>.
        /// <para>
        /// The getter replaces null with an empty state, like <see cref="Missions"/>. An empty state is correct
        /// only for an account that has played no game and never renamed, so the migration fills in the others.
        /// </para>
        /// </summary>
        [MetaMember(136)] PlayerTargetingFacts _targetingFacts = new PlayerTargetingFacts();

        [IgnoreDataMember] public PlayerTargetingFacts TargetingFacts => _targetingFacts ??= new PlayerTargetingFacts();

        /// <summary>
        /// Initializes a new player: generated name, starting wallet, first-week event and default cosmetics.
        /// <para>
        /// The name is generated here rather than in the server actor, because the actor cannot read the
        /// game-specific config at this point. <paramref name="name"/> is the actor's
        /// <see cref="DisplayNameGenerator.FallbackName"/>, passed on as the fallback (<c>docs/player.md</c>,
        /// "Generated names").
        /// </para>
        /// </summary>
        protected override void GameInitializeNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name)
        {
            SharedGameConfig config = (SharedGameConfig)gameConfig;

            PlayerId   = playerId;
            PlayerName = DisplayNameGenerator.Generate(config, playerId, name).Name;
            _wallet    = PlayerWalletModel.Starting(config.Global);

            // Start the first-week event now, with the currently active schedule. Starting at creation rather
            // than at the first session makes day one start when the account was created, and pins the schedule
            // so a later config publish does not change it for this player.
            FirstWeek.Start(config, now);

            // A new account owns and wears the default cosmetics instead of having empty slots. A slot can only
            // be changed to an owned item, so owning the defaults lets the player return to them
            // (docs/cosmetics.md, "The starting three").
            GrantDefaultCosmetics();
        }

        /// <summary>
        /// Schema v1 to v2: starts the first-week event for an account that has no first-week state
        /// (<c>docs/first-week-event.md</c>). The SDK sets the model's game config and clock before it runs
        /// migrations, on every path that runs them. The start is the migration time, not the account creation time,
        /// so the player gets a full first day. If the config has no active schedule, the match-completion observer
        /// starts the event on the player's next finished game.
        /// </summary>
        [MigrationFromVersion(1)]
        void MigrateStartFirstWeek()
        {
            FirstWeek.Start(GameConfig, CurrentTime);
        }

        /// <summary>
        /// Schema v2 to v3: adds every cosmetic in <see cref="PlayerTournamentState.EarnedCosmetics"/> to
        /// <see cref="Cosmetics"/>, which is the only record of cosmetic ownership (<c>docs/cosmetics.md</c>).
        /// <see cref="PlayerTournamentState.EarnedCosmetics"/> stays as the tournament's record of its rewards.
        /// <para>
        /// A newly added cosmetic is not equipped, because only the player changes what they wear. It is marked
        /// unacknowledged, which shows the new-item badge on Profile.
        /// </para>
        /// </summary>
        [MigrationFromVersion(2)]
        void MigrateEarnedCosmeticsIntoWardrobe()
        {
            foreach (CosmeticId earned in Tournament.EarnedCosmetics)
            {
                if (Cosmetics.Acquire(earned))
                    Cosmetics.MarkUnacknowledged(earned);
            }
        }

        /// <summary>
        /// Schema v3 to v4: grants the default cosmetics (<see cref="GrantDefaultCosmetics"/>) to an account that does
        /// not own them, so a player who equipped another item can switch back (<c>docs/cosmetics.md</c>, "The
        /// starting three"). An empty slot is filled with the default, which the client already draws for an empty
        /// slot. An occupied slot is not changed, because a migration must not change what the player wears. Nothing
        /// is marked unacknowledged, because the player already had this look.
        /// </summary>
        [MigrationFromVersion(3)]
        void MigrateGrantDefaultCosmetics()
        {
            GrantDefaultCosmetics();
        }

        /// <summary>
        /// Schema v4 to v5: fills in <see cref="TargetingFacts"/> from the player's record and rename state.
        /// Without it, an account persisted without the member would match no segment that counts games until
        /// its next finished game.
        /// </summary>
        [MigrationFromVersion(4)]
        void MigrateSettleTargetingFacts()
        {
            SettleTargetingFacts(PlayerTargetingFacts.Of(this));
        }

        /// <summary>
        /// Grants every cosmetic in <see cref="CosmeticDefaults.All"/> and equips each one whose slot is empty.
        /// Used for a new account and by <see cref="MigrateGrantDefaultCosmetics"/> (<c>docs/cosmetics.md</c>,
        /// "The starting three").
        /// <para>
        /// A default missing from the config's catalogue is skipped. The config build requires every default
        /// (<see cref="GameConfigValidation.ValidateCosmetics"/>), so this happens only with an older archive,
        /// and the client draws the default look for the empty slot.
        /// </para>
        /// </summary>
        void GrantDefaultCosmetics()
        {
            foreach (CosmeticId id in CosmeticDefaults.All)
            {
                CosmeticInfo info = CosmeticOf(id);
                if (info == null)
                    continue;

                Cosmetics.Acquire(id);

                if (Cosmetics.EquippedIn(info.Kind) == null)
                    Cosmetics.Equip(info.Kind, id);
            }
        }

        /// <summary>
        /// Emits the analytics events for the new account's starting wallet and generated name.
        /// <para>
        /// Both are set when the model is created, before any analytics handler is attached, so the events are
        /// emitted at the first login instead. Without them the event log would have no record of where the
        /// starting balance and name came from.
        /// </para>
        /// </summary>
        protected override void GameOnInitialLogin()
        {
            // Built from the current wallet and emitted through the same method as every later grant and spend.
            EmitWalletRows(
                Wallet.AsOpeningSettlement(),
                AnalyticsCorrelationId.Create(PlayerId, CurrentTime, "starting_wallet"));

            // Generate the name again instead of storing the generation result in the model. Generation is
            // deterministic from the player id and the vocabulary, so it gives the same result. The length is
            // measured on the stored name.
            //
            // If a config publish lands between creation and this login, the event reports the new vocabulary
            // version. The two moments are milliseconds apart and the version is only an analytics label, so
            // this is accepted.
            GeneratedDisplayName generated = DisplayNameGenerator.Generate(
                GameConfig, PlayerId, DisplayNameGenerator.FallbackName(PlayerId));

            EventStream.Event(new PlayerEventIdentityInitialized(
                generated.GeneratorVersion,
                DisplayNamePolicy.CountCharacters(PlayerName),
                generated.UsedFallback,
                BuildPublicIdentity().AvatarId));

            // The SDK's "Initialized new player" log line shows the actor's fallback name, which the generated
            // name replaced. Log the real name so an operator can find the player by name in the server log.
            Log.Info("Named {PlayerName} from vocabulary v{GeneratorVersion}{Fallback}.",
                PlayerName, generated.GeneratorVersion, generated.UsedFallback ? " (fallback)" : "");
        }

        /// <summary>Nothing on this model advances with time, so the tick does nothing.</summary>
        protected override void GameTick(IChecksumContext checksumCtx)
        {
        }

        /// <summary>Nothing on this model advances with time, so there is nothing to fast-forward.</summary>
        protected override void GameFastForwardTime(MetaDuration elapsedTime)
        {
        }

        #region The profile

        /// <summary>The finished games on the record, oldest first.</summary>
        public IReadOnlyList<MatchHistoryEntry> MatchHistory => _matchHistory ?? EmptyHistory;

        static readonly List<MatchHistoryEntry> EmptyHistory = new List<MatchHistoryEntry>();

        /// <summary>Whether this match is already on the record. See <see cref="MatchHistoryRules.HasRecorded"/>.</summary>
        public bool HasRecordedMatch(EntityId matchId) => MatchHistoryRules.HasRecorded(MatchHistory, matchId);

        /// <summary>
        /// Records one finished game: adds it to the history, updates <see cref="Record"/>, and then passes a
        /// <see cref="MatchCompletion"/> to the match-completion observers. Only <see cref="PlayerRecordMatchResult"/>
        /// calls it. It refuses a match already in the history before any observer runs, so the once-per-match rule
        /// does not depend on the caller (<c>docs/player.md</c>, "The match-completion fact").
        /// </summary>
        /// <returns>Whether the game was recorded. False means it already was.</returns>
        internal bool TryRecordMatch(MatchHistoryEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (HasRecordedMatch(entry.MatchId))
                return false;

            // An account persisted without these members deserializes them as null. Creating them on first use
            // avoids a schema migration.
            _matchHistory ??= new List<MatchHistoryEntry>();
            Record        ??= new PlayerRecord();

            MatchHistoryRules.Append(_matchHistory, entry);
            Record.Add(entry);

            ObserveMatchCompletion(entry);
            return true;
        }

        /// <summary>
        /// Adds every match-completion observer to <paramref name="observers"/>. This is the only place observers
        /// are registered.
        /// <para>
        /// To register a feature, add a line for its state member, which must implement
        /// <see cref="IMatchCompletionObserver"/> and be <c>[NoChecksum]</c>. <c>MatchCompletionTests</c> fails
        /// for a member that implements the interface but is not registered here, and for a registered member
        /// that is checksummed.
        /// </para>
        /// </summary>
        internal void CollectMatchCompletionObservers(List<IMatchCompletionObserver> observers)
        {
            observers.Add(Missions);
            observers.Add(Tournament);
            observers.Add(FirstWeek);
            observers.Add(WeeklyEvent);

            if (_testMatchCompletionObservers != null)
                observers.AddRange(_testMatchCompletionObservers);
        }

        /// <summary>
        /// Test observers, added by the shared-code tests to check match-completion dispatch through the same
        /// path as the real observers.
        /// <para>
        /// This hook is in the model because tests cannot subclass it: the serializer refuses a concrete type
        /// derived from a concrete <c>[MetaSerializable]</c> type, and the SDK constructs the registered model
        /// type. It is internal and not serialized, so only <c>SharedCode.Tests</c> can reach it and it is never
        /// persisted (see <c>SharedCode/AssemblyInfo.cs</c>).
        /// </para>
        /// </summary>
        [IgnoreDataMember] internal List<IMatchCompletionObserver> _testMatchCompletionObservers;

        /// <summary>
        /// Calls every match-completion observer, catching and logging each observer's exceptions separately.
        /// <para>
        /// This runs inside the request that acknowledges the table's result. An escaping exception would leave
        /// the result unacknowledged, so the table would keep re-sending it and the player's
        /// <see cref="CurrentMatchId"/> would never be cleared, which blocks matchmaking for the player.
        /// </para>
        /// </summary>
        void ObserveMatchCompletion(MatchHistoryEntry entry)
        {
            List<IMatchCompletionObserver> observers = new List<IMatchCompletionObserver>();
            CollectMatchCompletionObservers(observers);
            if (observers.Count == 0)
                return;

            // Built from the appended entry, so the completion and the record describe the same game.
            MatchCompletionContext context = new MatchCompletionContext(
                this,
                new MatchCompletion(entry.MatchId, entry.EndedAt, entry.Position, entry.TricksWon));

            foreach (IMatchCompletionObserver observer in observers)
            {
                if (observer == null)
                    continue;

                try
                {
                    observer.OnMatchCompleted(context);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Match-completion observer {Observer} threw on table {MatchId}. The game stays on the record; that feature's state is unchanged.",
                        observer.GetType().Name, entry.MatchId);
                }
            }
        }

        /// <summary>
        /// Applies an accepted rename: sets the name, <see cref="LastRenamedAt"/>, <see cref="HasCustomizedName"/>
        /// and <see cref="NameChangeCount"/>, and notifies both listeners. Only <see cref="PlayerRenamed"/> calls
        /// it, on its commit pass.
        /// <para>
        /// All state changes of a rename are in this method, so <see cref="NameChangeCount"/> changes only for an
        /// accepted rename.
        /// </para>
        /// </summary>
        public void ApplyRename(string name, MetaTime renamedAt)
        {
            PlayerName        = name;
            LastRenamedAt     = renamedAt;
            HasCustomizedName = true;
            NameChangeCount  += 1;

            // Notify the listeners here rather than in the action, so any caller that changes the name also
            // updates the client screens and the server's public identity snapshots.
            ClientListener.OnNameChanged(name);
            ServerListener.OnPublicIdentityChanged();
        }

        /// <summary>Replaces <see cref="TargetingFacts"/>. See <see cref="PlayerTargetingFacts"/>.</summary>
        public void SettleTargetingFacts(PlayerTargetingFacts facts)
        {
            _targetingFacts = facts;
        }

        /// <summary>
        /// This player's identity as other players see it. This is the only place a
        /// <see cref="PlayerPublicIdentity"/> is built (see that type for why).
        /// <para>
        /// A cosmetic slot is null when nothing is equipped in it. The client then draws its built-in default
        /// look, not a catalogue item.
        /// </para>
        /// </summary>
        public PlayerPublicIdentity BuildPublicIdentity() =>
            new PlayerPublicIdentity(
                PlayerId,
                PlayerName,
                Cosmetics.EquippedIn(CosmeticKind.Avatar),
                Cosmetics.EquippedIn(CosmeticKind.Frame),
                Cosmetics.EquippedIn(CosmeticKind.NameEffect));

        #endregion

        #region The wallet

        /// <summary>
        /// The player's currency balances. Only <see cref="ApplyWallet(WalletTransaction, bool, AnalyticsCorrelationId)"/>
        /// changes them.
        /// </summary>
        [IgnoreDataMember] public PlayerWalletModel Wallet => _wallet;

        /// <summary>
        /// Computes the result of <paramref name="transaction"/> on this player's wallet without applying it.
        /// <para>
        /// For display only, for example to show a shortfall. A settlement cannot be applied, so actions call
        /// <see cref="ApplyWallet(WalletTransaction, bool, AnalyticsCorrelationId)"/> instead.
        /// </para>
        /// </summary>
        public WalletSettlement PreviewWallet(WalletTransaction transaction) =>
            Wallet.Settle(transaction, WalletCaps.From(GameConfig.Global));

        /// <summary>
        /// The only method that changes a balance. Settles <paramref name="transaction"/> and, on the commit pass,
        /// either applies it or records the refusal. Returns the result the action should return.
        /// <para>
        /// An action calls it on both passes before writing anything else, and returns its result unless it is
        /// <see cref="MetaActionResult.Success"/>, so no refusal goes unlogged and the settlement is never stale. It
        /// emits the <c>economy_transaction</c> events. The action emits its own event with the same
        /// <paramref name="correlation"/> (<c>docs/analytics.md</c>). Only an insufficient-funds refusal is logged
        /// in the player's event log, because other refusals are code or config bugs.
        /// </para>
        /// </summary>
        public MetaActionResult ApplyWallet(WalletTransaction transaction, bool commit, AnalyticsCorrelationId correlation) =>
            ApplyWallet(transaction, commit, correlation, out WalletSettlement _);

        /// <summary>
        /// <inheritdoc cref="ApplyWallet(WalletTransaction, bool, AnalyticsCorrelationId)"/>
        /// </summary>
        /// <param name="settlement">
        /// The settlement, whether or not it was applied. The client uses its rows for the reward reveal, or its
        /// shortfall for the insufficient-funds message.
        /// </param>
        public MetaActionResult ApplyWallet(WalletTransaction transaction, bool commit, AnalyticsCorrelationId correlation, out WalletSettlement settlement)
        {
            settlement = PreviewWallet(transaction);

            if (!commit)
                return settlement.ActionResult;

            if (!settlement.IsSuccess)
            {
                if (settlement.IsPlayerFacingRefusal)
                {
                    // A player-facing refusal is always an unaffordable spend, so the event uses the spend
                    // reason.
                    EventStream.Event(new PlayerEventEconomySpendRejected(
                        settlement.RefusedCurrency, settlement.RefusedAmount, settlement.RefusedBalance,
                        transaction.SpendReason, transaction.Feature, transaction.ContentId, correlation));
                }

                return settlement.ActionResult;
            }

            if (settlement.MovesBalance)
            {
                _wallet = settlement.WalletAfter;
                EmitWalletRows(settlement, correlation);
                ClientListener.OnWalletChanged();
            }

            return MetaActionResult.Success;
        }

        /// <summary>
        /// Emits one <c>economy_transaction</c> event per currency the settlement changed. This is the only place
        /// the event is created, so the starting wallet and every later grant and spend produce the same events.
        /// <para>
        /// A sink row uses the transaction's spend reason and a source row uses its grant reason. For most
        /// transactions the two are the same. A wheel spin uses different ones: the token cost is
        /// <c>wheel_spin_cost</c> and the prize is <c>wheel_prize</c>.
        /// </para>
        /// </summary>
        void EmitWalletRows(WalletSettlement settlement, AnalyticsCorrelationId correlation)
        {
            foreach (WalletTransactionRow row in settlement.Rows)
            {
                EconomyReason reason = row.Flow == CurrencyFlow.Sink
                    ? settlement.Transaction.SpendReason
                    : settlement.Transaction.Reason;

                EventStream.Event(new PlayerEventEconomyTransaction(
                    row.Currency, row.Flow, row.Amount, row.BalanceBefore, row.BalanceAfter,
                    reason, settlement.Transaction.Feature, settlement.Transaction.ContentId, correlation));
            }
        }

        #endregion

        #region Demo purchases

        static readonly List<InAppProductId> NoPurchases = new List<InAppProductId>();

        /// <summary>The demo products this player has been granted.</summary>
        [IgnoreDataMember] public IReadOnlyList<InAppProductId> DemoPurchases => _demoPurchases ?? NoPurchases;

        /// <summary>Whether this player has already been granted <paramref name="productId"/>. Each demo bundle can be bought once.</summary>
        public bool HasPurchased(InAppProductId productId) => productId != null && DemoPurchases.Contains(productId);

        /// <summary>
        /// Grants a purchase the server has validated. Called only by the SDK, from
        /// <c>PlayerClaimPendingInAppPurchase</c> (<c>docs/offers.md</c>). The contents go through
        /// <see cref="ApplyWallet(WalletTransaction, bool, AnalyticsCorrelationId, out WalletSettlement)"/>,
        /// so the grant emits the usual <c>economy_transaction</c> events. If the grant is refused, the wallet is
        /// unchanged and the purchase still completes, because throwing here would leave the validated purchase
        /// pending permanently.
        /// </summary>
        public override void OnClaimedInAppProduct(InAppPurchaseEvent ev, InAppProductInfoBase productInfoBase, out ResolvedPurchaseContentBase resolvedContent)
        {
            resolvedContent = null;

            if (productInfoBase is not DemoInAppProductInfo product)
            {
                Log.Warning("Purchase {TransactionId} is of product {ProductId}, which is not a demo bundle. Nothing granted.",
                    ev?.TransactionId, productInfoBase?.ProductId);
                return;
            }

            // For a product sold through a demo-priced OfferInfo, the SDK's dynamic-content claim path grants
            // the offer's Rewards just before this runs (DemoOfferReward.Consume), and the SDK's offer state
            // enforces the offer's MaxPurchasesPerPlayer. Nothing is left to do here.
            if (product.HasDynamicContent)
                return;

            if (HasPurchased(product.ProductId))
            {
                Log.Warning("Purchase {TransactionId} is a repeat of {ProductId}, which sells once per player. Nothing granted.",
                    ev?.TransactionId, product.ProductId);
                return;
            }

            // Correlate by transaction id, so the wallet events, the SDK's purchase event and the Dashboard
            // receipt all refer to the same purchase.
            AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(PlayerId, CurrentTime, ev.TransactionId);

            MetaActionResult result = ApplyWallet(
                WalletTransaction.PaidGrant(EconomyFeature.Iap, EconomyReason.IapGrant, product.Contents, EconomyContentId.FromString(product.ProductId.Value)),
                commit: true,
                correlation);

            if (result != MetaActionResult.Success)
            {
                Log.Error("Purchase {TransactionId} of {ProductId} could not be granted: {Result}", ev.TransactionId, product.ProductId, result);
                return;
            }

            _demoPurchases ??= new List<InAppProductId>();
            _demoPurchases.Add(product.ProductId);

            // Stored on the purchase record for customer support. The client learns of the grant from the SDK's
            // InAppPurchaseClaimed callback, so no game listener is called.
            resolvedContent = new ResolvedWalletBundle(product.Contents);
        }

        #endregion

        #region Segmented, wallet-priced offers

        /// <summary>
        /// Whether this player allows segment-based offer targeting. Every published segment requires it, so
        /// when it is false the player matches no segment and only untargeted offer groups can activate
        /// (<c>docs/offers.md</c>).
        /// </summary>
        [IgnoreDataMember] public bool PersonalizedOffersEnabled => _personalizedOffersEnabled;

        /// <summary>
        /// Sets <see cref="PersonalizedOffersEnabled"/>. Only <see cref="PlayerSetPersonalizedOffersEnabled"/>
        /// calls it.
        /// <para>
        /// It does not cancel a purchase in progress or change a balance. It only changes which offer groups the
        /// next <see cref="IPlayerModelBase.RefreshMetaOffers"/> can activate (<c>docs/offers.md</c>).
        /// </para>
        /// </summary>
        public void SetPersonalizedOffersEnabled(bool enabled)
        {
            if (_personalizedOffersEnabled == enabled)
                return;

            _personalizedOffersEnabled = enabled;

            EventStream.Event(new PlayerEventPersonalizedOffersPreferenceChanged(enabled));
            ClientListener.OnOffersChanged();
        }

        #endregion

        #region The seasonal tournament

        /// <summary>
        /// This player's tournament state. Only the synchronized server actions in
        /// <c>SharedCode/Tournament/TournamentActions.cs</c> and the match-completion observer write it.
        /// </summary>
        [IgnoreDataMember] public PlayerTournamentState Tournament => _tournament ??= new PlayerTournamentState();

        /// <summary>
        /// The SDK-owned league state for this player: the current group and every finished season. Null until
        /// the league integration initializes it, which the player actor does on every start.
        /// </summary>
        [IgnoreDataMember] public TournamentClientState TournamentDivision =>
            PlayerSubClientStates != null && PlayerSubClientStates.TryGetValue(ClientSlotGame.Tournament, out PlayerSubClientStateBase state)
                ? state as TournamentClientState
                : null;

        /// <summary>Every concluded season this player took part in, oldest first.</summary>
        public IReadOnlyList<TournamentHistoryEntry> TournamentResults =>
            TournamentDivision?.HistoricalDivisions ?? EmptyTournamentResults;

        static readonly List<TournamentHistoryEntry> EmptyTournamentResults = new List<TournamentHistoryEntry>();

        /// <summary>The concluded season for group <paramref name="divisionId"/>, or null if there is none.</summary>
        public TournamentHistoryEntry TournamentResultOf(EntityId divisionId)
        {
            foreach (TournamentHistoryEntry entry in TournamentResults)
            {
                if (entry.DivisionId == divisionId)
                    return entry;
            }
            return null;
        }

        /// <summary>
        /// The oldest concluded season with an unclaimed placement reward, or null. New seasons are appended to
        /// the history and never remove old ones, so a season stays pending until its reward is claimed.
        /// </summary>
        public TournamentHistoryEntry PendingTournamentReward()
        {
            foreach (TournamentHistoryEntry entry in TournamentResults)
            {
                if (entry.HasReward && !Tournament.HasClaimedPlacement(entry.DivisionId))
                    return entry;
            }
            return null;
        }

        /// <summary>
        /// The reward table <paramref name="id"/>, which the season was joined under, or null if it is no longer in
        /// the config. For a null <paramref name="id"/> (never joined), returns the active table for a preview.
        /// A missing id does not fall back to the active table, because milestones are claimed by index and another
        /// table would map a claimed index to a different milestone. A claim against a missing table is refused.
        /// </summary>
        public TournamentRewardTableInfo TournamentRewardTable(TournamentRewardTableId id)
        {
            if (id == null)
                return GameConfig.Global?.ActiveTournamentRewardTable?.Ref;

            return GameConfig.TournamentRewards.TryGetValue(id, out TournamentRewardTableInfo table) ? table : null;
        }

        /// <summary>The reward table of the player's current season. See <see cref="TournamentRewardTable"/>.</summary>
        public TournamentRewardTableInfo JoinedSeasonRewardTable => TournamentRewardTable(Tournament.RewardTable);

        /// <summary>
        /// Joins the player to a season. Only <see cref="PlayerTournamentJoined"/> calls it, on its commit pass.
        /// </summary>
        public void JoinTournament(int season, EntityId divisionId, MetaTime seasonEndsAt, TournamentRewardTableId rewardTable)
        {
            Tournament.Join(season, divisionId, seasonEndsAt, rewardTable, CurrentTime);
        }

        /// <summary>
        /// Grants the participation milestone at <paramref name="index"/>. Calls
        /// <see cref="ApplyWallet(WalletTransaction, bool, AnalyticsCorrelationId)"/> on both passes, and the
        /// milestone event uses the same correlation id as the currency events.
        /// </summary>
        public MetaActionResult ClaimTournamentMilestone(int index, bool commit)
        {
            TournamentRewardTableInfo table      = JoinedSeasonRewardTable;
            MetaActionResult          claimCheck = Tournament.CanClaimMilestone(table, index);
            if (claimCheck != MetaActionResult.Success)
                return claimCheck;

            TournamentMilestoneInfo milestone   = table.MilestoneAt(index);
            AnalyticsCorrelationId  correlation = AnalyticsCorrelationId.Create(PlayerId, CurrentTime, "tournament_milestone");

            MetaActionResult walletResult = ApplyWallet(
                WalletTransaction.Grant(
                    EconomyFeature.Tournament,
                    EconomyReason.TournamentReward,
                    milestone.Reward,
                    EconomyContentId.FromString(table.Id.Value)),
                commit,
                correlation);

            if (walletResult != MetaActionResult.Success)
                return walletResult;

            if (commit)
            {
                Tournament.MarkMilestoneClaimed(index);

                EventStream.Event(new PlayerEventTournamentMilestoneClaimed(
                    Tournament.Season, index, milestone.ScoredMatches, table.Id, correlation));

                ClientListener.OnTournamentChanged();
            }

            return MetaActionResult.Success;
        }

        /// <summary>
        /// Grants the placement reward of the concluded season for group <paramref name="divisionId"/>, including
        /// its cosmetic. Only <see cref="PlayerTournamentPlacementClaim"/> calls it.
        /// </summary>
        public MetaActionResult ClaimTournamentPlacement(EntityId divisionId, bool commit)
        {
            TournamentHistoryEntry result = TournamentResultOf(divisionId);
            if (result == null)
                return ActionResults.NoSuchTournamentResult;
            if (!result.HasReward)
                return ActionResults.NoTournamentReward;
            if (Tournament.HasClaimedPlacement(divisionId))
                return ActionResults.TournamentRewardAlreadyClaimed;

            AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(PlayerId, CurrentTime, "tournament_placement");

            if (result.Reward != null)
            {
                MetaActionResult walletResult = ApplyWallet(
                    WalletTransaction.Grant(
                        EconomyFeature.Tournament,
                        EconomyReason.TournamentReward,
                        result.Reward,
                        result.RewardTable == null ? null : EconomyContentId.FromString(result.RewardTable.Value)),
                    commit,
                    correlation);

                if (walletResult != MetaActionResult.Success)
                    return walletResult;
            }

            if (commit)
            {
                // Record the claim, the cosmetic and the victory count after the same checks that guard the
                // currency grant. A second win adds a victory but not a second copy of the cosmetic.
                Tournament.MarkPlacementClaimed(result);

                // Cosmetics is the only record of cosmetic ownership, so the reward is added there too. Writing
                // this checksummed member is allowed because PlayerTournamentPlacementClaim is a synchronized
                // server action, which runs at the same timeline position on both sides. The unsynchronized
                // match-completion path could not do this.
                //
                // The cosmetic is not equipped. It is marked unacknowledged, so Profile shows the new-item badge
                // and the player decides whether to wear it.
                if (Cosmetics.Acquire(result.RewardCosmetic))
                {
                    Cosmetics.MarkUnacknowledged(result.RewardCosmetic);
                    ClientListener.OnCosmeticsChanged();
                }

                EventStream.Event(new PlayerEventTournamentPlacementRewardClaimed(
                    result.Season, result.Placement, BandOf(result), result.RewardTable, result.RewardCosmetic, correlation));

                ClientListener.OnTournamentChanged();
            }

            return MetaActionResult.Success;
        }

        /// <summary>The <see cref="TournamentPlacementInfo.MaxRank"/> of the band the result falls in, or the placement itself if there is no such band or table.</summary>
        int BandOf(TournamentHistoryEntry result)
        {
            TournamentPlacementInfo band = TournamentRewardTable(result.RewardTable)?.BandFor(result.Placement);
            return band?.MaxRank ?? result.Placement;
        }

        /// <summary>
        /// Emits the season result event for the concluded season in group <paramref name="divisionId"/>, at most
        /// once per group. Only <see cref="PlayerTournamentSeasonConcluded"/> calls it.
        /// </summary>
        public MetaActionResult RecordTournamentResult(EntityId divisionId, bool commit)
        {
            TournamentHistoryEntry result = TournamentResultOf(divisionId);
            if (result == null)
                return ActionResults.NoSuchTournamentResult;
            if (Tournament.HasReported(divisionId))
                return ActionResults.TournamentResultAlreadyReported;

            if (commit)
            {
                Tournament.MarkReported(divisionId);

                EventStream.Event(new PlayerEventTournamentResolved(
                    result.Season, result.Placement, result.Wins, result.ScoredMatches,
                    result.HumanCount, result.GroupSize - result.HumanCount));

                ClientListener.OnTournamentChanged();
            }

            return MetaActionResult.Success;
        }

        #endregion

        #region The daily reward

        /// <summary>
        /// The daily reward streak and cycle position. Only <see cref="ApplyDailyRewardClaim"/> changes it.
        /// </summary>
        [IgnoreDataMember] public DailyRewardState DailyReward => _dailyReward;

        /// <summary>
        /// Whether this player can claim <paramref name="activation"/>, a player-local day.
        /// <para>
        /// This check makes claiming idempotent. A day at or before the last claimed day cannot be claimed, so a
        /// repeated action, a double tap or a reconnect cannot claim the same day twice
        /// (<c>docs/daily-rewards.md</c>).
        /// </para>
        /// </summary>
        public bool CanClaimDailyReward(int activation) =>
            DailyRewardPolicy.TransitionFor(DailyReward, activation) != DailyStreakTransition.None;

        /// <summary>
        /// Applies a daily reward claim: marks the activation claimed and updates the streak, the skip day and
        /// the cycle position. Only <see cref="PlayerDailyRewardClaimed"/> calls it, on its commit pass.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// <see cref="CanClaimDailyReward"/> is false, so the caller skipped the check. Returning silently would
        /// leave granted currency with no claim recorded.
        /// </exception>
        internal void ApplyDailyRewardClaim(DailyRewardClaim claim, MetaTime claimedAt, DailyRewardTableId tableId)
        {
            if (!CanClaimDailyReward(claim.ActivationIndex))
                throw new InvalidOperationException($"Daily reward activation {claim.ActivationIndex} is already claimed ({DailyReward}); the caller did not consult CanClaimDailyReward.");

            _dailyReward.Apply(claim, claimedAt, tableId);

            // Notify the client here rather than in the action, so every caller triggers the redraw and the
            // reward reveal.
            ClientListener.OnDailyRewardClaimed();
        }

        #endregion

        #region The spin wheel

        /// <summary>
        /// The spin counters and the last result. Only <see cref="ApplySpinResult"/> and
        /// <see cref="AcknowledgeSpinResult"/> change it.
        /// </summary>
        [IgnoreDataMember] public SpinWheelState SpinWheel => _spinWheel ??= new SpinWheelState();

        /// <summary>
        /// Applies a resolved spin: stores the receipt and advances the spin ordinal. Only
        /// <see cref="PlayerWheelSpinResolved"/> calls it, on its commit pass. A spin is refused while a result is
        /// unacknowledged, so a repeated action, a double tap or a reconnect cannot spin twice
        /// (<c>docs/spin-wheel.md</c>).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// A result is pending, or the receipt is not the next ordinal, so the caller skipped
        /// <see cref="SpinWheelPolicy.OutlookFor"/>. Returning silently would leave a spent token with no result.
        /// </exception>
        internal void ApplySpinResult(SpinReceipt receipt)
        {
            if (SpinWheel.HasPendingReceipt)
                throw new InvalidOperationException($"A wheel result is already waiting to be shown ({SpinWheel}); the caller did not consult CanSpinWheel.");
            if (receipt.Ordinal != SpinWheel.NextOrdinal)
                throw new InvalidOperationException($"Wheel receipt {receipt.Ordinal} is not this player's next spin ({SpinWheel}); the caller did not consult CanSpinWheel.");

            _spinWheel.Apply(receipt);

            // Notify the client here rather than in the action, so every caller triggers the redraw and the
            // result reveal.
            ClientListener.OnSpinWheelChanged();
        }

        /// <summary>
        /// Marks the pending spin result as seen. See <see cref="PlayerAcknowledgeWheelSpin"/>.
        /// </summary>
        internal void AcknowledgeSpinResult()
        {
            if (!SpinWheel.HasPendingReceipt)
                throw new InvalidOperationException($"There is no wheel result waiting to be acknowledged ({SpinWheel}); the caller did not consult HasPendingReceipt.");

            _spinWheel.Acknowledge();
            ClientListener.OnSpinWheelChanged();
        }

        #endregion

        #region Cosmetics

        /// <summary>
        /// The cosmetics this player owns and has equipped. Only <see cref="BuyCosmetic"/>,
        /// <see cref="EquipCosmetic"/>, <see cref="AcknowledgeCosmetics"/>, the default grant, the earned-cosmetics
        /// migration and <see cref="ClaimTournamentPlacement"/> change it.
        /// </summary>
        [IgnoreDataMember] public PlayerCosmeticsState Cosmetics => _cosmetics ??= new PlayerCosmeticsState();

        /// <summary>The catalogue entry for <paramref name="id"/>, or null if the config has none.</summary>
        public CosmeticInfo CosmeticOf(CosmeticId id) =>
            id != null && GameConfig?.Cosmetics != null && GameConfig.Cosmetics.TryGetValue(id, out CosmeticInfo info) ? info : null;

        /// <summary>
        /// Buys a cosmetic and equips it. Only <see cref="PlayerBuyCosmetic"/> calls it. The price, the slot and
        /// whether the item is for sale come from the config catalogue. The action carries only the id.
        /// <see cref="ApplyWallet(WalletTransaction, bool, AnalyticsCorrelationId, out WalletSettlement)"/> runs on
        /// both passes before anything else is written, so an unaffordable purchase changes nothing.
        /// </summary>
        public MetaActionResult BuyCosmetic(CosmeticId id, bool commit)
        {
            CosmeticInfo info = CosmeticOf(id);
            if (info == null)
                return ActionResults.NoSuchCosmetic;
            if (!info.IsPurchasable || info.Price == null)
                return ActionResults.CosmeticNotPurchasable;
            if (Cosmetics.Owns(info.Id))
                return ActionResults.CosmeticAlreadyOwned;

            AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(PlayerId, CurrentTime, "cosmetic_purchase");

            MetaActionResult walletResult = ApplyWallet(
                WalletTransaction.Spend(
                    EconomyFeature.Cosmetics,
                    EconomyReason.CosmeticPurchase,
                    info.Price,
                    EconomyContentId.FromString(info.Id.Value)),
                commit,
                correlation,
                out WalletSettlement settlement);

            if (walletResult != MetaActionResult.Success)
                return walletResult;

            if (commit)
            {
                Cosmetics.Acquire(info.Id);

                // Buying also equips (docs/cosmetics.md). The purchase and the equip are separate events, and
                // the equip event's onPurchase flag marks it as part of a purchase.
                EventStream.Event(new PlayerEventCosmeticPurchased(
                    correlation, info.Id, info.Kind,
                    info.Price.Currency, info.Price.Amount,
                    settlement.WalletAfter.AmountOf(info.Price.Currency),
                    Cosmetics.Owned.Count));

                Wear(info, onPurchase: true);
            }

            return MetaActionResult.Success;
        }

        /// <summary>
        /// Equips an owned cosmetic. Only <see cref="PlayerEquipCosmetic"/> calls it.
        /// <para>
        /// The slot comes from the catalogue entry, not from the action, so an action cannot put an item in the
        /// wrong slot.
        /// </para>
        /// </summary>
        public MetaActionResult EquipCosmetic(CosmeticId id, bool commit)
        {
            CosmeticInfo info = CosmeticOf(id);
            if (info == null)
                return ActionResults.NoSuchCosmetic;
            if (!Cosmetics.Owns(info.Id))
                return ActionResults.CosmeticNotOwned;
            if (Cosmetics.IsEquipped(info.Kind, info.Id))
                return ActionResults.CosmeticAlreadyEquipped;

            if (commit)
                Wear(info, onPurchase: false);

            return MetaActionResult.Success;
        }

        /// <summary>
        /// Equips <paramref name="info"/>, emits the equip event, and notifies both listeners.
        /// <para>
        /// Buying and equipping both go through this method so that both call
        /// <see cref="IPlayerModelServerListener.OnPublicIdentityChanged"/>. Holders of a
        /// <see cref="PlayerPublicIdentity"/> keep a snapshot, for example the tournament standings, and would
        /// otherwise show the old appearance (<c>docs/player.md</c>, "Public identity").
        /// </para>
        /// </summary>
        void Wear(CosmeticInfo info, bool onPurchase)
        {
            CosmeticId replaced = Cosmetics.EquippedIn(info.Kind);

            Cosmetics.Equip(info.Kind, info.Id);

            EventStream.Event(new PlayerEventCosmeticEquipped(info.Id, info.Kind, replaced, onPurchase));

            ClientListener.OnCosmeticsChanged();
            ServerListener.OnPublicIdentityChanged();
        }

        /// <summary>
        /// Marks every unacknowledged cosmetic as seen. Only <see cref="PlayerAcknowledgeCosmetics"/> calls it.
        /// </summary>
        public MetaActionResult AcknowledgeCosmetics(bool commit)
        {
            if (!Cosmetics.HasUnacknowledged)
                return ActionResults.NoCosmeticToAcknowledge;

            if (commit)
            {
                Cosmetics.Acknowledge();
                ClientListener.OnCosmeticsChanged();
            }

            return MetaActionResult.Success;
        }

        #endregion
    }
}
