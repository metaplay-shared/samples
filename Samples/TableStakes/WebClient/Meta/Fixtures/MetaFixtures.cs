namespace WebClient.Meta.Fixtures;

using WebClient.Meta;

/// <summary>
/// Fixture meta state, used before a session exists and for the slices that a <c>?meta=</c> scenario pins.
/// <para>
/// The UI has no scenario switcher (docs/meta-shell.md, "Fixtures and ?meta= scenarios"). The scenarios cover
/// every <see cref="ActivityState"/> and every rung of <see cref="NextActionPolicy"/>, so the shell can be tested
/// without a server and without waiting for real time to pass.
/// </para>
/// </summary>
public static class MetaFixtures
{
    /// <summary>The scenario used when none is given. It matches the approved Home design mock.</summary>
    public const string Default = "default";

    /// <summary>
    /// Every scenario with its description and pinned slices. Each <see cref="NextActionPolicy"/> rung has at
    /// least one scenario.
    /// <para>
    /// Once a session exists, only the pinned slices keep their fixture data. See <see cref="ScenarioInfo"/>.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ScenarioInfo> Scenarios = new Dictionary<string, ScenarioInfo>
    {
        // Pinned nothing, so with a session every surface shows the player's real state.
        [Default] = ScenarioInfo.PinningNothing(
            "The populated default. Today's login reward is waiting — ladder rung 2."),

        // Rung 1 by deadline. The daily reward slice is pinned so that it shows as already claimed next to the
        // open first-week day. Rung 1 does not need it, because Choose returns before checking the daily reward.
        ["first-week"] = ScenarioInfo.Pinning(
            "A first-week day with unfinished goals and minutes left — rung 1, and the featured hub card.",
            FixtureSlice.DailyReward, FixtureSlice.FirstWeek),

        // Rung 1 by claimable reward. It is the only scenario with RewardReady and Missed day tiles, and so the
        // only one that shows the first-week Claim buttons on Home and Events and the Events badge.
        ["first-week-reward"] = ScenarioInfo.Pinning(
            "A first-week reward still owed from an earlier day, and a day that lapsed — rung 1 by entitlement rather than by clock.",
            FixtureSlice.DailyReward, FixtureSlice.FirstWeek),

        ["missions"] = ScenarioInfo.Pinning(
            "A finished mission, uncollected — rung 3.",
            FixtureSlice.DailyReward, FixtureSlice.FirstWeek, FixtureSlice.Missions),

        ["spin"] = ScenarioInfo.Pinning(
            "A spin in hand and nothing else pending — rung 4.",
            FixtureSlice.DailyReward, FixtureSlice.FirstWeek, FixtureSlice.Missions, FixtureSlice.SpinWheel),

        ["spin-pending"] = ScenarioInfo.Pinning(
            "A spin that resolved while the player was away: paid already, and the reveal still owed.",
            FixtureSlice.DailyReward, FixtureSlice.FirstWeek, FixtureSlice.Missions, FixtureSlice.SpinWheel),

        // Rung 5. It pins the weekly event slice because a real player without an event would replace the
        // fixture's event.
        ["weekly-reward"] = ScenarioInfo.Pinning(
            "A weekly event finished and its reward still waiting — rung 5.",
            FixtureSlice.DailyReward, FixtureSlice.FirstWeek, FixtureSlice.Missions, FixtureSlice.SpinWheel, FixtureSlice.WeeklyEvent),

        // Rung 6 for the weekly event. It pins the tournament too, because a real season ending sooner would win
        // rung 6 instead.
        ["ending"] = ScenarioInfo.Pinning(
            "A weekly event about to close — rung 6.",
            FixtureSlice.DailyReward, FixtureSlice.FirstWeek, FixtureSlice.Missions, FixtureSlice.SpinWheel,
            FixtureSlice.Tournament, FixtureSlice.WeeklyEvent),

        // Rung 7. It pins the tournament too, because a real season ending soon would match rung 6.
        ["progress"] = ScenarioInfo.Pinning(
            "Nothing claimable; the nearest thing to done wins — rung 7.",
            FixtureSlice.DailyReward, FixtureSlice.FirstWeek, FixtureSlice.Missions, FixtureSlice.SpinWheel,
            FixtureSlice.Tournament, FixtureSlice.WeeklyEvent),

        // The empty states, including an empty HUD wallet and record. It pins every slice so that a real
        // player's history does not replace them.
        ["fresh"] = ScenarioInfo.PinningEverything(
            "A brand-new player. Nothing has happened yet — rung 7, and the empty states."),

        // It pins the weekly event so that a real claimable weekly reward cannot add a badge, a card and a Claim
        // button to a scenario where everything is done.
        ["claimed"] = ScenarioInfo.Pinning(
            "Everything done for today. The 'come back tomorrow' states.",
            FixtureSlice.DailyReward, FixtureSlice.FirstWeek, FixtureSlice.Missions, FixtureSlice.SpinWheel,
            FixtureSlice.WeeklyEvent),

        // It pins the weekly event because the scenario shows an event whose scoring has closed.
        ["expired"] = ScenarioInfo.Pinning(
            "A lapsed first-week day, a concluded season, a reward awaiting collection.",
            FixtureSlice.DailyReward, FixtureSlice.FirstWeek, FixtureSlice.Tournament, FixtureSlice.WeeklyEvent),

        // A daily reward schedule before its first activation and after its last. Both pin only the daily reward
        // slice because they show one surface's state and do not depend on the next-action rung.
        ["daily-closed"] = ScenarioInfo.Pinning(
            "A daily-reward schedule with no activation open: the streak stands and there is nothing to claim.",
            FixtureSlice.DailyReward),

        ["daily-ended"] = ScenarioInfo.Pinning(
            "A daily-reward schedule that has run out of activations. Nothing further unlocks.",
            FixtureSlice.DailyReward),

        // The daily reward cycle and the wheel sector table are both missing from the game config, so both
        // surfaces are Unavailable.
        ["unpublished"] = ScenarioInfo.Pinning(
            "Neither the daily-reward cycle nor the wheel's table published. Both surfaces unavailable.",
            FixtureSlice.DailyReward, FixtureSlice.SpinWheel),

        // These put every feature in one state and leave identity, wallet and record real, so a browser test can
        // detect a live session by the player's generated name.
        ["loading"] = ScenarioInfo.PinningEveryFeature(
            "Every surface still loading. Skeletons, and no next action but Play."),

        ["offline"] = ScenarioInfo.PinningEveryFeature(
            "Every surface stale behind a dropped connection."),

        ["error"] = ScenarioInfo.PinningEveryFeature(
            "Every surface failed, with a retry."),
    };

