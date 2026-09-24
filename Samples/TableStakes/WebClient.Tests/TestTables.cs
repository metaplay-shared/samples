using Game.Logic;
using Game.Logic.Tests;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;

namespace WebClient.Tests;

/// <summary>
/// Builds real <see cref="MatchModel"/> tables in the states the render tests draw. No board is posed by hand: each
/// state is reached by running the server's deal, engine, turn-flow driver and bot policy
/// (<c>SharedCode/Match/MatchHost.cs</c>), so a test only asserts positions the rules can produce. The deal seed is
/// fixed, so a state is the same table on every build. Tables behind a <c>Lazy&lt;MatchModel&gt;</c> are built once
/// per run. Tables whose state depends on the clock, such as a move deadline about to lapse, are built on every
/// call, because a deadline stamped once would lapse before a later test reads it.
/// </summary>
public static class TestTables
{
    /// <summary>
    /// The viewer's seat. It is drawn at the bottom of the screen, and it is the only hand drawn face up.
    /// </summary>
    public const int ViewerSeat = 0;

    /// <summary>
    /// The opening deal: play has begun but no card has been played, because every bot's think delay is longer than
    /// any test run. The viewer's hand is delivered at play index zero. The starting leader is whichever seat the deal
    /// picks, bot or viewer.
    /// </summary>
    public static MatchModel OpeningDeal => _openingDeal.Value;

    /// <summary>
    /// Play has begun and the viewer is on turn with a full hand and a move deadline that does not lapse during the
    /// run.
    /// </summary>
    public static MatchModel ViewerOnTurn => _viewerOnTurn.Value;

    /// <summary>A trick in progress: the viewer has played a card and another seat is on turn.</summary>
    public static MatchModel MidTrick => _midTrick.Value;

    /// <summary>A game played to the end and won by the viewer, with standings for the results overlay.</summary>
    public static MatchModel FinishedViewerWon => _finishedViewerWon.Value;

    /// <summary>
    /// A game played to the end and lost by the viewer. See <see cref="BuildFinished"/> for how the deal is found.
    /// </summary>
    public static MatchModel FinishedViewerLost => _finishedViewerLost.Value;

    /// <summary>
    /// A table whose join window closed with no human arrived, so no game was played and there is no result.
    /// </summary>
    public static MatchModel Abandoned => _abandoned.Value;

    /// <summary>
    /// The viewer on turn with a move deadline far outside the deadline ring's closing window. The board reads the
    /// deadline, but the ring is not drawn and the hand still takes input.
    /// </summary>
    public static MatchModel BuildMoveDeadlineFarOut() => BuildOnTurnWithDeadline(MetaDuration.FromMinutes(10));

    /// <summary>The viewer on turn with a move deadline inside the deadline ring's closing window.</summary>
    public static MatchModel BuildMoveDeadlineClosing() => BuildOnTurnWithDeadline(MetaDuration.FromSeconds(6));

    private static readonly Lazy<MatchModel> _viewerOnTurn       = new Lazy<MatchModel>(BuildViewerOnTurn);
    private static readonly Lazy<MatchModel> _midTrick           = new Lazy<MatchModel>(BuildMidTrick);
    private static readonly Lazy<MatchModel> _finishedViewerWon  = new Lazy<MatchModel>(() => BuildFinished(viewerWins: true));
    private static readonly Lazy<MatchModel> _finishedViewerLost = new Lazy<MatchModel>(() => BuildFinished(viewerWins: false));
    private static readonly Lazy<MatchModel> _abandoned          = new Lazy<MatchModel>(BuildAbandoned);
    private static readonly Lazy<MatchModel> _openingDeal        = new Lazy<MatchModel>(BuildOpeningDeal);

