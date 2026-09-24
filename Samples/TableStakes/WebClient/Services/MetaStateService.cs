using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.League;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using System.Timers;
using Microsoft.AspNetCore.Components;
using WebClient.Meta;
using WebClient.Meta.Fixtures;
using WebClientBase.Utilities;

namespace WebClient.Services;

/// <summary>
/// Provides the meta shell's state. Screens read only <see cref="Snapshot"/> (docs/meta-shell.md, "How screens
/// get state"). The snapshot starts from the fixture for the current <c>?meta=</c> scenario. Once a session exists,
/// each <see cref="FixtureSlice"/> is replaced with state read from the player model, unless the scenario pins that
/// slice in <see cref="MetaFixtures.Scenarios"/>. Scenarios declare their pins because many fixture states, such
/// as an actionable first-week day, are also normal real states and cannot be told apart by value.
/// </summary>
public sealed class MetaStateService : IDisposable
{
    /// <summary>The query parameter that selects a fixture scenario.</summary>
    public const string ScenarioParameter = "meta";

    private readonly NavigationManager _navigation;
    private readonly MetaplayClientService _client;
    private readonly System.Timers.Timer _tickTimer;
    private readonly DateTime _startedAt = DateTime.UtcNow;

    private readonly HashSet<string> _seenOfferIds = new HashSet<string>();
    private bool _cosmeticAcknowledged;

    public MetaStateService(NavigationManager navigation, MetaplayClientService client)
    {
        _navigation = navigation;
        _client     = client;

        Analytics    = new PlayerTimelineShellAnalytics(client);
        Observations = new ShellObservationRecorder(Analytics);

        // One timer for the whole shell. Countdowns subscribe to it and redraw only when their displayed text
        // changes.
        _tickTimer = new System.Timers.Timer(1000) { AutoReset = true };
        _tickTimer.Elapsed += OnTick;
        _tickTimer.Start();

        // Rebuild the snapshot when the player model changes, so screens that do not subscribe to the tick still
        // redraw.
        //
        // ModelChanged is raised only when the model changes, unlike MetaplayClientService.OnStateChanged, which
        // is raised on every client update and would rebuild the snapshot far more often than needed. It is also
        // raised at session start, when a new model appears without any model change callback firing.
        _client.ModelChanged += OnModelChanged;
    }

    /// <summary>Raised once a second. Countdowns subscribe to it.</summary>
    public event Action? OnTicked;

    /// <summary>
    /// Raised when the meta state changes, for example after a claim, a purchase or a player model change.
    /// </summary>
    public event Action? OnStateChanged;

    /// <summary>
    /// The shell's analytics sink, which executes <c>PlayerObserve…</c> player actions
    /// (<see cref="PlayerTimelineShellAnalytics"/>).
    /// </summary>
    public IShellAnalytics Analytics { get; }

    /// <summary>
    /// Filters observations before they reach <see cref="Analytics"/>. It is kept on the service instead of in a
    /// component, because a reconnect rebuilds components and would lose the state it needs to detect duplicates.
    /// </summary>
    public ShellObservationRecorder Observations { get; }

    /// <summary>The time since this service was created. Fixture countdowns use it.</summary>
    public TimeSpan Elapsed => DateTime.UtcNow - _startedAt;

    /// <summary>
    /// The fixture scenario from the URL's <see cref="ScenarioParameter"/>, or <see cref="MetaFixtures.Default"/>.
    /// It is read on every render, so the result is cached until the URL changes.
    /// </summary>
    public string Scenario
    {
        get
        {
            string address = _navigation.Uri;
            if (!string.Equals(_scenarioAddress, address, StringComparison.Ordinal))
            {
                _scenarioAddress = address;
                _scenario        = ScenarioIn(address);
            }
            return _scenario;
        }
    }

    private static string ScenarioIn(string address) =>
        QueryParameters.Get(new Uri(address).Query, ScenarioParameter, StringComparison.OrdinalIgnoreCase) ?? MetaFixtures.Default;

    private string? _scenarioAddress;
    private string  _scenario = MetaFixtures.Default;

    /// <summary>
    /// What the player has already seen in this client. It is not stored on the server. The value is cached,
    /// because the navigation bar reads it on every tick, and replaced when the player sees something new.
    /// </summary>
    public SeenState Seen => _seen ??= new SeenState(_seenOfferIds.ToArray(), _cosmeticAcknowledged);

    private SeenState? _seen;

    /// <summary>
    /// The slices the current scenario pins, from <see cref="MetaFixtures.PinnedSliceNames"/>, or empty if it pins
    /// none. Browser tests read it to know which parts of the screen show fixture data.
    /// <para>
    /// The shell writes it to the DOM as the <c>data-authored</c> attribute, which browser tests assert on.
    /// </para>
    /// </summary>
    public string PinnedSliceNames
    {
        get
        {
            string scenario = Scenario;
            if (_claimedSliceNames == null || _claimScenario != scenario)
            {
                _claimScenario     = scenario;
                _claimedSliceNames = MetaFixtures.PinnedSliceNames(scenario);
            }
            return _claimedSliceNames;
        }
    }

    private string? _claimedSliceNames;
    private string? _claimScenario;