    /// <summary>
    /// The slices that <paramref name="scenario"/> pins. An unknown name pins nothing, so that a mistyped
    /// scenario never replaces the player's real state.
    /// </summary>
    public static IReadOnlySet<FixtureSlice> PinnedBy(string scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario))
            return NothingClaimed;

        return Scenarios.TryGetValue(scenario.Trim().ToLowerInvariant(), out ScenarioInfo? info)
            ? info.Pinned
            : NothingClaimed;
    }

    /// <summary>
    /// The pinned slice names in lower case, space-separated in <see cref="FixtureSlice"/> order, for tests to read from
    /// the page.
    /// </summary>
    public static string PinnedSliceNames(string scenario)
    {
        IReadOnlySet<FixtureSlice> pinned = PinnedBy(scenario);
        List<string> names = new List<string>();
        foreach (FixtureSlice slice in Enum.GetValues<FixtureSlice>())
        {
            if (pinned.Contains(slice))
                names.Add(slice.ToString().ToLowerInvariant());
        }
        return string.Join(" ", names);
    }

    private static readonly IReadOnlySet<FixtureSlice> NothingClaimed = new HashSet<FixtureSlice>();

    /// <summary>
    /// Builds a scenario's snapshot at <paramref name="elapsed"/> after the session started. An unknown name builds
    /// the default scenario.
    /// <para>
    /// Time is a parameter instead of a clock read, so countdowns run down in the app and tests can assert exact
    /// values.
    /// </para>
    /// </summary>
    public static MetaSnapshot Build(string scenario, TimeSpan elapsed)
    {
        string name = string.IsNullOrWhiteSpace(scenario) ? Default : scenario.Trim().ToLowerInvariant();

        return name switch
        {
            "first-week"        => FirstWeekUrgent(elapsed),
            "first-week-reward" => FirstWeekRewardOwed(elapsed),
            "missions"          => MissionReady(elapsed),
            "spin"              => SpinReady(elapsed),
            "spin-pending"      => SpinInterrupted(elapsed),
            "weekly-reward"     => WeeklyRewardOwed(elapsed),
            "ending"            => EndingSoon(elapsed),
            "progress"          => ProgressOnly(elapsed),
            "fresh"             => Fresh(elapsed),
            "claimed"           => AllClaimed(elapsed),
            "expired"           => Lapsed(elapsed),
            "daily-closed"      => DailyBetweenActivations(elapsed),
            "daily-ended"       => DailyScheduleRunOut(elapsed),
            "unpublished"       => NeitherTablePublished(elapsed),
            "loading"           => EveryFeatureIn(ActivityState.Loading),
            "offline"           => EveryFeatureIn(ActivityState.Offline),
            "error"             => EveryFeatureIn(ActivityState.Error),
            _                   => Populated(elapsed),
        };
    }

    /// <summary>
    /// Whether <paramref name="scenario"/> names a known scenario. <see cref="Build"/> uses the default for unknown
    /// names.
    /// </summary>
    public static bool IsKnown(string scenario) =>
        !string.IsNullOrWhiteSpace(scenario) && Scenarios.ContainsKey(scenario.Trim().ToLowerInvariant());

    // ---------------------------------------------------------------------------------------------------
    // The scenarios
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The approved Home design: a claimable daily reward, a first-week day in progress, partly done missions, a
    /// spin token, a running weekly event and an upcoming tournament.
    /// <para>
    /// The first-week day has more time left than in the Home mock. With the mock's time, rung 1 would fire and
    /// the Next-up card would show the first-week event instead of the daily reward that the mock shows. The
    /// "first-week" scenario covers that case.
    /// </para>
    /// </summary>
    private static MetaSnapshot Populated(TimeSpan elapsed) => new MetaSnapshot(
        Identity: Avery,
        Wallet:   new WalletView(24_350, 1_250, 8),
        DailyReward: new DailyRewardView(
            State:               ActivityState.Actionable,
            StreakDays:          6,
            HasClaimableReward:  true,
            UntilNextAvailable:  null,
            TodayReward:         RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 330), RewardViewItem.Of(CurrencyKind.SpinTokens, 1)),
            TomorrowReward:      RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 150)),
            Cycle:               SevenStepCycle,
            StepsClaimedInCycle: 6,
            SkipDayAvailable:    true,
            WillUseSkipDay:      false,
            WillResetStreak:     false),
        FirstWeek: FirstWeekRun(day: 4, progress: 1, left: Left(TimeSpan.FromHours(8), elapsed)),
        Missions:  MissionList(claimed: false, left: Left(TimeSpan.FromHours(6), elapsed)),
        SpinWheel: BaselineWheel(ActivityState.Actionable, spins: 1),
        WeeklyEvent: TableChallenge(points: 620, left: Left(TimeSpan.FromHours(62), elapsed)),
        Tournament:  RoyalCup(untilStart: Left(TimeSpan.FromHours(24), elapsed), remaining: null, hasClaimableReward: false),
        Shop:        FullShop(elapsed),
        Profile:     AveryProfile(ActivityState.Ready));

    /// <summary>
    /// Rung 1: a first-week day with an open goal that is ending soon.
    /// <para>
    /// The day is <see cref="ActivityState.Actionable"/>, so <see cref="FeatureOrdering"/> puts the first-week card
    /// first on the Events hub with the featured style, as in the approved Events mock.
    /// </para>
    /// </summary>
    private static MetaSnapshot FirstWeekUrgent(TimeSpan elapsed) => Populated(elapsed) with
    {
        DailyReward = ClaimedDaily(elapsed),
        FirstWeek   = FirstWeekRun(day: 4, progress: 1, left: Left(TimeSpan.FromMinutes(47), elapsed),
                                state: ActivityState.Actionable),
    };

    /// <summary>
    /// Rung 1 by claimable reward: rewards from earlier days not yet claimed, and today's goal still open without
    /// being close to its deadline.
    /// <para>
    /// The Home card, the Events card and the badge count <see cref="FirstWeekView.ClaimableDays"/> across all days,
    /// and this scenario exercises that. It also includes missed days, which the step rail draws differently from
    /// completed ones.
    /// </para>
    /// </summary>
    private static MetaSnapshot FirstWeekRewardOwed(TimeSpan elapsed) => Populated(elapsed) with
    {
        DailyReward = ClaimedDaily(elapsed),
        FirstWeek   = FirstWeekRun(day: 5, progress: 1, left: Left(TimeSpan.FromHours(8), elapsed),
                                state: ActivityState.Actionable, readyDay: 2, missedDay: 3),
    };

    /// <summary>Rung 3: a finished mission is the only claimable item.</summary>
    private static MetaSnapshot MissionReady(TimeSpan elapsed) => Populated(elapsed) with
    {
        DailyReward = ClaimedDaily(elapsed),
        FirstWeek   = FirstWeekRun(day: 4, progress: 2, left: Left(TimeSpan.FromHours(8), elapsed), claimed: true) with
                      {
                          State = ActivityState.Completed,
                      },
    };

    /// <summary>Rung 4: a spin token, with rungs 1 to 3 not matching.</summary>
    private static MetaSnapshot SpinReady(TimeSpan elapsed) => MissionReady(elapsed) with
    {
        Missions = MissionList(claimed: true, left: Left(TimeSpan.FromHours(6), elapsed)),
    };

    /// <summary>
    /// An interrupted spin: a resolved result the player has not seen.
    /// <para>
    /// The reward is already in the wallet, so only the reveal remains. The screen shows it before allowing
    /// another spin, so that an unseen result is not replaced (<c>docs/spin-wheel.md</c>).
    /// </para>
    /// </summary>
    private static MetaSnapshot SpinInterrupted(TimeSpan elapsed) => SpinReady(elapsed) with
    {
        SpinWheel = BaselineWheel(ActivityState.Actionable, spins: 1, pending: new SpinReceiptView(
            SectorIndex: 5,
            Reward:      RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 1_000)),
            Tier:        WheelTier.Rare)),
    };

    /// <summary>
    /// Rung 5: the weekly event target is reached, the reward is not claimed, and the event is still running.
    /// </summary>
    private static MetaSnapshot WeeklyRewardOwed(TimeSpan elapsed) => SpinReady(elapsed) with
    {
        SpinWheel   = BaselineWheel(ActivityState.Ready, spins: 0),
        WeeklyEvent = TableChallenge(points: 1_000, left: Left(TimeSpan.FromHours(62), elapsed)) with
                      {
                          State = ActivityState.Actionable, HasClaimableReward = true,
                      },
    };

    /// <summary>Rung 6: the weekly event is ending soon, and nothing is claimable.</summary>
    private static MetaSnapshot EndingSoon(TimeSpan elapsed) => SpinReady(elapsed) with
    {
        SpinWheel   = BaselineWheel(ActivityState.Ready, spins: 0),
        WeeklyEvent = TableChallenge(points: 620, left: Left(TimeSpan.FromHours(3), elapsed)),
    };

    /// <summary>Rung 7: nothing is claimable or ending soon, so the feature with the most progress wins.</summary>
    private static MetaSnapshot ProgressOnly(TimeSpan elapsed) => EndingSoon(elapsed) with
    {
        WeeklyEvent = TableChallenge(points: 620, left: Left(TimeSpan.FromHours(62), elapsed)),
        Tournament  = RoyalCup(untilStart: Left(TimeSpan.FromHours(72), elapsed), remaining: null, hasClaimableReward: false),
    };

    /// <summary>A brand-new player, showing the empty states.</summary>
    private static MetaSnapshot Fresh(TimeSpan elapsed) => new MetaSnapshot(
        Identity: Avery with { DisplayName = "Guest", Level = 1, CompetitionName = "", CompetitionRank = 0 },
        Wallet:   WalletView.Empty,
        DailyReward: new DailyRewardView(ActivityState.Actionable, 0, true, null,
                         RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 150)),
                         RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 170)),
                         SevenStepCycle, 0, true, false, false),
        FirstWeek: FirstWeekRun(day: 1, progress: 0, left: Left(TimeSpan.FromHours(23), elapsed)),
        Missions:  MissionList(claimed: false, left: Left(TimeSpan.FromHours(23), elapsed)) with
                   {
                       DailyMissions = Array.Empty<GoalView>(),
                       State         = ActivityState.Unavailable,
                   },
        SpinWheel:   BaselineWheel(ActivityState.Ready, spins: 0),
        WeeklyEvent: TableChallenge(points: 0, left: Left(TimeSpan.FromHours(62), elapsed)) with { State = ActivityState.Ready },
        Tournament: RoyalCup(untilStart: Left(TimeSpan.FromHours(24), elapsed), remaining: null, hasClaimableReward: false) with
                    {
                        State         = ActivityState.Unavailable,
                        IsInSeason    = false,
                        Rank          = 0,
                        Score         = 0,
                        ScoredMatches = 0,
                        PendingRank   = 0,
                    },
        Shop:       FullShop(elapsed),
        Profile:    AveryProfile(ActivityState.Ready) with
                    {
                        GamesPlayed = 0, GamesWon = 0, TricksWon = 0,
                        Identity = Avery with { DisplayName = "Guest", Level = 1, CompetitionName = "", CompetitionRank = 0 },
                    });

    /// <summary>
    /// Everything done for today. Each surface must explain its state instead of only disabling its controls.
    /// </summary>
    private static MetaSnapshot AllClaimed(TimeSpan elapsed) => Populated(elapsed) with
    {
        DailyReward = ClaimedDaily(elapsed),
        FirstWeek   = FirstWeekRun(day: 4, progress: 2, left: Left(TimeSpan.FromHours(8), elapsed), claimed: true) with
                      {
                          State = ActivityState.Completed,
                      },
        Missions  = MissionList(claimed: true, left: Left(TimeSpan.FromHours(6), elapsed)) with { State = ActivityState.Completed },
        SpinWheel = BaselineWheel(ActivityState.Ready, spins: 0),
    };

    /// <summary>Closed time windows, and an unclaimed tournament reward.</summary>
    private static MetaSnapshot Lapsed(TimeSpan elapsed) => Populated(elapsed) with
    {
        DailyReward = ClaimedDaily(elapsed),
        FirstWeek   = FirstWeekRun(day: 4, progress: 1, left: TimeSpan.Zero) with { State = ActivityState.Expired },
        // Completed instead of Expired, because WeeklyEventViewBuilder never returns Expired for real state.
        WeeklyEvent = TableChallenge(points: 620, left: TimeSpan.Zero) with { State = ActivityState.Completed, IsScoring = false },
        Tournament  = RoyalCup(untilStart: null, remaining: TimeSpan.Zero, hasClaimableReward: true) with { State = ActivityState.Completed },
    };

    /// <summary>
    /// A daily reward schedule with no open activation: today's reward has not started, as opposed to having been
    /// claimed. The streak and cycle are unchanged, and the countdown shows when the next activation opens.
    /// <para>
    /// This is the case that <c>DailyRewardRefusal.NoActivation</c> refuses, for example a schedule configured to
    /// start later. Neither the claimable nor the claimed card describes it, so it is
    /// <see cref="ActivityState.Ready"/>, and the cycle shows the step the next activation will offer.
    /// </para>
    /// </summary>
    private static MetaSnapshot DailyBetweenActivations(TimeSpan elapsed) => Populated(elapsed) with
    {
        DailyReward = ClaimedDaily(elapsed) with
        {
            State               = ActivityState.Ready,
            StreakDays          = 3,
            StepsClaimedInCycle = 3,
            TodayReward         = RewardView.Empty,
            TomorrowReward      = RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 210)),
        },
    };

    /// <summary>
    /// A daily reward schedule whose last activation has closed, with no further activation.
    /// <para>
    /// A recurring schedule can have a repeat count, and after the last repeat the SDK reports no further
    /// occasion. This is therefore <see cref="ActivityState.Expired"/>, not <see cref="ActivityState.Ready"/>, and
    /// shows no countdown (<c>docs/meta-shell.md</c>, "MetaSnapshot").
    /// </para>
    /// </summary>
    private static MetaSnapshot DailyScheduleRunOut(TimeSpan elapsed) => Populated(elapsed) with
    {
        DailyReward = ClaimedDaily(elapsed) with
        {
            State              = ActivityState.Expired,
            UntilNextAvailable = null,
            TodayReward        = RewardView.Empty,
            TomorrowReward     = RewardView.Empty,
        },
    };

    /// <summary>
    /// A game config without the daily reward cycle or the wheel sector table, so both surfaces are
    /// <see cref="ActivityState.Unavailable"/>.
    /// <para>
    /// The daily reward slice does not replace the fixture when its table is missing, so only this fixture shows
    /// the daily reward in this state. The wheel also has no spin tokens, because the badge and the card's button
    /// check for tokens without checking the wheel's state (<c>docs/spin-wheel.md</c>).
    /// </para>
    /// </summary>
    private static MetaSnapshot NeitherTablePublished(TimeSpan elapsed) => Populated(elapsed) with
    {
        DailyReward = new DailyRewardView(
            State:               ActivityState.Unavailable,
            StreakDays:          0,
            HasClaimableReward:  false,
            UntilNextAvailable:  null,
            TodayReward:         RewardView.Empty,
            TomorrowReward:      RewardView.Empty,
            Cycle:               Array.Empty<CycleStepView>(),
            StepsClaimedInCycle: 0,
            SkipDayAvailable:    false,
            WillUseSkipDay:      false,
            WillResetStreak:     false),
        SpinWheel = new SpinWheelView(
            State:          ActivityState.Unavailable,
            SpinsAvailable: 0,
            PrizeTeaser:    "",
            TopPrize:       RewardView.Empty),
    };

    /// <summary>The default scenario with every feature set to <paramref name="state"/>.</summary>
    private static MetaSnapshot EveryFeatureIn(ActivityState state)
    {
        MetaSnapshot populated = Populated(TimeSpan.Zero);
        return populated with
        {
            DailyReward = populated.DailyReward with { State = state },
            FirstWeek   = populated.FirstWeek   with { State = state },
            Missions    = populated.Missions    with { State = state },
            SpinWheel   = populated.SpinWheel   with { State = state },
            WeeklyEvent = populated.WeeklyEvent with { State = state },
            Tournament  = populated.Tournament  with { State = state },
            Shop        = populated.Shop        with { State = state },
            Profile     = populated.Profile     with { State = state },
        };
    }

    // ---------------------------------------------------------------------------------------------------
    // Building blocks shared by the scenarios
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The time left on a <paramref name="window"/> after <paramref name="elapsed"/>, or zero if it has passed.
    /// </summary>
    private static TimeSpan Left(TimeSpan window, TimeSpan elapsed)
    {
        TimeSpan left = window - elapsed;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>
    /// The daily reward cycle. The values match the shipped game config, so the fixture shows what a real player
    /// sees.
    /// </summary>
    private static readonly CycleStepView[] SevenStepCycle =
    {
        new CycleStepView(1, 150, false),
        new CycleStepView(2, 170, false),
        new CycleStepView(3, 190, false),
        new CycleStepView(4, 210, false),
        new CycleStepView(5, 240, false),
        new CycleStepView(6, 270, false),
        new CycleStepView(7, 330, true),
    };

    /// <summary>
    /// The wheel sectors, clockwise from the marker. The values match the shipped game config, so the fixture shows
    /// what a real player sees.
    /// </summary>
    private static readonly WheelSectorView[] WheelSectors =
    {
        new WheelSectorView(0, CurrencyKind.Coins,       100, WheelTier.Common),
        new WheelSectorView(1, CurrencyKind.Coins,       500, WheelTier.Uncommon),
        new WheelSectorView(2, CurrencyKind.Coins,       250, WheelTier.Common),
        new WheelSectorView(3, CurrencyKind.Gems,         30, WheelTier.Premium),
        new WheelSectorView(4, CurrencyKind.SpinTokens,    1, WheelTier.SpinAgain),
        new WheelSectorView(5, CurrencyKind.Coins,     1_000, WheelTier.Rare),
        new WheelSectorView(6, CurrencyKind.Coins,       250, WheelTier.Common),
        new WheelSectorView(7, CurrencyKind.SpinTokens,    1, WheelTier.SpinAgain),
        new WheelSectorView(8, null,                       0, WheelTier.Nothing),
        new WheelSectorView(9, CurrencyKind.Coins,       500, WheelTier.Uncommon),
    };

    /// <summary>
    /// The odds computed from <see cref="WheelSectors"/>, so the fixture's odds always match its wheel. The blank
    /// sector has no currency, so it is sorted last, as in the shared code's published odds.
    /// </summary>
    private static readonly WheelOddsRow[] WheelOdds = WheelSectors
        .GroupBy(sector => (sector.Currency, sector.Amount))
        .OrderBy(group => group.Key.Currency is CurrencyKind kind ? (int)kind : int.MaxValue)
        .ThenBy(group => group.Key.Amount)
        .Select(group => new WheelOddsRow(group.Key.Currency, group.Key.Amount, group.Count() * 100 / WheelSectors.Length))
        .ToArray();

    /// <summary>The fixture wheel with the given state, spin count and pending receipt.</summary>
    private static SpinWheelView BaselineWheel(ActivityState state, int spins, SpinReceiptView? pending = null) =>
        new SpinWheelView(
            State:          state,
            SpinsAvailable: spins,
            PrizeTeaser:    "Up to 1,000 coins or 30 gems",
            TopPrize:       RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 1_000)),
            Wheel:          WheelSectors,
            Odds:           WheelOdds,
            PendingReceipt: pending);

    private static readonly IdentityView Avery = new IdentityView(
        DisplayName:     "Avery",
        AvatarToken:     "avatar-crown",
        FrameToken:      "frame-sapphire",
        NameEffectToken: "name-prism",
        Level:           27,
        CompetitionName: "Seasonal Tournament",
        CompetitionRank: 4);

    private static DailyRewardView ClaimedDaily(TimeSpan elapsed) => new DailyRewardView(
        State:               ActivityState.Completed,
        StreakDays:          7,
        HasClaimableReward:  false,
        UntilNextAvailable:  Left(TimeSpan.FromHours(9), elapsed),
        TodayReward:         RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 330), RewardViewItem.Of(CurrencyKind.SpinTokens, 1)),
        TomorrowReward:      RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 150)),
        Cycle:               SevenStepCycle,
        StepsClaimedInCycle: 7,
        SkipDayAvailable:    true,
        WillUseSkipDay:      false,
        WillResetStreak:     false);

    /// <summary>
    /// The match count goal for each first-week day. These values and <see cref="FirstWeekReward"/> match the shipped
    /// game config, so the fixture shows what a real player sees.
    /// </summary>
    private static readonly int[] FirstWeekGoalTargets = { 1, 1, 2, 2, 2, 3, 1 };

    private static RewardView FirstWeekReward(int day) => day == 7
        ? RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 1_500), RewardViewItem.Of(CurrencyKind.Gems, 100), RewardViewItem.Of(CurrencyKind.SpinTokens, 2))
        : RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, new[] { 250, 300, 350, 400, 450, 500 }[day - 1]));

    /// <param name="progress">The number of matches completed toward the active day's goal.</param>
    /// <param name="claimed">Whether the active day's goal is complete and its reward claimed.</param>
    /// <param name="readyDay">An earlier day that is complete with an unclaimed reward, or 0 for none.</param>
    /// <param name="missedDay">An earlier day that ended with its goal not completed, or 0 for none.</param>
    /// <param name="state">The event's activity state. The approved Events design features it as
    /// <see cref="ActivityState.Actionable"/>.</param>
    private static FirstWeekView FirstWeekRun(int day, int progress, TimeSpan left, bool claimed = false,
                                           ActivityState state = ActivityState.InProgress,
                                           int readyDay = 0, int missedDay = 0)
    {
        int target        = FirstWeekGoalTargets[day - 1];
        int todayProgress = claimed ? target : progress;

        List<FirstWeekDayView> week = new List<FirstWeekDayView>();
        for (int index = 1; index <= 7; index++)
        {
            FirstWeekDayState dayState =
                index == readyDay  ? FirstWeekDayState.RewardReady
              : index == missedDay ? FirstWeekDayState.Missed
              : index <  day    ? FirstWeekDayState.Claimed
              : index == day    ? (claimed ? FirstWeekDayState.Claimed : FirstWeekDayState.InProgress)
              :                   FirstWeekDayState.Future;

            week.Add(new FirstWeekDayView(
                Day:      index,
                Title:    FirstWeekViewBuilder.TitleOf(FirstWeekGoalTargets[index - 1]),
                Progress: dayState == FirstWeekDayState.Missed ? 0
                        : index < day ? FirstWeekGoalTargets[index - 1] : index == day ? todayProgress : 0,
                Target:   FirstWeekGoalTargets[index - 1],
                Reward:   FirstWeekReward(index),
                State:    dayState,
                Id:       $"firstweek.v1.day{index}"));
        }

        return new FirstWeekView(
            State:      state,
            Title:      FirstWeekViewBuilder.Title,
            CurrentDay: day,
            TotalDays:  7,
            TodaysGoals: new[]
            {
                new GoalView(FirstWeekViewBuilder.TitleOf(target), todayProgress, target, FirstWeekReward(day), IsClaimed: claimed,
                    Id: $"firstweek.v1.day{day}"),
            },
            UntilDayExpires:       left,
            HasClaimableDayReward: false,
            DayReward:             FirstWeekReward(day),
            GrandPrize:            FirstWeekReward(7),
            Week:                  week);
    }

    /// <summary>
    /// The mission list. The missions match the shipped game config, so the fixture shows what a real player
    /// sees.
    /// </summary>
    private static MissionsView MissionList(bool claimed, TimeSpan left) => new MissionsView(
        State: ActivityState.InProgress,
        DailyMissions: new[]
        {
            new GoalView("Play 1 game",  1, 1, RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 100)), IsClaimed: true),
            new GoalView("Play 3 games", 2, 3, RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 150)), IsClaimed: false),
            new GoalView("Win 1 game",   1, 1, RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 150)), IsClaimed: claimed),
        },
        UntilDailyReset: left,
        Weekly: new[]
        {
            new GoalView("Play 10 games", 6, 10, RewardView.Of(RewardViewItem.Of(CurrencyKind.SpinTokens, 1)), IsClaimed: false),
            new GoalView("Win 3 games",   2, 3,  RewardView.Of(RewardViewItem.Of(CurrencyKind.SpinTokens, 1)), IsClaimed: false),
        },
        UntilWeeklyReset: left + TimeSpan.FromDays(3));

    private static WeeklyEventView TableChallenge(long points, TimeSpan left) => new WeeklyEventView(
        State:            ActivityState.InProgress,
        Theme:            "Weekly Table Challenge",
        Tagline:          "Compete. Climb. Win Big.",
        PhaseLabel:       "This week",
        Points:           points,
        TargetPoints:     1_000,
        UntilEnd:         left,
        Reward:           RewardView.Of(RewardViewItem.Of(CurrencyKind.Gems, 250), RewardViewItem.Item("Champion Frame", "frame")),
        IsNewlyAvailable: false,
        EventId:          "fixture-week");

    /// <summary>
    /// A tournament season. It is in preview when <paramref name="untilStart"/> has a value, and otherwise running
    /// with the player joined, part of the scored-match cap used and some milestones claimed. Bot rows are
    /// marked.
    /// </summary>
    private static TournamentView RoyalCup(TimeSpan? untilStart, TimeSpan? remaining, bool hasClaimableReward) => new TournamentView(
        State:         untilStart.HasValue ? ActivityState.Ready : ActivityState.InProgress,
        Name:          "Seasonal Tournament",
        PhaseLabel:    untilStart.HasValue ? "Starts soon" : "Season 12",
        SeasonNumber:  12,
        IsInSeason:    !untilStart.HasValue,
        UntilStart:    untilStart,
        UntilEnd:      remaining,
        Rank:          4,
        Score:         5,
        ScoredMatches: 6,
        MatchCap:      10,
        Standings: new[]
        {
            new StandingRow(1, "Luna",     8, 0, false, false, "avatar-ace",      "frame-gold",     "name-bloom"),
            new StandingRow(2, "RoyalAce", 7, 0, false, true,  "avatar-dice",     "frame-gold",     "name-amber"),
            new StandingRow(3, "Nova",     6, 0, false, false, "avatar-heart",    "frame-silver",   "name-azure"),
            new StandingRow(4, "Avery",    5, 0, true,  false, "avatar-crown",    "frame-sapphire", "name-prism"),
            new StandingRow(5, "Jade",     4, 0, false, true,  "avatar-spade",    "frame-bronze",   "name-emerald"),
        },
        Milestones: new[]
        {
            new TournamentMilestoneView(0,  1, RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 250)), IsReached: true,  IsClaimed: true),
            new TournamentMilestoneView(1,  3, RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 250)), IsReached: true,  IsClaimed: true),
            new TournamentMilestoneView(2,  7, RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 500)), IsReached: false, IsClaimed: false),
            new TournamentMilestoneView(3, 10, RewardView.Of(RewardViewItem.Of(CurrencyKind.SpinTokens, 1)), IsReached: false, IsClaimed: false),
        },
        PlacementBands: new[]
        {
            new PlacementBandView(1, RewardView.Of(
                RewardViewItem.Of(CurrencyKind.Coins, 1_500),
                RewardViewItem.Of(CurrencyKind.Gems, 50),
                RewardViewItem.Item("Tournament Champion", "frame"))),
            new PlacementBandView(3, RewardView.Of(
                RewardViewItem.Of(CurrencyKind.Coins, 1_000),
                RewardViewItem.Of(CurrencyKind.SpinTokens, 1))),
            new PlacementBandView(5, RewardView.Of(
                RewardViewItem.Of(CurrencyKind.Coins, 500))),
        },
        HasClaimableReward: hasClaimableReward,
        PendingRank:        hasClaimableReward ? 3 : 0,
        PendingReward:      hasClaimableReward
                               ? RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 1_000), RewardViewItem.Of(CurrencyKind.SpinTokens, 1))
                               : RewardView.Empty,
        HasMaterialChange: false);

    private static ShopView FullShop(TimeSpan elapsed) => new ShopView(
        State: ActivityState.Ready,
        Featured: new OfferView(
            Id:           "gem-booster",
            Title:        "Gem Booster Pack",
            Tagline:      "Power up your game.",
            Contents:     RewardView.Of(
                              RewardViewItem.Of(CurrencyKind.Gems, 1_250),
                              RewardViewItem.Of(CurrencyKind.Coins, 10_000),
                              RewardViewItem.Of(CurrencyKind.SpinTokens, 3)),
            Price:        OfferPrice.Demo("$4.99"),
            Remaining:    Left(TimeSpan.FromHours(62), elapsed),
            Availability: OfferAvailability.Available,
            LockReason:   "",
            BonusPercent: 40,
            IsTargeted:   true,
            IconId:       "chest-gems"),
        Catalogue: new[]
        {
            new OfferView("lucky-spin", "Lucky Spin Bundle", "Spin more, win more.",
                RewardView.Of(RewardViewItem.Of(CurrencyKind.SpinTokens, 5), RewardViewItem.Of(CurrencyKind.Coins, 2_000)),
                OfferPrice.In(CurrencyKind.Gems, 500), null, OfferAvailability.Available, "", null, false, "wheel"),

            new OfferView("coin-vault-fixture", "Coin Vault", "Fill your vault.",
                RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 25_000)),
                OfferPrice.Demo("$2.99"), null, OfferAvailability.Available, "", null, false, "vault"),

            new OfferView("starter-2", "Starter Pack II", "Next step. Bigger rewards.",
                RewardView.Of(
                    RewardViewItem.Of(CurrencyKind.Gems, 1_500),
                    RewardViewItem.Of(CurrencyKind.Coins, 15_000),
                    RewardViewItem.Of(CurrencyKind.SpinTokens, 5)),
                OfferPrice.Demo("$9.99"), null, OfferAvailability.Locked, "Unlocks after Starter Pack", null, false, "card-pack"),

            new OfferView("daily-deal", "Daily Deal Chest", "Great value every day.",
                RewardView.Of(RewardViewItem.Of(CurrencyKind.Gems, 300)),
                OfferPrice.In(CurrencyKind.Coins, 5_000), Left(TimeSpan.FromMinutes(872), elapsed),
                OfferAvailability.SoldOut, "", null, false, "chest-plum"),
        });

    private static ProfileView AveryProfile(ActivityState state) => new ProfileView(
        State:       state,
        Identity:    Avery,
        GamesPlayed: 42,
        GamesWon:    27,
        TricksWon:   118,
        // Uses the shipped catalogue ids and style tokens, so the fixture matches the live screen. The retired
        // frame exists only in the fixture, because the shipped catalogue has no retired item. The champion frame
        // is Locked, as a real player sees it until they win a season.
        //
        // Items are listed by slot, which sets the tab order. The equipped items match the Avery identity.
        Cosmetics: new CosmeticsView(
            State: state,
            Items: new[]
            {
                new CosmeticItem("avatar.crown",  CosmeticSlot.Avatar, "Crown",          "Sit at the head of the table.", CosmeticOwnership.Equipped,     CurrencyKind.Coins, 0,     "",                         "avatar-crown"),
                new CosmeticItem("avatar.jack",   CosmeticSlot.Avatar, "Jack of Clubs",  "One of the court.",             CosmeticOwnership.Owned,        CurrencyKind.Coins, 0,     "",                         "avatar-club"),
                new CosmeticItem("avatar.queen",  CosmeticSlot.Avatar, "Queen of Hearts","A favour, worn openly.",        CosmeticOwnership.Affordable,   CurrencyKind.Coins, 600,   "",                         "avatar-heart"),
                new CosmeticItem("avatar.trump",  CosmeticSlot.Avatar, "Trump Star",     "The trick is yours.",           CosmeticOwnership.Unaffordable, CurrencyKind.Gems,  400,   "",                         "avatar-trump"),

                new CosmeticItem("frame.sapphire", CosmeticSlot.Frame, "Sapphire Frame", "Prestige, elegant, timeless.", CosmeticOwnership.Equipped,     CurrencyKind.Coins, 0,     "",                         "frame-sapphire"),
                new CosmeticItem("frame.brass",    CosmeticSlot.Frame, "Brass Frame",    "Warm, worn, and yours.",       CosmeticOwnership.Owned,        CurrencyKind.Coins, 0,     "",                         "frame-bronze"),
                new CosmeticItem("frame.silver",   CosmeticSlot.Frame, "Silver Frame",   "Where everybody starts.",      CosmeticOwnership.Affordable,   CurrencyKind.Coins, 500,   "",                         "frame-silver"),
                new CosmeticItem("frame.emerald",  CosmeticSlot.Frame, "Emerald Frame",  "Quiet confidence.",            CosmeticOwnership.Unaffordable, CurrencyKind.Gems,  150,   "",                         "frame-emerald"),
                new CosmeticItem("frame.champion", CosmeticSlot.Frame, "Tournament Champion", "Top of the table.",       CosmeticOwnership.Locked,       CurrencyKind.Coins, 0,     "Win a tournament season",  "frame-gold"),
                new CosmeticItem("frame.retired",  CosmeticSlot.Frame, "Ruby Crown",     "No longer awarded.",           CosmeticOwnership.Unavailable,  CurrencyKind.Coins, 0,     "",                         "frame-ruby"),

                new CosmeticItem("name.shimmer",   CosmeticSlot.NameEffect, "Shimmer",   "Every colour at once.",        CosmeticOwnership.Equipped,     CurrencyKind.Gems,  0,     "",                         "name-prism"),
                new CosmeticItem("name.amber",     CosmeticSlot.NameEffect, "Amber Glow","Warm and steady.",             CosmeticOwnership.Owned,        CurrencyKind.Coins, 0,     "",                         "name-amber"),
                new CosmeticItem("name.emerald",   CosmeticSlot.NameEffect, "Emerald",   "Quietly sharp.",               CosmeticOwnership.Affordable,   CurrencyKind.Coins, 500,   "",                         "name-emerald"),
                new CosmeticItem("name.violet",    CosmeticSlot.NameEffect, "Violet",    "For the bold.",                CosmeticOwnership.Affordable,   CurrencyKind.Coins, 1_500, "",                         "name-violet"),
                new CosmeticItem("name.frost",     CosmeticSlot.NameEffect, "Frozen",    "Ice in the veins.",            CosmeticOwnership.Unaffordable, CurrencyKind.Gems,  600,   "",                         "name-frost"),
            },
            SelectedSlot: CosmeticSlot.Frame,
            HasUnacknowledgedAcquisition: false));
}