    private static MatchModel BuildOpeningDeal()
    {
        MatchTimings timings = Timings(moveDeadline: PastAnyRun);

        // Everywhere else in this class the bot think delay is zero (Timings starts from MatchTimings.Instant), so
        // MatchHost.RunTable plays through bot turns without waiting. A long delay here makes the first RunTable
        // call begin play and then stop on the leading seat before any card is played. The E2E suite does the
        // same with a large botThinkMs.
        timings.BotThinkDelayMin = timings.BotThinkDelayMax = timings.BotThinkDelayOccasionalMax = MetaDuration.FromMinutes(10);

        MatchModel model = Deal(timings, numHumanSeats: 1, viewerHasArrived: true, otherHumansHaveArrived: true);

        RunTableUntil(model, until: current => current.PlayHasBegun, maxSteps: 2);

        ThrowUnlessTableReached(model.PlayHasBegun, "play has begun");
        ThrowUnlessTableReached(model.Board.Plays.Count == 0, "no card has been played yet");
        return DeliverViewerHand(model);
    }

    private static MatchModel BuildViewerOnTurn() => BuildOnTurnWithDeadline(PastAnyRun);

    /// <summary>
    /// The viewer on turn with the given move deadline. <see cref="ViewerOnTurn"/>,
    /// <see cref="BuildMoveDeadlineFarOut"/> and <see cref="BuildMoveDeadlineClosing"/> differ only in the deadline.
    /// <para>
    /// A table with a short deadline must be built on every call. The deadline is stamped at build time and the page
    /// compares it with the current time, so a table reused later in the run would show the ring drawn or the
    /// deadline lapsed. <see cref="ViewerOnTurn"/> is reused only because its deadline is <see cref="PastAnyRun"/>.
    /// </para>
    /// </summary>
    private static MatchModel BuildOnTurnWithDeadline(MetaDuration moveDeadline)
    {
        MatchModel model = Deal(
            Timings(moveDeadline: moveDeadline),
            numHumanSeats: 1,
            viewerHasArrived: true,
            otherHumansHaveArrived: true);

        RunTableUntil(model, until: ViewerIsOnTurn);
        ThrowUnlessTableReached(ViewerIsOnTurn(model), "play has begun and the viewer is on turn");
        return DeliverViewerHand(model);
    }

    private static MatchModel BuildMidTrick()
    {
        MatchModel model = Deal(
            Timings(moveDeadline: PastAnyRun),
            numHumanSeats: 1,
            viewerHasArrived: true,
            otherHumansHaveArrived: true);

        RunTableUntil(model, until: ViewerIsOnTurn);
        ThrowUnlessTableReached(ViewerIsOnTurn(model), "play has begun and the viewer is on turn");
        PlayForViewer(model);

        ThrowUnlessTableReached(model.PlayHasBegun && model.Board.Plays.Count > 0 && model.Board.SeatOnTurn != ViewerSeat,
               "the viewer's card is down and somebody else is on turn");
        return DeliverViewerHand(model);
    }

    /// <summary>
    /// Plays deals from <see cref="DealSeed"/> onward to the end and returns the first where the viewer ranks first
    /// (<paramref name="viewerWins"/> true) or does not (false). The winner of a deal depends on the engine and bot
    /// policy, so it is searched for rather than assumed. Throws <see cref="InvalidOperationException"/> when none of
    /// <see cref="NumDealsToSearch"/> deals qualifies.
    /// </summary>
    private static MatchModel BuildFinished(bool viewerWins)
    {
        for (ulong dealSeed = DealSeed; dealSeed < DealSeed + NumDealsToSearch; dealSeed++)
        {
            MatchModel model = Deal(MatchTimings.Instant, numHumanSeats: 1, viewerHasArrived: true,
                                    otherHumansHaveArrived: true, dealSeed: dealSeed);

            // Play every card, the viewer's included, so the standings are the ones the engine computed.
            for (int play = 0; play < MatchRules.NumPlays + MatchRules.NumTricks && model.Phase != MatchPhase.Ended; play++)
            {
                if (ViewerIsOnTurn(model))
                    PlayForViewer(model);
                else
                    RunTableUntil(model, until: ViewerIsOnTurn, maxSteps: 4);
            }

            ThrowUnlessTableReached(model.Phase == MatchPhase.Ended, "the game should have ended");
            ThrowUnlessTableReached(model.Board.Standings.Count == MatchRules.NumSeats, "a finished game should have standings");

            if ((model.Board.Standings[0].Seat == ViewerSeat) == viewerWins)
                return DeliverViewerHand(model);
        }

        throw new InvalidOperationException(
            $"TestTables played {NumDealsToSearch} deals and the viewer {(viewerWins ? "won" : "lost")} none of them.");
    }