    /// <summary>
    /// The current meta state. It is cached and rebuilt after each tick, after a state change, and when the
    /// scenario changes.
    /// <para>
    /// It is cached so that every read during one render sees the same values. Rebuilding on each read would give
    /// slightly different countdowns within a render and new view objects on every read, so reference comparisons
    /// such as <c>LiveFeature == Snapshot.WeeklyEvent</c> would always be false.
    /// </para>
    /// </summary>
    public MetaSnapshot Snapshot
    {
        get
        {
            string scenario = Scenario;
            if (_snapshot == null || _snapshotScenario != scenario)
            {
                // The pins depend only on the scenario, so they are looked up again only when it changes.
                if (_snapshotScenario != scenario)
                    _claimedSlices = MetaFixtures.PinnedBy(scenario);

                _snapshotScenario = scenario;
                _snapshot = WithRealCosmetics(WithRealWeeklyEvent(WithRealSpinWheel(WithRealFirstWeek(WithRealDailyReward(WithRealTournament(WithRealState(MetaFixtures.Build(_snapshotScenario, Elapsed))))))));
            }
            return _snapshot;
        }
    }

    private MetaSnapshot? _snapshot;
    private string? _snapshotScenario;
    private IReadOnlySet<FixtureSlice> _claimedSlices = MetaFixtures.PinnedBy(MetaFixtures.Default);

    /// <summary>
    /// Whether the current scenario pins <paramref name="slice"/>. A pinned slice keeps its fixture value even
    /// when a session exists.
    /// </summary>
    private bool IsPinnedByScenario(FixtureSlice slice) => _claimedSlices.Contains(slice);

    /// <summary>Clears the cached snapshot so that the next read rebuilds it.</summary>
    private void Invalidate() => _snapshot = null;

    /// <summary>Records that the player has seen an offer, which clears the shop badge for it.</summary>
    public void MarkOfferSeen(string offerId)
    {
        if (_seenOfferIds.Add(offerId))
        {
            _seen = null;
            Invalidate();
            OnStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Records that the player has acknowledged newly acquired cosmetics. It sets a local flag that clears the badge
    /// in this tab, and executes the acknowledge action so that the badge stays cleared after a reload.
    /// <para>
    /// It does nothing without a session, because the local flag alone would leave the server state unchanged. The
    /// screen first renders before the session exists, so callers call it again when the state changes.
    /// </para>
    /// </summary>
    public void AcknowledgeCosmetic()
    {
        if (!HasSession)
            return;

        // The result is ignored. Null means there was nothing to acknowledge, which counts as acknowledged.
        _client.AcknowledgeCosmetics();

        if (!_cosmeticAcknowledged)
        {
            _cosmeticAcknowledged = true;
            _seen = null;
            Invalidate();
            OnStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Records that the player is at <paramref name="route"/>. The shell calls it on every navigation, and
    /// <see cref="Observations"/> filters out repeats.
    /// </summary>
    public void RecordArrival(string route) => Observations.Arrived(route, HasSession);

    /// <summary>Records that the player selected a promoted entry leading to <paramref name="destination"/>.</summary>
    public void RecordPromotedEntrySelection(PromotedEntryPlacement placement, MetaFeature destination) =>
        Observations.Selected(placement, destination, HasSession);

    /// <summary>
    /// Whether a session exists. Observations and mutations need one because they are player actions.
    /// </summary>
    private bool HasSession => _client.PlayerModel != null;

    /// <summary>
    /// Pinned a finished mission's reward. Returns the action result, or null if there is no session (see
    /// <see cref="RefreshAfterAction"/>).
    /// </summary>
    public MetaActionResult? ClaimMission(string instanceId) => RefreshAfterAction(_client.ClaimMissionReward(instanceId));

    /// <summary>
    /// Called by every mutation after submitting its action: rebuilds the snapshot and raises
    /// <see cref="OnStateChanged"/>, then returns <paramref name="result"/>.
    /// <para>
    /// A null result means there was no session, as in fixture scenarios without a session. Nothing is rebuilt,
    /// and the caller plays the reward flow's stages without a real mutation.
    /// </para>
    /// </summary>
    private MetaActionResult? RefreshAfterAction(MetaActionResult? result)
    {
        if (result != null)
        {
            Invalidate();
            OnStateChanged?.Invoke();
        }

        return result;
    }

    /// <summary>
    /// Pinned a completed first-week day's reward. Returns the action result, or null if there is no session (see
    /// <see cref="RefreshAfterAction"/>).
    /// </summary>
    public MetaActionResult? ClaimFirstWeekDay(string dayId) => RefreshAfterAction(_client.ClaimFirstWeekReward(dayId));

    /// <summary>
    /// Pinned the reward of a weekly event whose target the player has reached. Returns the action result, or null
    /// if there is no session (see <see cref="RefreshAfterAction"/>).
    /// </summary>
    public MetaActionResult? ClaimWeeklyEvent(string eventId) => RefreshAfterAction(_client.ClaimWeeklyEventReward(eventId));

    /// <summary>
    /// Replaces the weekly event slice with the player's real weekly event state, unless the scenario pins it. A
    /// real <see cref="ActivityState.Unavailable"/> view also replaces the fixture, unlike in
    /// <see cref="WithRealFirstWeek"/>, because it correctly reports that no event is scheduled. It uses the player
    /// model's time, not <c>DateTime.UtcNow</c>, because the SDK advances event phases on the model's timeline.
    /// </summary>
    private MetaSnapshot WithRealWeeklyEvent(MetaSnapshot snapshot)
    {
        PlayerModel? player = _client.PlayerModel;
        if (player == null)
            return snapshot;

        if (IsPinnedByScenario(FixtureSlice.WeeklyEvent))
            return snapshot;

        return snapshot with { WeeklyEvent = WeeklyEventViewBuilder.From(player.WeeklyEvent, player.LiveOpsEvents, player.CurrentTime) };
    }

    /// <summary>
    /// Replaces the first-week slice with the player's real first-week state, unless the scenario pins it or the
    /// real view is <see cref="ActivityState.Unavailable"/>.
    /// <para>
    /// It uses the player model's time, not <c>DateTime.UtcNow</c>, because the model's time decides when a day
    /// ends.
    /// </para>
    /// </summary>
    private MetaSnapshot WithRealFirstWeek(MetaSnapshot snapshot)
    {
        PlayerModel? player = _client.PlayerModel;
        if (player?.GameConfig == null)
            return snapshot;

        if (IsPinnedByScenario(FixtureSlice.FirstWeek))
            return snapshot;

        FirstWeekView view = FirstWeekViewBuilder.From(player.FirstWeek, player.GameConfig, player.CurrentTime);
        return view.State == ActivityState.Unavailable ? snapshot : snapshot with { FirstWeek = view };
    }

    /// <summary>
    /// The player's real missions view, using the player model's time and time zone offset, because they decide
    /// when missions reset.
    /// </summary>
    private static MissionsView? RealMissions(PlayerModel player) =>
        MissionsViewBuilder.From(player.Missions, player.GameConfig, player.CurrentTime, player.TimeZoneInfo.CurrentUtcOffset);

    /// <summary>
    /// Replaces the identity, wallet, missions, shop and record slices with real player state. Before a session
    /// exists the fixture values remain, so the first render shows a complete screen.
    /// <para>
    /// Each slice checks its own pin, so a scenario can keep some of these slices and not others.
    /// </para>
    /// </summary>
    private MetaSnapshot WithRealState(MetaSnapshot snapshot)
    {
        PlayerPublicIdentity? publicIdentity = _client.PublicIdentity;
        PlayerModel?          player         = _client.PlayerModel;
        if (publicIdentity == null || player == null)
            return snapshot;

        // Use the player's public identity instead of the model fields, so the HUD and Profile show the same
        // identity that other players see in the standings (docs/player.md, "Public identity").
        //
        // CosmeticsPolicy.ViewOf converts the worn catalogue ids to style tokens, as the game table's seat
        // plaques do. A slot with nothing worn becomes an empty string, and the avatar component shows its
        // default for it.
        //
        // Only the name and cosmetics are copied. WithRealTournament fills in the competition standing.
        IdentityView worn = CosmeticsPolicy.ViewOf(player.GameConfig, publicIdentity);

        IdentityView identity = IsPinnedByScenario(FixtureSlice.Identity)
            ? snapshot.Identity
            : snapshot.Identity with
              {
                  DisplayName     = worn.DisplayName,
                  AvatarToken     = worn.AvatarToken,
                  FrameToken      = worn.FrameToken,
                  NameEffectToken = worn.NameEffectToken,
              };

        return snapshot with
        {
            Identity = identity,
            Wallet   = IsPinnedByScenario(FixtureSlice.Wallet) ? snapshot.Wallet : new WalletView(player.Wallet.Coins, player.Wallet.Gems, player.Wallet.SpinTokens),
            Missions = IsPinnedByScenario(FixtureSlice.Missions) ? snapshot.Missions : RealMissions(player) ?? snapshot.Missions,
            Shop     = IsPinnedByScenario(FixtureSlice.Shop) ? snapshot.Shop : WithRealShop(snapshot.Shop, player),
            Profile  = snapshot.Profile with
            {
                Identity    = identity,
                GamesPlayed = IsPinnedByScenario(FixtureSlice.Record) ? snapshot.Profile.GamesPlayed : _client.Record.GamesPlayed,
                GamesWon    = IsPinnedByScenario(FixtureSlice.Record) ? snapshot.Profile.GamesWon    : _client.Record.GamesWon,
                TricksWon   = IsPinnedByScenario(FixtureSlice.Record) ? snapshot.Profile.TricksWon   : _client.Record.TricksWon,
            },
        };
    }

    /// <summary>
    /// Replaces the cosmetics catalogue with the game config's items and the player's owned and equipped items,
    /// unless the scenario pins the slice. Affordability is only for display: the server decides whether a
    /// purchase succeeds.
    /// <para>
    /// Items of a kind this client cannot render are left out, so the player is never offered an item that could
    /// not be shown when worn. They stay in the config so that existing ownership records remain valid
    /// (<c>docs/cosmetics.md</c>).
    /// </para>
    /// </summary>
    private MetaSnapshot WithRealCosmetics(MetaSnapshot snapshot)
    {
        PlayerModel? player = _client.PlayerModel;
        if (player?.GameConfig?.Cosmetics == null || IsPinnedByScenario(FixtureSlice.Cosmetics))
            return snapshot;

        List<CosmeticItem> items = new List<CosmeticItem>();
        foreach (CosmeticInfo info in player.GameConfig.Cosmetics.Values)
        {
            if (!CosmeticStyles.IsShipped(info.Kind))
                continue;

            CurrencyKind currency = info.Price?.Currency == CurrencyType.Gems ? CurrencyKind.Gems : CurrencyKind.Coins;

            items.Add(new CosmeticItem(
                Id:                info.Id.Value,
                Slot:              SlotOf(info.Kind),
                Name:              info.DisplayName,
                Flavour:           info.Flavour ?? "",
                Ownership:         CosmeticsPolicy.OwnershipOf(
                                       isOwned:           player.Cosmetics.Owns(info.Id),
                                       isEquipped:        player.Cosmetics.IsEquipped(info.Kind, info.Id),
                                       isPurchasable:     info.IsPurchasable && info.Price != null,
                                       canAfford:         info.Price != null && player.Wallet.CanAfford(info.Price),
                                       unlockRequirement: info.UnlockRequirement ?? ""),
                PriceCurrency:     currency,
                PriceAmount:       info.Price?.Amount ?? 0,
                UnlockRequirement: info.UnlockRequirement ?? "",
                StyleToken:        info.StyleToken));
        }

        // Ready instead of Actionable. The only badge-worthy case, a newly acquired item, is read by BadgePolicy
        // from HasUnacknowledgedAcquisition (docs/meta-shell.md).
        CosmeticsView cosmetics = snapshot.Cosmetics with
        {
            State                        = ActivityState.Ready,
            Items                        = items,
            HasUnacknowledgedAcquisition = player.Cosmetics.HasUnacknowledged,
        };

        // MetaSnapshot.Cosmetics returns Profile.Cosmetics, so setting it on the Profile is enough.
        return snapshot with { Profile = snapshot.Profile with { Cosmetics = cosmetics } };
    }

    private static CosmeticSlot SlotOf(CosmeticKind kind) => kind switch
    {
        CosmeticKind.Avatar => CosmeticSlot.Avatar,
        CosmeticKind.Frame  => CosmeticSlot.Frame,
        _                   => CosmeticSlot.NameEffect,
    };

    /// <summary>
    /// Buys a cosmetic. Returns the action result, or null if there is no session, in which case the screen does
    /// not treat the fixture item as bought.
    /// </summary>
    public MetaActionResult? BuyCosmetic(string cosmeticId) => RefreshAfterAction(_client.BuyCosmetic(cosmeticId));

    /// <summary>Equips an owned cosmetic. Returns the action result, or null if there is no session.</summary>
    public MetaActionResult? EquipCosmetic(string cosmeticId) => RefreshAfterAction(_client.EquipCosmetic(cosmeticId));

    /// <summary>
    /// The Shop's featured offer and catalogue from the SDK's offer group state (<c>docs/offers.md</c>, "Offer
    /// groups and placements"). Falls back to the fixture values when the config has no offer groups or a
    /// placement resolves to nothing.
    /// <para>
    /// The caller checks the <see cref="FixtureSlice.Shop"/> pin before calling this method.
    /// </para>
    /// </summary>
    private ShopView WithRealShop(ShopView shop, PlayerModel? player)
    {
        if (player?.GameConfig?.OfferGroups == null)
            return shop;

        OfferView? featured  = ResolveFeatured(player);
        OfferView[] catalogue = ResolveCatalogue(player);

        // The featured offer is also configured in the catalogue placement, so that it stays available after it
        // stops being featured. Remove it from the catalogue so the same card does not appear twice.
        if (featured != null)
            catalogue = catalogue.Where(o => o.Id != featured.Id).ToArray();

        return shop with
        {
            Featured  = featured ?? shop.Featured,
            Catalogue = catalogue.Length > 0 ? catalogue : shop.Catalogue,
        };
    }

    /// <summary>
    /// The featured offer: the first offer of the first active group in
    /// <c>MetaOfferGroupsPerPlacementInMostImportantFirstOrder</c> for <c>ShopFeatured</c>, or null if none.
    /// <para>
    /// Two groups can both be active for one placement, because a transient group can become active again without
    /// a placement availability check (see the SDK's <c>IsTransient</c>). Picking the first active group here keeps
    /// the featured slot to one offer.
    /// </para>
    /// </summary>
    private OfferView? ResolveFeatured(PlayerModel player)
    {
        if (!player.GameConfig.MetaOfferGroupsPerPlacementInMostImportantFirstOrder.TryGetValue(OfferPlacementIds.ShopFeatured, out List<MetaOfferGroupInfoBase>? groups))
            return null;

        foreach (MetaOfferGroupInfoBase groupInfo in groups)
        {
            if (!player.MetaOfferGroups.IsActive(groupInfo.GroupId, player))
                continue;

            // Mark the offer as targeted only if its group is segmented and the offer itself is active. An active
            // group can contain an offer whose own conditions do not hold, and that offer was not chosen for the
            // player.
            foreach (MetaOfferStatus status in player.MetaOfferGroups.GetOffersInGroup(groupInfo, player))
                return OfferViewOf(status, groupInfo, player,
                    isTargeted: groupInfo.ActivableParams?.Segments?.Count > 0 && status.IsActive);
        }

        return null;
    }

    /// <summary>
    /// Every catalogue offer in configured order, including sold-out and locked offers. <c>GetOffersInGroup</c>
    /// returns every offer in the group regardless of eligibility, so locked offers are shown with a reason
    /// instead of being hidden.
    /// </summary>
    private OfferView[] ResolveCatalogue(PlayerModel player)
    {
        IEnumerable<OfferGroupInfo> groups = player.GameConfig.OfferGroups.Values.Where(g => g.Placement == OfferPlacementIds.ShopCatalogue);

        List<OfferView> catalogue = new();
        foreach (OfferGroupInfo groupInfo in groups)
        {
            foreach (MetaOfferStatus status in player.MetaOfferGroups.GetOffersInGroup(groupInfo, player))
                catalogue.Add(OfferViewOf(status, groupInfo, player,
                    // Targeted only if the offer has segments and IsActive is true. The catalogue includes offers
                    // the player cannot buy, and those must not be labelled "Just for you". IsActive means that
                    // every condition on the offer holds, including its segments.
                    isTargeted: status.Info.Segments?.Count > 0 && status.IsActive));
        }
        return catalogue.ToArray();
    }

    /// <summary>
    /// Builds the view for one offer. Availability comes from the SDK's <see cref="MetaOfferStatus"/>:
    /// <see cref="OfferAvailability.SoldOut"/> when a per-activation limit is reached, with a countdown to the
    /// activation's end, <see cref="OfferAvailability.Purchased"/> when another purchase limit is reached,
    /// <see cref="OfferAvailability.Locked"/> when the offer's conditions do not hold, with its
    /// <see cref="MetaOfferInfoBase.Description"/> as the reason (<c>docs/offers.md</c>), and otherwise
    /// <see cref="OfferAvailability.Available"/>.
    /// </summary>
    private OfferView OfferViewOf(MetaOfferStatus status, MetaOfferGroupInfoBase groupInfo, PlayerModel player, bool isTargeted)
    {
        Game.Logic.OfferInfo offer = (Game.Logic.OfferInfo)status.Info;

        OfferAvailability availability;
        TimeSpan? remaining = null;

        if (status.AnyPurchaseLimitReached)
        {
            if (offer.MaxPurchasesPerActivation.HasValue)
            {
                availability = OfferAvailability.SoldOut;
                remaining    = ActivationRemaining(player, groupInfo.GroupId);
            }
            else
            {
                availability = OfferAvailability.Purchased;
            }
        }
        else if (!status.IsActive)
        {
            availability = OfferAvailability.Locked;
        }
        else
        {
            availability = OfferAvailability.Available;
        }

        // A price must have a currency the shell can show, so an unknown currency falls back to coins. The config
        // parser rejects a price without a currency, so the fallback applies only to a currency that
        // Currencies.KindOf does not map yet.
        OfferPrice price = offer.HasInGameCurrencyCost
            ? OfferPrice.In(Currencies.KindOf(offer.PriceCurrency!.Value) ?? CurrencyKind.Coins, offer.PriceAmount!.Value)
            : OfferPrice.Demo((offer.InAppProduct?.Ref as DemoInAppProductInfo)?.DemoPriceText ?? "");

        // The currency whose balance the offer's contents would push over its cap. OverflowingCurrency is the same
        // check the server uses to refuse the purchase, so the card and the server agree. Only available offers
        // are checked, because other states already explain why there is no Buy button.
        CurrencyType overflowingCurrency = availability == OfferAvailability.Available
            ? offer.OverflowingCurrency(player)
            : CurrencyType.None;

        return new OfferView(
            Id:           offer.OfferId.Value,
            Title:        offer.DisplayName,
            Tagline:      offer.Description,
            Contents:     RewardView.From(offer.Contents),
            Price:        price,
            Remaining:    remaining,
            Availability: availability,
            LockReason:   availability == OfferAvailability.Locked ? offer.Description : "",
            BonusPercent: null,
            IsTargeted:   isTargeted,
            IconId:       offer.IconId,
            BlockedBy:    Currencies.KindOf(overflowingCurrency));
    }

    /// <summary>
    /// The time until the group's latest activation ends, for a sold-out offer's countdown, or null if unknown.
    /// </summary>
    private static TimeSpan? ActivationRemaining(PlayerModel player, MetaOfferGroupId groupId)
    {
        Metaplay.Core.Activables.MetaActivableState? state = player.MetaOfferGroups.TryGetState(groupId);
        MetaTime? endAt = state?.LatestActivation?.EndAt;
        return endAt.HasValue ? Countdown.NonNegative(endAt.Value - player.CurrentTime) : null;
    }

    /// <summary>
    /// Replaces the tournament slice with the player's tournament state and division, unless the scenario pins
    /// it. Before a session exists the fixture values remain.
    /// <para>
    /// The standings, including bots in empty seats, come from <see cref="TournamentDivisionModel.Standings"/>,
    /// which the division actor also uses to decide placements. The player's run comes from the player model, and
    /// the rewards come from the reward table in the game config.
    /// </para>
    /// </summary>
    private MetaSnapshot WithRealTournament(MetaSnapshot snapshot)
    {
        PlayerModel? player = _client.PlayerModel;
        if (player == null)
            return snapshot;

        if (IsPinnedByScenario(FixtureSlice.Tournament))
            return snapshot;

        PlayerTournamentState      tournamentState = player.Tournament;
        SharedGameConfig?          config          = player.GameConfig;
        TournamentRewardTableInfo? table           = player.JoinedSeasonRewardTable;

        TournamentDivisionModel?  division      = _client.TournamentDivision;
        TournamentHistoryEntry?   pendingResult = _client.PendingTournamentResult;
        MetaTime                  now           = MetaTime.Now;

        DivisionSeasonPhase phase = division.ComputeSeasonPhaseAt(now);

        // Check for a running season instead of HasJoined. HasJoined stays set after the player's season ends, and
        // using it would leave the player on a finished tournament with no way to join the next one.
        bool inSeason = tournamentState.IsRunningAt(now) || phase == DivisionSeasonPhase.Ongoing || phase == DivisionSeasonPhase.Preview;

        EntityId self = player.PlayerId;

        IReadOnlyList<StandingRow> standings = division == null
            ? Array.Empty<StandingRow>()
            : StepRailBuilder.StandingsOf(division.Standings(now), self, id => CosmeticsPolicy.StyleTokenOf(config, id));

        TournamentView view = snapshot.Tournament with
        {
            State              = StateOf(inSeason, division, phase, pendingResult),
            Name               = "Seasonal Tournament",
            PhaseLabel         = PhaseTextOf(tournamentState, inSeason, phase),
            SeasonNumber       = inSeason ? TournamentSeasons.NumberOf(tournamentState.Season) : 0,
            IsInSeason         = inSeason,
            UntilStart         = phase == DivisionSeasonPhase.Preview && division != null ? Countdown.NonNegative(division.StartsAt - now) : null,
            UntilEnd           = phase == DivisionSeasonPhase.Ongoing && division != null ? Countdown.NonNegative(division.EndsAt - now) : null,
            ScoredMatches      = tournamentState.ScoredMatches,
            MatchCap           = TournamentRules.ScoredMatchCap,
            Standings          = inSeason ? standings : Array.Empty<StandingRow>(),
            Milestones         = MilestonesOf(tournamentState, table),
            PlacementBands     = BandsOf(table, config),
            HasClaimableReward = pendingResult != null,
            PendingRank        = pendingResult?.Placement ?? 0,
            PendingReward      = pendingResult == null ? RewardView.Empty : Bundle(pendingResult.Reward, pendingResult.RewardCosmetic, config),
        };

        // Read the player's rank and score from their own standings row, so the summary matches the row. The
        // run's win count is the fallback, and also wins when it is higher, which happens after a match is
        // counted on the player but before the score reaches the division.
        int  selfRank  = 0;
        long selfScore = tournamentState.Wins;
        foreach (StandingRow row in standings)
        {
            if (row.IsSelf)
            {
                selfRank  = row.Rank;
                selfScore = Math.Max(row.Score, tournamentState.Wins);
            }
        }

        // Put the real competition standing on the identity, so the Profile shows the player's real rank.
        //
        // A pin on either Tournament or Identity keeps the fixture standing (see FixtureSlice). A Tournament pin
        // returns earlier in this method.
        //
        // Both fields use inSeason instead of HasJoined, because HasJoined and the division standings remain
        // after the season ends. Otherwise the Profile would show a rank while Compete invites the player to join
        // the next season.
        IdentityView identity = IsPinnedByScenario(FixtureSlice.Identity)
            ? snapshot.Identity
            : snapshot.Identity with
              {
                  CompetitionName = inSeason ? "Seasonal Tournament" : string.Empty,
                  CompetitionRank = inSeason ? selfRank : 0,
              };

        return snapshot with
        {
            Identity   = identity,
            Profile    = snapshot.Profile with { Identity = identity },
            Tournament = view with { Rank = selfRank, Score = selfScore },
        };
    }

    private static ActivityState StateOf(bool inSeason, TournamentDivisionModel? division, DivisionSeasonPhase phase, TournamentHistoryEntry? pending)
    {
        if (pending != null)
            return ActivityState.Actionable;
        if (!inSeason)
            return ActivityState.Ready;
        if (division == null)
            return ActivityState.Loading;

        return phase switch
        {
            DivisionSeasonPhase.Preview   => ActivityState.Ready,
            DivisionSeasonPhase.Ongoing   => ActivityState.InProgress,
            DivisionSeasonPhase.Resolving => ActivityState.InProgress,
            DivisionSeasonPhase.Concluded => ActivityState.Completed,
            _                             => ActivityState.Unavailable,
        };
    }

    private static string PhaseTextOf(PlayerTournamentState run, bool inSeason, DivisionSeasonPhase phase)
    {
        if (!inSeason)
            return "Open to enter";

        return phase switch
        {
            DivisionSeasonPhase.Preview   => "Starting soon",
            DivisionSeasonPhase.Resolving => "Finalising results",
            DivisionSeasonPhase.Concluded => $"Season {TournamentSeasons.NumberOf(run.Season)} finished",
            _                             => $"Season {TournamentSeasons.NumberOf(run.Season)}",
        };
    }

    /// <summary>
    /// The participation milestones from <c>PlayerTournamentState.Milestones</c>. A reached but unclaimed
    /// milestone stays claimable after the season ends.
    /// </summary>
    private static IReadOnlyList<TournamentMilestoneView> MilestonesOf(PlayerTournamentState run, TournamentRewardTableInfo? table)
    {
        List<TournamentMilestoneView> views = new List<TournamentMilestoneView>();
        foreach (TournamentMilestoneStatus status in run.Milestones(table))
        {
            views.Add(new TournamentMilestoneView(
                status.Index,
                status.ScoredMatches,
                RewardView.From(status.Reward),
                status.IsReached,
                status.IsClaimed));
        }
        return views;
    }

    /// <summary>
    /// Every placement band of the reward table, in table order. The screen shows each band's reward on the
    /// standings rows that earn it.
    /// </summary>
    private static IReadOnlyList<PlacementBandView> BandsOf(TournamentRewardTableInfo? table, SharedGameConfig? config)
    {
        if (table == null)
            return Array.Empty<PlacementBandView>();

        List<PlacementBandView> bands = new List<PlacementBandView>();
        foreach (TournamentPlacementInfo band in table.Placements)
            bands.Add(new PlacementBandView(band.MaxRank, Bundle(band.Reward, band.Cosmetic?.Ref?.Id, config)));

        return bands;
    }

    /// <summary>
    /// Converts a game config reward bundle, plus an optional cosmetic reward, for the shell. The cosmetic is shown
    /// with its catalogue display name, or with its id if <paramref name="config"/> does not contain it.
    /// </summary>
    private static RewardView Bundle(Game.Logic.RewardBundle? reward, CosmeticId? cosmetic, SharedGameConfig? config)
    {
        RewardViewItem? cosmeticItem = null;
        if (cosmetic != null)
        {
            string label = config != null && config.Cosmetics.TryGetValue(cosmetic, out CosmeticInfo info)
                ? info.DisplayName
                : cosmetic.Value;
            cosmeticItem = RewardViewItem.Item(label, "frame");
        }

        return RewardView.From(reward, cosmeticItem);
    }

    /// <summary>
    /// Replaces the daily reward slice with the player's real state, unless the scenario pins it or the config
    /// has no active reward table. All rules come from <see cref="DailyRewardPolicy"/>, the code the server also
    /// uses, and this method only builds the view.
    /// The player model's time is used for display only. If the client's clock is ahead, the card can show a
    /// claimable reward that the server then refuses (<c>docs/daily-rewards.md</c>). The real view is only
    /// <see cref="ActivityState.Actionable"/> or <see cref="ActivityState.Completed"/>, so other states appear only
    /// in scenarios that pin this slice.
    /// </summary>
    private MetaSnapshot WithRealDailyReward(MetaSnapshot snapshot)
    {
        PlayerModel? player = _client.PlayerModel;
        if (player?.GameConfig == null)
            return snapshot;

        if (IsPinnedByScenario(FixtureSlice.DailyReward))
            return snapshot;

        DailyRewardTableInfo? table = DailyRewardPolicy.ActiveTable(player.GameConfig);
        if (table == null)
            return snapshot;

        PlayerLocalTime now = DailyRewardPolicy.LocalTimeOf(player, player.CurrentTime);

        DailyRewardOutlook outlook = DailyRewardPolicy.OutlookAt(player.DailyReward, player.GameConfig.Global, table, now);

        List<CycleStepView> cycle = new List<CycleStepView>();
        foreach (DailyRewardStepInfo step in table.Steps)
            cycle.Add(new CycleStepView(step.Step, step.Reward.AmountOf(CurrencyType.Coins), step.Reward.Grants(CurrencyType.SpinTokens)));

        return snapshot with
        {
            DailyReward = new DailyRewardView(
                State:               outlook.IsClaimable ? ActivityState.Actionable : ActivityState.Completed,
                StreakDays:          outlook.StreakDays,
                HasClaimableReward:  outlook.IsClaimable,
                UntilNextAvailable:  outlook.UntilNextAvailable(now.Time).ToTimeSpan(),
                TodayReward:         RewardView.From(outlook.Today?.Reward),
                TomorrowReward:      RewardView.From(outlook.Tomorrow?.Reward),
                Cycle:               cycle,
                StepsClaimedInCycle: outlook.StepsClaimedInCycle,
                SkipDayAvailable:    outlook.SkipDayAvailable,
                WillUseSkipDay:      outlook.Claim.UsesSkipDay,
                WillResetStreak:     outlook.Claim.ResetsStreak),
        };
    }

    /// <summary>
    /// Requests a wheel spin from the server. The snapshot is rebuilt when the result changes the player model.
    /// <para>
    /// Returns false if there is no session, in which case the screen plays the reveal without a real spin.
    /// </para>
    /// </summary>
    public bool SpinWheel() => _client.RequestWheelSpin();

    /// <summary>
    /// Marks the pending spin receipt as seen. Returns null if there is no session or no pending receipt.
    /// </summary>
    public MetaActionResult? AcknowledgeSpin() => RefreshAfterAction(_client.AcknowledgeWheelSpin());

    /// <summary>
    /// Replaces the spin wheel slice with the player's real state and the game config's wheel table, unless the
    /// scenario pins it. The odds are computed from the sectors by <see cref="WheelTableInfo.Odds"/>, so no
    /// configured percentages can disagree with the wheel (<c>docs/spin-wheel.md</c>).
    /// <para>
    /// Spins depend only on the token balance, with no cooldown. The pending receipt is the player's unacknowledged
    /// spin receipt, which the screen shows before offering another spin.
    /// </para>
    /// </summary>
    private MetaSnapshot WithRealSpinWheel(MetaSnapshot snapshot)
    {
        PlayerModel? player = _client.PlayerModel;
        if (player?.GameConfig == null)
            return snapshot;

        if (IsPinnedByScenario(FixtureSlice.SpinWheel))
            return snapshot;

        WheelTableInfo? table = SpinWheelPolicy.ActiveTable(player.GameConfig);
        if (table == null || !table.IsComplete)
            return snapshot with { SpinWheel = snapshot.SpinWheel with { State = ActivityState.Unavailable } };

        SpinWheelOutlook outlook = SpinWheelPolicy.OutlookFor(player.SpinWheel, player.Wallet, player.GameConfig);

        List<WheelSectorView> sectors = new List<WheelSectorView>();
        for (int index = 0; index < table.Sectors.Count; index++)
        {
            WheelSectorInfo sector = table.Sectors[index];
            sectors.Add(new WheelSectorView(index, Currencies.KindOf(sector.Currency), sector.Amount, TierOf(sector.Tier)));
        }

        List<WheelOddsRow> odds = new List<WheelOddsRow>();
        foreach (WheelOddsEntry entry in table.Odds())
            odds.Add(new WheelOddsRow(Currencies.KindOf(entry.Currency), entry.Amount, entry.ChancePercent));

        SpinReceipt? pendingReceipt = outlook.PendingReceipt;
        SpinReceiptView? pendingView = pendingReceipt == null
            ? null
            : new SpinReceiptView(
                pendingReceipt.SectorIndex,
                RewardView.From(pendingReceipt.Reward),
                TierOf(table.SectorAt(pendingReceipt.SectorIndex)?.Tier ?? WheelPrizeTier.Common));

        // The currency whose balance a wheel prize would push over its cap, so the screen can tell the player which
        // currency to spend. The server refuses spins with the same function.
        CurrencyType overflowingCurrency = SpinWheelPolicy.OverflowingCurrency(player.Wallet, WalletCaps.From(player.GameConfig.Global), table);

        return snapshot with
        {
            SpinWheel = new SpinWheelView(
                State:          outlook.SpinsAvailable > 0 || pendingView != null ? ActivityState.Actionable : ActivityState.Ready,
                SpinsAvailable: outlook.SpinsAvailable,
                PrizeTeaser:    TeaserOf(table),
                TopPrize:       TopPrizeOf(table),
                Wheel:          sectors,
                Odds:           odds,
                PendingReceipt: pendingView,
                BlockedBy:      Currencies.KindOf(overflowingCurrency)),
        };
    }

    /// <summary>
    /// The teaser text, built from the table's largest coin and gem prizes so that it follows config changes.
    /// </summary>
    private static string TeaserOf(WheelTableInfo table)
    {
        long topCoins = 0;
        long topGems  = 0;
        foreach (WheelSectorInfo sector in table.Sectors)
        {
            if (sector.Currency == CurrencyType.Coins && sector.Amount > topCoins)
                topCoins = sector.Amount;
            if (sector.Currency == CurrencyType.Gems && sector.Amount > topGems)
                topGems = sector.Amount;
        }

        if (topCoins > 0 && topGems > 0)
            return $"Up to {Currencies.Format(topCoins)} coins or {Currencies.Format(topGems)} gems";
        return topCoins > 0 ? $"Up to {Currencies.Format(topCoins)} coins" : $"Up to {Currencies.Format(topGems)} gems";
    }

    /// <summary>The largest coin prize, for the top prize display, or empty if the table has no coin prize.</summary>
    private static RewardView TopPrizeOf(WheelTableInfo table)
    {
        WheelSectorInfo? best = null;
        foreach (WheelSectorInfo sector in table.Sectors)
        {
            if (sector.Currency == CurrencyType.Coins && (best == null || sector.Amount > best.Amount))
                best = sector;
        }

        return best == null
            ? RewardView.Empty
            : RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, best.Amount));
    }

    /// <summary>Converts a config wheel tier to the shell's <see cref="WheelTier"/>.</summary>
    private static WheelTier TierOf(WheelPrizeTier tier) => tier switch
    {
        WheelPrizeTier.Uncommon  => WheelTier.Uncommon,
        WheelPrizeTier.Rare      => WheelTier.Rare,
        WheelPrizeTier.Premium   => WheelTier.Premium,
        WheelPrizeTier.SpinAgain => WheelTier.SpinAgain,
        WheelPrizeTier.Nothing   => WheelTier.Nothing,
        _                        => WheelTier.Common,
    };

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        Invalidate();
        OnTicked?.Invoke();
    }

    /// <summary>
    /// Clears the cached snapshot and raises <see cref="OnStateChanged"/> when the player model changes.
    /// <para>
    /// ModelChanged is not raised when only time passes. The once-a-second tick clears the snapshot for that,
    /// which also handles the local day changing at midnight.
    /// </para>
    /// </summary>
    private void OnModelChanged()
    {
        Invalidate();
        OnStateChanged?.Invoke();
    }

    public void Dispose()
    {
        _client.ModelChanged -= OnModelChanged;
        _tickTimer.Elapsed -= OnTick;
        _tickTimer.Stop();
        _tickTimer.Dispose();
    }
}