    private static MatchModel BuildAbandoned()
    {
        // No human arrives before the join window closes, which is the only way a table becomes abandoned
        // (docs/match.md, "Phases").
        MatchModel model = Deal(MatchTimings.Instant, numHumanSeats: 1,
                                viewerHasArrived: false, otherHumansHaveArrived: false);

        RunTableUntil(model, until: current => current.Phase == MatchPhase.Abandoned, maxSteps: 4);

        ThrowUnlessTableReached(model.Phase == MatchPhase.Abandoned, "the table was abandoned");
        return model;
    }

    // -------------------------------------------------------------------------------------------------
    // Building and running a table

    /// <summary>
    /// Creates and deals a table through <c>MatchHost.SetupNewMatch</c>. The seeds are constants, so the deal, the bot
    /// names and every bot decision are the same on every run.
    /// </summary>
    private static MatchModel Deal(MatchTimings timings, int numHumanSeats, bool viewerHasArrived,
                                   bool otherHumansHaveArrived, ulong dealSeed = DealSeed)
    {
        MatchModel model = new MatchModel();

        // Set model time to now, as the offline host does (WebClient/Integration/TableStakesOfflineServer.cs), so
        // the deadlines stamped below lie in the future.
        ((IMultiplayerModel)model).ResetTime(MetaTime.Now);

        List<MatchSeat> seats = new List<MatchSeat>(MatchRules.NumSeats);
        for (int seat = 0; seat < MatchRules.NumSeats; seat++)
        {
            if (seat < numHumanSeats)
            {
                bool arrived = seat == ViewerSeat ? viewerHasArrived : otherHumansHaveArrived;
                seats.Add(new MatchSeat(seat, PlayerPublicIdentity.ForSeat(PlayerId(seat), seat == ViewerSeat ? "You" : "Robin"),
                                        MatchSeatOccupancy.Human, arrived));
            }
            else
            {
                seats.Add(new MatchSeat(seat, PlayerPublicIdentity.ForBot(EntityId.None, TestBotConfig.Names[seat - numHumanSeats], avatarId: null),
                                        MatchSeatOccupancy.Bot, hasArrived: true));
            }
        }

        MatchHost.SetupNewMatch(model, dealSeed, TableSeed, seats, timings, Profiles, MetaTime.Now);
        return model;
    }

    /// <summary>
    /// Calls <c>MatchHost.RunTable</c> until <paramref name="until"/> returns true, at most <paramref name="maxSteps"/>
    /// times.
    /// <para>
    /// The predicate takes the whole model rather than the board, because <c>SeatOnTurn</c> already names the leader
    /// before play begins. Only <c>MatchModel.PlayHasBegun</c> tells whether the game has started.
    /// </para>
    /// </summary>
    private static void RunTableUntil(MatchModel model, Func<MatchModel, bool> until, int maxSteps = 8)
    {
        MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();
        TestMatchHost       host           = new TestMatchHost(model, HostSeed);

        for (int step = 0; step < maxSteps && !until(model); step++)
            MatchHost.RunTable(model, pendingBotMove, MetaTime.Now, host);
    }

    /// <summary>
    /// Whether play is under way and the viewer owes a card. See <see cref="RunTableUntil"/> for why both.
    /// </summary>
    private static bool ViewerIsOnTurn(MatchModel model) =>
        model.PlayHasBegun && model.Board.SeatOnTurn == ViewerSeat;

    /// <summary>
    /// Plays a card for the viewer through <c>MatchHost.TryPlayForSeat</c>. The card is the bot policy's choice for
    /// the viewer's seat, which is always legal, so this helper needs no legality check of its own.
    /// </summary>
    private static void PlayForViewer(MatchModel model)
    {
        BotDecision decision = MatchHost.DecideBotMove(model, ViewerSeat, TableSeed);
        MatchHost.TryPlayForSeat(model, ViewerSeat, decision.Card, MetaTime.Now, new TestMatchHost(model, HostSeed));
    }

    /// <summary>
    /// Delivers the viewer's hand to the model the way the server does. The engine holds every hand server-side, and
    /// without a delivery the viewer's hand renders empty.
    /// </summary>
    private static MatchModel DeliverViewerHand(MatchModel model)
    {
        // TryDeliverOwnHand refuses a delivery stamped with a play index the board has not reached. Fail loudly
        // instead of returning a table with an empty viewer hand.
        ThrowUnlessTableReached(model.TryDeliverOwnHand(MatchHost.BuildHandDelivery(model, ViewerSeat).ToOwnHand()),
               "the viewer's own hand was delivered");
        return model;
    }

    private static MatchTimings Timings(MetaDuration moveDeadline)
    {
        MatchTimings timings = MatchTimings.Instant;
        timings.JoinWindow   = MetaDuration.Zero;
        timings.MoveDeadline = moveDeadline;
        timings.ResolvePause = MetaDuration.Zero;
        return timings;
    }

    /// <summary>
    /// Throws when a built table is not in the state its builder promises. A rules or driver change can make a state
    /// unreachable, and this makes the build fail instead of handing tests the wrong table.
    /// </summary>
    private static void ThrowUnlessTableReached(bool condition, string expectation)
    {
        if (!condition)
            throw new InvalidOperationException($"TestTables could not build a table where {expectation}.");
    }

    private static EntityId PlayerId(int seat) => EntityId.Create(EntityKindCore.Player, (ulong)(4100 + seat));

    /// <summary>
    /// The only bot profile dealt, so every bot plays at full strength and makes no deliberate mistakes. Repeatable
    /// results come from the fixed seeds, not from this list.
    /// </summary>
    private static readonly List<BotProfile> Profiles = new List<BotProfile> { BotProfiles.Strongest };

    /// <summary>
    /// The move deadline of a reused table.
    /// <para>
    /// A reused table may be read long after it was built, and the deadline is compared with the current time. The
    /// duration is longer than any test run, so a reused table stays in the same state for the whole run.
    /// </para>
    /// </summary>
    private static readonly MetaDuration PastAnyRun = MetaDuration.FromMinutes(600);

    private const ulong DealSeed  = 20260907UL;
    private const ulong TableSeed = 5UL;

    /// <summary>The seed of the host shell's random numbers, which the turn-flow driver draws from.</summary>
    private const ulong HostSeed  = 77UL;

    /// <summary>
    /// How many deals <see cref="BuildFinished"/> tries before throwing.
    /// </summary>
    private const ulong NumDealsToSearch = 60UL;
}

/// <summary>
/// Where the table draws each seat, written out independently of the code under test. Both the render tests and the
/// browser tests compute their expected seat positions here.
/// </summary>
public static class ExpectedSeatPositions
{
    /// <summary>
    /// The CSS screen position of seat <paramref name="seat"/> for a viewer in seat <paramref name="viewerSeat"/>.
    /// The viewer is always drawn at South, and seat indices run clockwise on screen from there.
    /// <para>
    /// The names and their order are written out here rather than read from <see cref="SeatRotation"/>, so a wrong
    /// rotation in the code under test fails the assertion instead of moving the expected value with it.
    /// </para>
    /// </summary>
    public static string ScreenPositionOfSeat(int seat, int viewerSeat) =>
        ClockwiseFromViewer[(seat - viewerSeat + 4) % 4];

    private static readonly string[] ClockwiseFromViewer = { "south", "west", "north", "east" };
}
