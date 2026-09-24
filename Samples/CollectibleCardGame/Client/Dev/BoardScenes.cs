using Game.Client.Components.Board;
using Game.Client.Components.Heist;
using Game.Client.Services;
using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using System;
using System.Collections.Generic;

namespace Game.Client.Dev;

/// <summary>
/// One named board state, ready to draw: the model it was played out on, and the
/// <see cref="BoardViewState"/> the board renders from.
/// </summary>
public sealed class BoardScene
{
    public BoardScene(string name, string caption, MatchModel match, BoardViewState state)
    {
        Name    = name;
        Caption = caption;
        Match   = match;
        State   = state;
    }

    public string Name { get; }

    /// <summary> What this scene is showing, for the preview page's own corner label. </summary>
    public string Caption { get; }

    public MatchModel Match { get; }
    public BoardViewState State { get; }
}

/// <summary>
/// The board's states, built offline from the shared rules so a screenshot needs no server.
/// <para>
/// <b>Development only.</b> Nothing links to the page that draws these and nothing in the player loop calls
/// this class; <c>DevBoardPreview</c> is the one caller and refuses to run outside a local or offline
/// environment. It exists because the board is the sample's densest screen and iterating on how it
/// <em>looks</em> against a live server means playing a real match to a real mid-game state every time.
/// </para>
/// <para>
/// <b>Every scene is the real rules.</b> A scene is a seeded <see cref="Deal"/> followed by the same rule
/// actions the match actor commits, driven by the same <see cref="BotPolicy"/> the server seats — so the
/// board is drawn from a state the game could actually be in, not from a hand-written fake. A client process
/// holding the authority-side model for a dev page leaks nothing: the model never reaches another client, and
/// the hidden half is reached only through the shared accessors that own it.
/// </para>
/// <para>
/// Deterministic, and that is the point: a scene is a pure function of (its name, the content), so two
/// screenshots of the same scene differ only where the implementation changed.
/// </para>
/// </summary>
public static class BoardScenes
{
    public const string Mulligan = "mulligan";
    public const string MidTurn  = "midturn";
    public const string Inspect  = "inspect";
    public const string Heal     = "heal";
    public const string Sneaky   = "sneaky";
    public const string GuardSneaky = "guard-sneaky";
    public const string Peek     = "peek";
    public const string End      = "end";
    public const string Play     = "play";
    public const string Reveal   = "reveal";
    public const string Struck   = "struck";
    public const string Covered  = "covered";
    public const string CoveredWaiting = "covered-waiting";
    public const string CoveredReclaiming = "covered-reclaiming";
    public const string Heist    = "heist";
    public const string HeistTaken = "heist-taken";

    /// <summary> Every scene, in the order the screenshot fixture walks them. </summary>
    public static readonly string[] All =
    {
        Mulligan, MidTurn, Inspect, Heal, Sneaky, GuardSneaky, Peek, End, Play, Reveal, Struck, Covered,
        CoveredWaiting, CoveredReclaiming, Heist, HeistTaken,
    };

    /// <summary> The seat the preview is looking through, which is the one whose hand is drawn. </summary>
    const int LocalSeat = 0;

    /// <summary>
    /// A fixed start for the model's clock, so a scene's deadline stamps — and therefore the ring and the
    /// readout — are the same in every capture. A clock started off <c>MetaTime.Now</c> would draw a
    /// different ring every run and no two screenshots could be compared.
    /// </summary>
    static readonly MetaTime Epoch = MetaTime.FromDateTime(new DateTime(2026, 3, 1, 20, 0, 0, DateTimeKind.Utc));

    /// <summary>
    /// Deadlines in force, beats zero. A beat would hold the table shut at the moment of the capture — the
    /// board would draw its busy state rather than the state the scene is named for — while the deadlines
    /// are what the ring and the quiet readout are drawn from, so those stay real.
    /// </summary>
    static readonly MatchTimings Timings = new MatchTimings(
        mulliganDeadline:     MetaDuration.FromSeconds(30),
        turnDeadline:         MetaDuration.FromSeconds(60),
        turnReserveBank:      MetaDuration.FromSeconds(60),
        turnReserveExtension: MetaDuration.FromSeconds(15),
        effectChoiceDeadline: MetaDuration.FromSeconds(20));

    /// <summary> How many deals a scene tries before it keeps the best-scoring one it saw. </summary>
    const int SeedsToTry = 48;

    /// <summary>
    /// The one card in the game that grants Sneaky in play, and therefore the only instrument the two scenes
    /// about a keyword arriving in play have. No Weather grants Sneaky, and the engine never asks where a
    /// keyword came from (<c>Docs/rules.md</c>), so the card that puts it there is the scene.
    /// </summary>
    static readonly CardId SmokeBomb = CardId.FromString("SmokeBomb");

    /// <summary> What a scene's scripted Smoke Bomb is aimed at. </summary>
    enum CloakWanted
    {
        /// <summary> Nothing: the policy plays every card, which is what every other scene wants. </summary>
        Nothing,

        /// <summary> Any critter, so the board carries Sneaky on a card whose own entry does not author it. </summary>
        AnyCritter,

        /// <summary> A Guard, so a Den stays open behind one. </summary>
        AGuard,
    }

    /// <summary> Whether this is a scene name the preview knows. </summary>
    public static bool IsKnown(string name) => Array.IndexOf(All, name) >= 0;

    /// <summary> Build one scene, or null when the name is not one of <see cref="All"/>. </summary>
    public static BoardScene? Build(string name, SharedGameConfig config)
    {
        switch (name)
        {
            case Mulligan: return BuildMulligan(config);
            case MidTurn:  return BuildMidTurn(config);
            case Inspect:  return BuildInspect(config);
            case Heal:     return BuildHeal(config);
            case Sneaky:   return BuildSneaky(config);
            case GuardSneaky: return BuildGuardSneaky(config);
            case Peek:     return BuildPeek(config);
            case End:      return BuildEnd(config);
            case Play:     return BuildPlay(config);
            case Reveal:   return BuildReveal(config);
            case Struck:   return BuildStruck(config);
            case Covered:  return BuildCovered(config);
            case CoveredWaiting: return BuildCoveredWaiting(config);
            case CoveredReclaiming: return BuildCoveredReclaiming(config);
            case Heist:    return BuildHeist(config);
            case HeistTaken: return BuildHeistTaken(config);
            default:       return null;
        }
    }

    /// <summary>
    /// What a posed beat moves. Numbers a real resolution could produce: every authored amount in the game
    /// is a multiple of the stat quantum with a floor of one quantum, so the old hard-coded 2 was a number
    /// the game cannot generate — misleading in exactly the tool a human uses to judge how a hit reads.
    /// A critter's worth of damage and three quanta at a 125-hit-point Den.
    /// </summary>
    const int PoseAmount    = 10;
    const int PoseDenAmount = 15;

    /// <summary>
    /// Hold one named resolution beat over a settled scene long enough for the browser fixture and a human
    /// reviewer to inspect its actual computed animation. The visual state deliberately remains the state
    /// before the event: <see cref="BoardPresentation"/> applies the event only after its beat finishes.
    /// </summary>
    public static bool PoseAnimation(BoardScene scene, string animation)
    {
        BoardViewState state = scene.State;
        BoardVisualSeat mine = state.Seat(state.LocalSeat);
        BoardVisualSeat opponent = state.Seat(state.OpponentSeat);
        MatchEvent? ev = animation switch
        {
            "play"       => CardPlayPose(state),
            "opponent-trick" => TrickPose(state),
            "attack"     => mine.Board.Count > 0
                                ? new AttackDeclaredEvent(
                                    mine.Board[0].Instance,
                                    opponent.Board.Count > 0
                                        ? EffectTargetRef.OnCritter(opponent.Board[0].Instance)
                                        : EffectTargetRef.Den(state.OpponentSeat))
                                : null,
            "damage"     => opponent.Board.Count > 0
                                ? new DamageDealtEvent(
                                    mine.Board.Count > 0 ? mine.Board[0].Instance : CardInstanceId.None,
                                    EffectTargetRef.OnCritter(opponent.Board[0].Instance),
                                    PoseAmount,
                                    absorbedByBubble: false)
                                : null,
            "heal"       => mine.Board.Count > 0
                                ? new HealedEvent(
                                    EffectTargetRef.OnCritter(mine.Board[0].Instance),
                                    PoseAmount,
                                    newValue: mine.Board[0].CurrentHealth)
                                : null,
            "den-damage" => new DenDamagedEvent(
                                state.OpponentSeat,
                                PoseDenAmount,
                                newHp: Math.Max(0, opponent.DenHp - PoseDenAmount)),
            "death"      => opponent.Board.Count > 0
                                ? new CritterDiedEvent(
                                    opponent.Board[opponent.Board.Count / 2].Instance,
                                    state.OpponentSeat,
                                    CritterDeathCause.LethalDamage)
                                : null,
            _             => null,
        };

        if (ev == null)
            return false;

        state.Beat = BoardBeat.FromEvent(
            ev,
            state.Visual.ActionCount,
            MetaDuration.FromSeconds(10),
            progress: 0,
            sequence: 1);
        state.IsBusy = true;
        state.TargetingState = TargetingLayer.TargetingKind.None;
        state.TargetableCritters.Clear();
        state.TargetableDens.Clear();
        return true;
    }

    static MatchEvent? TrickPose(BoardViewState state)
    {
        if (state.Config == null)
            return null;
        foreach (CardInfo card in state.Config.Cards.Values)
        {
            if (card.Type == CardType.Trick)
                return new CardPlayedEvent(state.OpponentSeat, new CardInstanceId(987654), card.CardId,
                    0, EffectTargetRef.None, 0);
        }
        return null;
    }

    static MatchEvent? CardPlayPose(BoardViewState state)
    {
        foreach (HandCard card in state.HandCards)
        {
            if (state.Config?.Cards.TryGetValue(card.Card, out CardInfo info) != true || info.Type != CardType.Critter)
                continue;

            return new CardPlayedEvent(
                state.LocalSeat,
                card.Instance,
                card.Card,
                card.Rank,
                EffectTargetRef.None,
                state.Rules == null || state.Config == null
                    ? 0
                    : ManaRules.CostToPlay(state.Config, state.Rules, state.LocalSeat, card));
        }

        return null;
    }

    // ---------------------------------------------------------------- the scenes

    static BoardScene BuildPlay(SharedGameConfig config)
    {
        Table table = FindDeal(config, candidate =>
        {
            if (FindPlayableCritter(candidate.Match) == null)
                return int.MinValue;
            // The aura Weather, so the critter that lands wears a keyword its own card does not author.
            return (candidate.Rules.Weather?.Ref.WeatherId.Value == "BubbleBath" ? 100 : 0)
                + candidate.Rules.Seat(LocalSeat).Board.Count;
        }, minTurn: 7, playsIntoOwnTurn: 0);
        return new BoardScene(Play, "play a critter · real rules and presentation", table.Match,
            table.BuildState(remainingOnClock: MetaDuration.FromSeconds(42)));
    }

    public static PlayCardIntent? FindPlayableCritter(MatchModel match)
    {
        List<HandCard> hand = SecretOps.HandOf(match, LocalSeat);
        foreach (MatchIntent intent in Legality.EnumerateForSeat(match, LocalSeat))
        {
            if (intent is not PlayCardIntent play)
                continue;
            foreach (HandCard card in hand)
            {
                if (card.Instance == play.Card && match.Content.Cards.TryGetValue(card.Card, out CardInfo info)
                    && info.Type == CardType.Critter)
                    return play;
            }
        }
        return null;
    }

    /// <summary> Commit a legal preview play using the same public action as the live match actor. </summary>
    public static bool PlayCritter(BoardScene scene)
    {
        PlayCardIntent? intent = FindPlayableCritter(scene.Match);
        if (intent == null)
            return false;
        if (!intent.Prepare(scene.Match, LocalSeat, out MatchAction action).IsSuccess)
            return false;

        return action.InvokeExecute(scene.Match, commit: true).IsSuccess;
    }

    /// <summary>
    /// The opening hand, uncovered, with two cards marked. The deal leaves the table here on its own: the
    /// Weather is drawn, both hands are dealt and the mulligan deadline is armed.
    /// </summary>
    static BoardScene BuildMulligan(SharedGameConfig config)
    {
        Table table = Table.Deal(config, seed: 7);

        BoardViewState state = table.BuildState(remainingOnClock: MetaDuration.FromSeconds(24));

        // Two marks, from the cards the mulligan is actually offering, so the scene marks exactly what a
        // player could have marked.
        foreach (CardInstanceId instance in state.ReplaceableCards)
        {
            if (state.MulliganMarks.Count >= 2)
                break;
            state.MulliganMarks.Add(instance);
        }

        return new BoardScene(Mulligan, "the mulligan · two cards marked", table.Match, state);
    }

    /// <summary>
    /// The pre-match reveal, over the mulligan it does not block. A <b>ranked</b> table rather than a practice
    /// one, because the wager is the whole content of the panel: two Power Scores, two lock sets and a tier
    /// that names a favourite. It is the one scene whose stakes are not
    /// <c>MatchStakes.Practice(...)</c>, which is also what makes it the cheapest way to look at the
    /// asymmetric copy without playing a ranked game.
    /// </summary>
    static BoardScene BuildReveal(SharedGameConfig config)
    {
        Table table = Table.Deal(config, seed: 7);

        // A human across the table, because a bot opponent forces the practice tier: a scene showing the
        // computer-player mark beside a favourite's wager would be a state the game cannot reach, and every
        // scene here is one it can.
        table.Match.Seats[1] = new MatchSeat(EntityId.None, "Pipwhistle", SeatOccupancy.Human, BotProfileId.Strongest)
        {
            IsConnected = true,
        };

        table.Match.Stakes = new MatchStakes(
            isRanked: true,
            StakesTier.Favourite,
            new List<int> { 74, 41 },
            new List<List<CardId>>
            {
                new List<CardId> { CardId.FromString("EmberKit"), CardId.FromString("GardenSnail") },
                new List<CardId> { CardId.FromString("WhiskerThief") },
            });

        BoardViewState state = table.BuildState(remainingOnClock: MetaDuration.FromSeconds(28));
        state.ShowsPreMatchReveal = true;

        return new BoardScene(Reveal, "the pre-match reveal · over the mulligan", table.Match, state);
    }

    /// <summary>
    /// Mid-turn with an attacker selected and its legal targets lit, against an enemy Den a Guard has
    /// closed. Played out by the bot to the first own turn past the opening, then stopped part-way through it
    /// so mana is partly spent and the hand still has something in it.
    /// </summary>
    static BoardScene BuildMidTurn(SharedGameConfig config)
    {
        Table table = FindDeal(config, ScoreMidTurn, minTurn: 11, playsIntoOwnTurn: 2);

        BoardViewState state = table.BuildState(remainingOnClock: MetaDuration.FromSeconds(42));
        SelectAnAttacker(table, state);
        PreviewTheAttack(table, state);

        return new BoardScene(MidTurn, "mid-turn · an attacker selected", table.Match, state);
    }

    /// <summary>
    /// One lapsed turn deadline, with the transient notice that says what the next one costs. The seat is
    /// still its owner's — one lapse is being slow — so the board is otherwise an ordinary mid-turn one.
    /// </summary>
    static BoardScene BuildStruck(SharedGameConfig config)
    {
        Table table = FindDeal(config, ScoreMidTurn, minTurn: 11, playsIntoOwnTurn: 2);
        table.SetSeatCover(LocalSeat, SeatOccupancy.Human, strikes: 1);

        BoardViewState state = table.BuildState(remainingOnClock: MetaDuration.FromSeconds(48));
        state.ShowsStrikeNotice = true;

        return new BoardScene(Struck, "one lapsed turn · the strike notice", table.Match, state);
    }

    /// <summary>
    /// The seat after the second lapse: a bot is playing it and its owner is still sitting in front of the
    /// board. Their own plaque carries the computer mark — the identity is kept, so the name does not change —
    /// and the notice says the way back in is playing anything. It is drawn on the seat's <em>own</em> turn,
    /// which is the half of the covered state where that instruction has something to press.
    /// </summary>
    static BoardScene BuildCovered(SharedGameConfig config)
    {
        Table table = FindDeal(config, ScoreMidTurn, minTurn: 11, playsIntoOwnTurn: 2);
        table.SetSeatCover(LocalSeat, SeatOccupancy.HumanCoveredByBot, strikes: 2);

        return new BoardScene(Covered, "a covered seat · its owner still watching", table.Match,
            table.BuildState(remainingOnClock: MetaDuration.FromSeconds(48)));
    }

    /// <summary>
    /// The same covered seat, one turn boundary later: the opponent is on turn, so there is nothing on the
    /// board for its owner to press. That is the half of the covered state the standing notice has to word
    /// differently — "play anything" is an instruction with no target for the whole of somebody else's turn —
    /// and it is where the cover actually lands, because the lapsed turn is played out before the seat goes.
    /// </summary>
    static BoardScene BuildCoveredWaiting(SharedGameConfig config)
    {
        Table table = FindDeal(config, ScoreMidTurn, minTurn: 11, playsIntoOwnTurn: 2);
        table.SetSeatCover(LocalSeat, SeatOccupancy.HumanCoveredByBot, strikes: 2);
        table.EndTheTurn();

        return new BoardScene(CoveredWaiting, "a covered seat · waiting out the opponent's turn", table.Match,
            table.BuildState(remainingOnClock: MetaDuration.FromSeconds(48)));
    }

    /// <summary>
    /// The covered seat after its owner pressed something: the seat is theirs from the next turn boundary, the
    /// bot keeps the turn it is playing, and the notice says so in place of the way back in.
    /// </summary>
    static BoardScene BuildCoveredReclaiming(SharedGameConfig config)
    {
        Table table = FindDeal(config, ScoreMidTurn, minTurn: 11, playsIntoOwnTurn: 2);
        table.SetSeatCover(LocalSeat, SeatOccupancy.HumanCoveredByBot, strikes: 0, reclaimPending: true);

        return new BoardScene(CoveredReclaiming, "a covered seat · its owner back from the next turn", table.Match,
            table.BuildState(remainingOnClock: MetaDuration.FromSeconds(48)));
    }

    /// <summary> The ordinary settled turn used to inspect cards without a targeting layer suppressing hover. </summary>
    static BoardScene BuildInspect(SharedGameConfig config)
    {
        Table table = FindDeal(config, ScoreMidTurn, minTurn: 11, playsIntoOwnTurn: 2);
        BoardViewState state = table.BuildState(remainingOnClock: MetaDuration.FromSeconds(42));
        return new BoardScene(Inspect, "mid-turn · card inspection", table.Match, state);
    }

    /// <summary>
    /// A Snack choosing its target, with the amount that would actually heal previewed on each one — the Den
    /// at full reading nothing. The board is driven the same way and then the heal card in hand is lifted.
    /// </summary>
    static BoardScene BuildHeal(SharedGameConfig config)
    {
        Table table = FindDeal(config, ScoreHeal, minTurn: 9, playsIntoOwnTurn: 1);

        BoardViewState state = table.BuildState(remainingOnClock: MetaDuration.FromSeconds(9));
        LiftAHealTrick(table, state);
        PreviewTheHeal(table, state);

        return new BoardScene(Heal, "a Snack choosing its target", table.Match, state);
    }

    /// <summary>
    /// A board where a Smoke Bomb has put Sneaky on a critter whose own card does not author it. This is the
    /// state that proves the live symbol follows the critter rather than the catalogue entry.
    /// </summary>
    static BoardScene BuildSneaky(SharedGameConfig config)
    {
        Table table = FindCloakedDeal(config, ScoreSneaky, CloakWanted.AnyCritter, minTurn: 7,
            "No legal position with a Smoke-Bombed critter was found in the scene deals.");
        BoardViewState state = table.BuildState(remainingOnClock: MetaDuration.FromSeconds(36));
        return new BoardScene(Sneaky, "Smoke Bomb · Sneaky granted in play", table.Match, state);
    }

    /// <summary> Stop at a legal position where a Smoke-Bombed Guard can no longer close its own Den. </summary>
    static BoardScene BuildGuardSneaky(SharedGameConfig config)
    {
        Table table = FindCloakedDeal(config, ScoreGuardSneaky, CloakWanted.AGuard, minTurn: 3,
            "No legal position with a Smoke-Bombed Guard was found in the scene deals.");
        return new BoardScene(GuardSneaky, "Sneaky Guard · Den remains open", table.Match,
            table.BuildState(remainingOnClock: MetaDuration.FromSeconds(36)));
    }

    /// <summary> The peek: a held resolution showing its owner the top of their own deck. </summary>
    static BoardScene BuildPeek(SharedGameConfig config)
    {
        Table table = FindDeal(
            config, ScorePeek, minTurn: 2, playsIntoOwnTurn: 24, SceneDecks.WithAPeek(config),
            "No seeded deal held a peek on the local seat.");

        return new BoardScene(Peek, "a held peek", table.Match, table.BuildState(MetaDuration.FromSeconds(18)));
    }

    /// <summary> The result panel, over a board the game has finished on. </summary>
    static BoardScene BuildEnd(SharedGameConfig config)
    {
        Table table = Table.Deal(config, seed: 7);
        table.ResolveMulligan();
        table.PlayToTheEnd();
        table.RecordResult();

        BoardViewState state = table.BuildState(remainingOnClock: null);
        state.ShowsResultPanel = true;

        return new BoardScene(End, "the result", table.Match, state);
    }

    /// <summary>
    /// The Heist screen from the <b>winner's</b> side, mid-pick: a ranked even matchup the local seat won, with
    /// every card the loser played on the lineup — one behind the loser's own padlock, one behind the winner's,
    /// and enough of them that the lineup has to scroll.
    /// <para>
    /// Everything the screen draws is real. The game is played out by the same policy the server seats, the
    /// menu is the shared subtraction over the loser's actual played list, and the phase and the pick clock
    /// arrive through the same two actions the actor publishes them with — so what the preview shows is a
    /// state the table can genuinely be in rather than a mock of one.
    /// </para>
    /// </summary>
    static BoardScene BuildHeist(SharedGameConfig config)
    {
        Table table = PoseHeist(config, winnerSeat: LocalSeat, phase: MatchTablePhase.HeistPick, out _);
        table.ArmHeistDeadline(LocalSeat, MetaDuration.FromSeconds(45));

        BoardViewState state = table.BuildState(remainingOnClock: MetaDuration.FromSeconds(31));
        state.ShowsResultPanel = true;

        // The board draws no ring of its own here, exactly as the live page does not: by the time the pick
        // clock is armed the ENGINE's phase is Complete, so MatchBoardPage.ActiveDeadlineAt is null and the
        // Heist screen reads the stamp itself. The posed clock stays, because the screen's own ring is drawn
        // from it.
        state.DeadlineAt = null;

        return new BoardScene(Heist, "the Heist · the winner's pick", table.Match, state);
    }

    /// <summary>
    /// The same screen from the <b>loser's</b> side, after the pick: what left their collection, in words, and
    /// the note that says the clock took it rather than a player.
    /// <para>
    /// It is a scene of its own because every relative thing on that screen swaps with the reader — whose
    /// padlock a row carries, whose copy the transfer names — and the one that is drawn for the side that
    /// cannot pick is the one nothing else exercises.
    /// </para>
    /// </summary>
    static BoardScene BuildHeistTaken(SharedGameConfig config)
    {
        Table table = PoseHeist(config, winnerSeat: MatchSeats.Other(LocalSeat), phase: MatchTablePhase.Ended, out _);

        BoardViewState state = table.BuildState(remainingOnClock: null);
        state.ShowsResultPanel = true;

        return new BoardScene(HeistTaken, "the Heist · what the loser is told", table.Match, state);
    }

    /// <summary> The rank the Heist scenes' loser holds their cards at, so a decrement has somewhere to go. </summary>
    const int HeistLoserRank = 3;

    /// <summary>
    /// A finished ranked table posed at the Heist: a real played-out game, both frozen sets chosen from what
    /// the loser actually played so that <em>both</em> padlock cases are on the lineup, the menu built by the
    /// shared rule, and — for a settled phase — one pick already taken by the deadline's own default.
    /// </summary>
    static Table PoseHeist(SharedGameConfig config, int winnerSeat, MatchTablePhase phase, out CardId taken)
    {
        int   loserSeat = MatchSeats.Other(winnerSeat);
        Table table     = FindHeistDeal(config, winnerSeat, loserSeat);

        // The wager cannot be posed until it is known which cards it is about. Two of the loser's played cards
        // go behind padlocks — one their own, one the winner's — because a lock runs both ways and the two read
        // differently on the two screens.
        List<HeistEligibleCard> played      = new List<HeistEligibleCard>(table.Rules.Result.PlayedBy(loserSeat));
        List<CardId>            loserLocks  = new List<CardId> { played[played.Count - 1].Card };
        List<CardId>            winnerLocks = new List<CardId> { played[played.Count - 2].Card };

        List<List<CardId>> locked = new List<List<CardId>> { null, null };
        locked[loserSeat]  = loserLocks;
        locked[winnerSeat] = winnerLocks;

        table.Match.Stakes = new MatchStakes(
            isRanked: true,
            StakesTier.Even,
            new List<int> { 25 * HeistLoserRank, 25 * HeistLoserRank },
            locked);

        List<List<HeistEligibleCard>> eligibility = MatchHeistPolicy.Eligibility(table.Rules.Result, table.Match.Stakes);

        // A settled scene takes what an absent winner would have taken, through the same rule the table uses.
        taken = phase == MatchTablePhase.HeistPick ? null : MatchHeistPolicy.AutoDefault(eligibility[loserSeat], config);

        HeistResult heist = taken == null
            ? null
            : new HeistResult(new List<CardId> { taken }, anyPickAutoDefaulted: true);

        table.RecordResult(wasRanked: true, eligibility, phase, heist);
        return table;
    }

    /// <summary>
    /// A played-out game one named seat won, with as much on the loser's side as the seeds offer. Seeded search
    /// rather than a pinned outcome, for the reason every other scene searches: which seat wins is a fact about
    /// a real game, so the honest way to get one is to find a game that has it. The <em>longest</em> lineup
    /// among them is kept, because a lineup that has to scroll is the case that broke.
    /// </summary>
    static Table FindHeistDeal(SharedGameConfig config, int winnerSeat, int loserSeat)
    {
        Table? best      = null;
        int    bestCount = 0;

        for (ulong seed = 1; seed <= SeedsToTry; seed++)
        {
            Table table = Table.Deal(
                config,
                seed,
                localDeck:    SceneDecks.ForSeat(config, 0, HeistLoserRank),
                opponentDeck: SceneDecks.ForSeat(config, 1, HeistLoserRank));

            table.ResolveMulligan();
            table.PlayToTheEnd();

            MatchResult result = table.Rules.Result;
            if (result == null || result.WinnerSeat != winnerSeat)
                continue;

            int played = result.PlayedBy(loserSeat).Count;
            if (played > bestCount)
            {
                best      = table;
                bestCount = played;
            }
        }

        // Four is what the two padlocks plus a pickable row need; the scenes want many more than that and the
        // seeds give them, but the floor is what makes the failure legible if the content ever changes.
        if (best == null || bestCount < 4)
            throw new InvalidOperationException($"No scene deal was found where seat {winnerSeat} wins with a lineup to draw (best was {bestCount}).");

        return best;
    }

    // ---------------------------------------------------------------- what makes a scene worth drawing

    /// <summary>
    /// Play out the deals in seed order and keep the one that scores highest for this scene. Seeded search
    /// rather than a hand-built board: what a scene wants — four critters a side, a Guard closing a Den, a
    /// Snack in hand with something hurt to put it on — is a fact about a real game, so the honest way to
    /// get it is to find a game that has it.
    /// <para>
    /// Scored rather than first-match, because this game's numbers are small and its mana ramps by one a
    /// turn: a full board is an ordinary mid-game state and a rare early one, so a
    /// predicate that either holds or does not would usually fall through to whatever the last seed left.
    /// </para>
    /// </summary>
    static Table FindDeal(
        SharedGameConfig config, Func<Table, int> score, int minTurn, int playsIntoOwnTurn,
        List<MatchDeckCard>? localDeck = null, string? whenNoneFound = null)
    {
        Table? best      = null;
        int    bestScore  = int.MinValue;

        for (ulong seed = 1; seed <= SeedsToTry; seed++)
        {
            Table table = Table.Deal(config, seed, localDeck);
            table.ResolveMulligan();
            table.PlayToOwnTurn(minTurn, playsIntoOwnTurn);

            int scored = score(table);
            if (scored > bestScore)
            {
                bestScore = scored;
                best      = table;
            }
        }

        if (best == null)
            throw new InvalidOperationException(whenNoneFound ?? "No seeded deal reached the scene's state.");

        return best;
    }

    /// <summary>
    /// The same seeded search with the scripted Smoke Bomb in the loop, and a hard failure rather than a
    /// best-effort table: a scene named for a keyword granted in play is worth nothing if the grant never
    /// happened, so it says so instead of quietly drawing an ordinary board.
    /// </summary>
    static Table FindCloakedDeal(
        SharedGameConfig config, Func<Table, int> score, CloakWanted wanted, int minTurn, string whenNoneFound)
    {
        Table? best      = null;
        int    bestScore = int.MinValue;

        for (ulong seed = 1; seed <= SeedsToTry; seed++)
        {
            Table table = Table.Deal(config, seed, SceneDecks.WithASmokeBomb(config));
            table.ResolveMulligan();
            table.PlayToOwnTurn(minTurn, playsIntoOwnTurn: 0, wanted);

            int scored = score(table);
            if (scored > bestScore)
            {
                bestScore = scored;
                best      = table;
            }
        }

        if (bestScore == int.MinValue)
            throw new InvalidOperationException(whenNoneFound);

        return best!;
    }

    /// <summary> Both rows populated, something of ours ready to swing, a Guard closing the enemy Den. </summary>
    static int ScoreMidTurn(Table table)
    {
        if (!table.IsOwnTurn || table.Rules.PendingChoice != null)
            return int.MinValue;

        SeatState mine  = table.Rules.Seat(LocalSeat);
        SeatState yours = table.Rules.Seat(MatchSeats.Other(LocalSeat));

        int score = 0;
        score += 10 * Math.Min(mine.Board.Count, 4);
        score += 10 * Math.Min(yours.Board.Count, 4);
        score += table.HasAttacker ? 40 : 0;
        score += KeywordRules.GuardGate(yours) ? 30 : 0;
        score += mine.Mana < mine.MaxMana ? 10 : 0;
        score += table.HandCount >= 3 ? 10 : 0;
        score += AnyDamaged(yours) ? 8 : 0;

        return score;
    }

    /// <summary> A Snack in hand with somewhere hurt to put it, and the Den at full so one preview reads +0. </summary>
    static int ScoreHeal(Table table)
    {
        if (!table.IsOwnTurn || table.Rules.PendingChoice != null || table.FindHealTrick() == null)
            return int.MinValue;

        SeatState mine = table.Rules.Seat(LocalSeat);

        int score = 100;
        score += 10 * Math.Min(mine.Board.Count, 4);
        score += AnyDamaged(mine) ? 40 : 0;
        score += mine.DenHp >= table.Match.Content.Global.DenStartingHp ? 20 : 0;
        score += 10 * Math.Min(table.Rules.Seat(MatchSeats.Other(LocalSeat)).Board.Count, 3);

        return score;
    }

    /// <summary> Prefer a populated table carrying Sneaky on a card that does not author it. </summary>
    static int ScoreSneaky(Table table)
    {
        if (!table.IsOwnTurn || table.DynamicSneakyCount == 0)
            return int.MinValue;

        return table.DynamicSneakyCount * 100 + Bodies(table) * 10;
    }

    /// <summary> A Guard whose own Sneaky has opened its Den, with as much else in play as the deal offers. </summary>
    static int ScoreGuardSneaky(Table table)
    {
        if (!table.IsOwnTurn || !table.HasSneakyGuard)
            return int.MinValue;

        return 100 + Bodies(table) * 10;
    }

    /// <summary> How many critters are in play across both rows. </summary>
    static int Bodies(Table table)
        => table.Rules.Seat(LocalSeat).Board.Count + table.Rules.Seat(MatchSeats.Other(LocalSeat)).Board.Count;

    /// <summary> A resolution held on the local seat, which is the only state the peek overlay is drawn in. </summary>
    static int ScorePeek(Table table)
    {
        PendingEffectChoice pending = table.Rules.PendingChoice;
        if (pending == null || pending.Seat != LocalSeat)
            return int.MinValue;

        return 100 + 10 * Math.Min(table.Rules.Seat(LocalSeat).Board.Count, 4);
    }

    static bool AnyDamaged(SeatState seat)
    {
        foreach (BoardCritter critter in seat.Board)
        {
            if (critter.Damage > 0)
                return true;
        }

        return false;
    }

    // ---------------------------------------------------------------- the local feedback a scene poses

    /// <summary> Select a ready attacker and light everything it may legally hit. </summary>
    static void SelectAnAttacker(Table table, BoardViewState state)
    {
        foreach (CardInstanceId attacker in state.Attackers)
        {
            state.SelectedAttacker = attacker;
            state.TargetingState   = TargetingLayer.TargetingKind.Attack;
            table.CollectTargets(state, forAttacker: attacker, forCard: null);
            return;
        }
    }

    /// <summary> Lift the heal trick in hand and light every target it may be aimed at. </summary>
    static void LiftAHealTrick(Table table, BoardViewState state)
    {
        CardInstanceId? heal = table.FindHealTrick();
        if (heal == null)
            return;

        state.SelectedCard   = heal;
        state.TargetingState = TargetingLayer.TargetingKind.Heal;
        table.CollectTargets(state, forAttacker: null, forCard: heal);
    }

    /// <summary>
    /// Put the amount that would actually heal on every legal target, and say so on the one already at full.
    /// The same <see cref="TargetPreview"/> the live board reads, so a capture shows what a player would see
    /// rather than what a scene made up.
    /// </summary>
    static void PreviewTheHeal(Table table, BoardViewState state)
    {
        if (!state.SelectedCard.HasValue)
            return;

        int amount = table.HealAmountOf(state.SelectedCard.Value);
        if (amount <= 0)
            return;

        SeatState mine = table.Rules.Seat(LocalSeat);

        foreach (CardInstanceId id in state.TargetableCritters)
        {
            BoardCritter critter = mine.FindCritter(id);
            if (critter != null)
            {
                state.CritterTags[id] = TargetPreview.CritterHeal(critter, amount);
                state.CritterDeltas[id] = TargetPreview.HealCritter(critter, amount);
                state.CritterTagAmounts[id] = state.CritterDeltas[id].Amount;
            }
        }

        foreach (int seat in state.TargetableDens)
        {
            state.DenTags[seat] = TargetPreview.DenHeal(table.Rules.Seat(seat).DenHp, state.MaxDenHp, amount);
            state.DenDeltas[seat] = TargetPreview.HealDen(table.Rules.Seat(seat).DenHp, state.MaxDenHp, amount);
            state.DenTagAmounts[seat] = state.DenDeltas[seat].Amount;
        }
    }

    /// <summary> The consequence of the selected attacker hitting each lit target. </summary>
    static void PreviewTheAttack(Table table, BoardViewState state)
    {
        if (!state.SelectedAttacker.HasValue)
            return;

        state.CritterTags = TargetPreview.Consequences(
            table.Rules, LocalSeat, state.SelectedAttacker.Value, state.TargetableCritters);
        state.CritterDeltas = TargetPreview.Attacks(
            table.Rules, LocalSeat, state.SelectedAttacker.Value, state.TargetableCritters);
        BoardCritter attacker = table.Rules.Seat(LocalSeat).FindCritter(state.SelectedAttacker.Value);
        foreach (int seat in state.TargetableDens)
            state.DenDeltas[seat] = TargetPreview.AttackDen(attacker, table.Rules.Seat(seat).DenHp);
    }

    // ---------------------------------------------------------------- the table

    /// <summary>
    /// One match, played locally. It is the actor's own loop with the actor's concerns left out: commit an
    /// action through the same validate-then-commit gate, and ask the same bot
    /// policy for the next move.
    /// </summary>
    sealed class Table
    {
        readonly MatchModel   _match;
        readonly BotPolicy    _policy;

        Table(MatchModel match, SharedGameConfig config)
        {
            _match  = match;
            _policy = BotPolicy.Strongest(config);
        }

        public MatchModel      Match => _match;
        public MatchRulesState Rules => _match.Rules;

        public bool IsOwnTurn => Rules.Phase == MatchPhase.Playing && Rules.SeatOnTurn == LocalSeat;

        /// <summary>
        /// Deal a game, the way the actor sets one up. <paramref name="localDeck"/> replaces the local seat's
        /// list, which is how the two scenes that need a Smoke Bomb in that hand get one, and
        /// <paramref name="opponentDeck"/> replaces the other seat's — which is how the Heist scene gets a
        /// loser holding cards above the rank floor, so the transfer it draws has something to say.
        /// </summary>
        public static Table Deal(SharedGameConfig config, ulong seed, List<MatchDeckCard>? localDeck = null, List<MatchDeckCard>? opponentDeck = null)
        {
            MatchModel match = new MatchModel
            {
                GameConfig       = config,
                Timings          = Timings,
                Stakes           = MatchStakes.Practice(
                                       new List<int> { 25, 25 },
                                       new List<List<CardId>> { new List<CardId>(), new List<CardId>() }),
                Phase            = MatchTablePhase.Playing,
                ResultAcked      = new List<bool> { true, true },
                HeistEligibility = new List<List<HeistEligibleCard>> { new List<HeistEligibleCard>(), new List<HeistEligibleCard>() },
                History          = new List<MatchEvent>(),
                Seats            = new List<MatchSeat>
                {
                    new MatchSeat(EntityId.None, "Thistle", SeatOccupancy.Human, BotProfileId.Strongest) { IsConnected = true },
                    new MatchSeat(EntityId.None, "Moppet", SeatOccupancy.Bot, BotProfileId.Strongest),
                },
            };

            ((IMultiplayerModel)match).ResetTime(Epoch);
            Game.Logic.Deal.Create(
                match,
                new MatchSetup(seed, config, Timings, localDeck ?? SceneDecks.ForSeat(config, 0), opponentDeck ?? SceneDecks.ForSeat(config, 1)),
                botSeed: seed);

            Table table = new Table(match, config);
            table.Settle();
            return table;
        }

        /// <summary>
        /// Both seats keep their dealt hands: a scene is about how the board looks, not about which cards went
        /// back.
        /// </summary>
        public void ResolveMulligan()
        {
            if (Rules.Phase == MatchPhase.Mulligan)
                Apply(new MatchMulliganResolve());

            Settle();
        }

        /// <summary>
        /// Play until the local seat is on turn at or past <paramref name="minTurn"/>, then take
        /// <paramref name="playsIntoOwnTurn"/> of that turn's <em>plays</em> and stop — so the capture is
        /// mid-turn, with mana partly spent and cards still in hand.
        /// <para>
        /// Plays only, never attacks. The policy attacks whenever it can, and an attack is what empties the
        /// board the capture is meant to show: at the point the game is worth drawing, taking the policy's
        /// attacks would leave two rows the previous turn had already cleared and no ready attacker to
        /// select. So the turn is walked forward by the moves that build a board and stopped before the ones
        /// that spend it.
        /// </para>
        /// <para>
        /// <paramref name="cloak"/> adds one scripted Smoke Bomb, cast as the last thing that happens and
        /// replacing the stop condition, for the two scenes that are about a keyword granted in play. It is
        /// cast last because Sneaky drops the moment a critter deals damage: a cloak laid down any earlier
        /// is one the policy's own next swing takes straight back off.
        /// </para>
        /// </summary>
        public void PlayToOwnTurn(int minTurn, int playsIntoOwnTurn, CloakWanted cloak = CloakWanted.Nothing)
        {
            for (int step = 0; step < 600; step++)
            {
                if (Rules.Phase != MatchPhase.Playing)
                    return;

                if (IsOwnTurn && Rules.Turn >= minTurn && (cloak == CloakWanted.Nothing || Cloak(cloak)))
                    break;

                if (!TakeOneMove())
                    return;
            }

            for (int play = 0; play < playsIntoOwnTurn; play++)
            {
                if (!IsOwnTurn || Rules.PendingChoice != null)
                    return;

                if (NextIntent() is not PlayCardIntent card || !Submit(ActingSeat, card))
                    return;
            }
        }

        /// <summary> Play the game out, however it goes. </summary>
        public void PlayToTheEnd()
        {
            for (int step = 0; step < 4000 && Rules.Phase != MatchPhase.Complete; step++)
            {
                if (!TakeOneMove())
                    return;
            }
        }

        /// <summary>
        /// Give the finished table the result record the actor would have written, and the phase it would have
        /// moved to. A stakeless table goes straight to <c>Ended</c> with nothing to steal; a ranked one whose
        /// tier owes a pick stops at <c>HeistPick</c> with the loser's menu on the model, which is the state
        /// the Heist screen is drawn from.
        /// </summary>
        public void RecordResult(
            bool wasRanked = false,
            List<List<HeistEligibleCard>>? eligibility = null,
            MatchTablePhase phase = MatchTablePhase.Ended,
            HeistResult? heist = null)
        {
            MatchResult result = Rules.Result;
            if (result == null)
                return;

            Apply(new MatchSetResult(
                new MatchOutcomeRecord(
                    result.Outcome,
                    result.WinnerSeat,
                    result.FinalTurn,
                    result.Cause,
                    new List<bool> { true, true },
                    wasRanked: wasRanked,
                    decidedAt: _match.CurrentTime)
                {
                    Heist = heist,
                },
                eligibility ?? new List<List<HeistEligibleCard>> { new List<HeistEligibleCard>(), new List<HeistEligibleCard>() }));

            Apply(new MatchSetPhase(phase));
        }

        /// <summary> Arm the winner's pick clock, through the action the actor arms it with. </summary>
        public void ArmHeistDeadline(int seat, MetaDuration remaining)
            => Apply(new MatchArmHeistDeadline(seat, remaining));

        /// <summary>
        /// Put one seat's roster entry where a lapsed turn deadline would have left it, through the same
        /// <see cref="MatchSetSeats"/> action the match actor publishes — so a scene's roster arrives on the
        /// timeline the way a live one does rather than being poked into the model.
        /// <para>
        /// The occupancy flip is spelled out here rather than taken from <c>MatchSeatPolicy.Cover</c>, and the
        /// reason is worth stating rather than implying: that policy lives in <c>Backend/Server</c> and a
        /// browser build cannot reference it. What this shares with the live path is the action and the shape
        /// it writes; the rule that decides to write it is the server's, and is tested there.
        /// </para>
        /// </summary>
        public void SetSeatCover(int seat, SeatOccupancy occupancy, int strikes, bool reclaimPending = false)
        {
            List<MatchSeat> seats = new List<MatchSeat>(_match.Seats.Count);
            foreach (MatchSeat entry in _match.Seats)
            {
                seats.Add(new MatchSeat(entry.PlayerId, entry.DisplayName, entry.Occupancy, entry.BotProfile)
                {
                    IsConnected    = entry.IsConnected,
                    Strikes        = entry.Strikes,
                    ReclaimPending = entry.ReclaimPending,
                });
            }

            seats[seat].Occupancy      = occupancy;
            seats[seat].Strikes        = strikes;
            seats[seat].ReclaimPending = reclaimPending;

            Apply(new MatchSetSeats(seats));
        }

        // ---- driving ----

        /// <summary> One move for whichever seat the table is waiting on, or false when it is waiting on nothing. </summary>
        bool TakeOneMove()
        {
            Settle();

            if (Rules.Phase != MatchPhase.Playing)
                return false;

            MatchIntent? intent = NextIntent();
            if (intent == null)
                return false;

            int seat = ActingSeat;
            if (Submit(seat, intent))
                return true;

            // A policy's move the rules would refuse still has to leave the game moving, exactly as it does
            // on the server: ending the turn is always legal for the seat on turn.
            return Submit(seat, new EndTurnIntent());
        }

        /// <summary>
        /// End whichever seat's turn the table is on, through the ordinary rule action. One scene wants the
        /// board it already has with the <em>other</em> seat on turn, which is a turn boundary away rather
        /// than a different deal.
        /// </summary>
        public bool EndTheTurn()
            => Rules.Phase == MatchPhase.Playing && Submit(Rules.SeatOnTurn, new EndTurnIntent());

        /// <summary> The seat the table is waiting on. </summary>
        int ActingSeat => Rules.PendingChoice != null ? Rules.PendingChoice.Seat : Rules.SeatOnTurn;

        /// <summary> What the policy would do for the seat the table is waiting on. </summary>
        MatchIntent? NextIntent()
        {
            int seat = ActingSeat;
            return _policy.ChooseAction(SeatView.Build(_match, seat), seat) ?? new EndTurnIntent();
        }

        /// <summary>
        /// Commit the action one intent becomes. The played card's identity comes off the acting seat's own
        /// hand view — the same payload the private channel delivers — so this needs no reach into the
        /// hidden half of the model.
        /// </summary>
        bool Submit(int seat, MatchIntent intent)
        {
            HandCard? played = null;

            if (intent is PlayCardIntent play)
            {
                played = FindInHand(seat, play.Card);
                if (played == null)
                    return false;
            }

            // A mulligan swap and a peek answer both move cards through the authority's secret. Scenes keep
            // the whole hand and keep nothing from a peek, so neither arises.
            if (intent is MulliganIntent || intent is EffectChoiceIntent)
                return false;

            bool applied = Apply(seat, intent);
            Settle();
            return applied;
        }

        /// <summary>
        /// Bring the table to a state a capture can be taken of: no resolution held on the opponent. A
        /// resolution held on the <em>local</em> seat is left alone — it is a scene of its own.
        /// </summary>
        void Settle()
        {
            for (int step = 0; step < 32; step++)
            {
                PendingEffectChoice pending = Rules.PendingChoice;
                if (pending != null && pending.Seat != LocalSeat)
                {
                    // Keeping nothing is a legal answer: the revealed cards go to the bottom of the deck.
                    // Said as an explicit empty intent rather than left to the action's own default, which
                    // would keep the costliest — a scene wants the settled board, not a card in a hand.
                    if (!Apply(pending.Seat, new EffectChoiceIntent(Rules.PendingChoice.Id, new List<int>())))
                        return;
                    continue;
                }

                return;
            }
        }

        /// <summary> Offer one host action, checked first, exactly as the actor does. </summary>
        bool Apply(MatchHostAction? action)
        {
            if (action == null || !action.ServerPrepare(_match).IsSuccess)
                return false;

            action.InvokeExecute(_match, commit: true);
            return true;
        }

        /// <summary> Offer one intent, prepared into its action first, exactly as the actor does. </summary>
        bool Apply(int seat, MatchIntent? intent)
        {
            if (intent == null)
                return false;

            if (!intent.Prepare(_match, seat, out MatchAction action).IsSuccess)
                return false;

            action.InvokeExecute(_match, commit: true);
            return true;
        }

        // ---- reading ----

        /// <summary> One card in a seat's hand, as that seat's own delivered hand names it. </summary>
        HandCard? FindInHand(int seat, CardInstanceId instance)
        {
            foreach (HandCard card in SecretOps.HandOf(_match, seat))
            {
                if (card.Instance == instance)
                    return card;
            }

            return null;
        }

        /// <summary> How many cards the local hand holds. </summary>
        public int HandCount => Rules.Seat(LocalSeat).HandCount;

        /// <summary> Whether anything of ours can still swing. </summary>
        public bool HasAttacker
        {
            get
            {
                foreach (MatchIntent intent in Legality.EnumerateForSeat(_match, LocalSeat))
                {
                    if (intent is AttackIntent)
                        return true;
                }

                return false;
            }
        }

        /// <summary> Whether any seat holds a Guard whose Sneaky has left its own Den open. </summary>
        public bool HasSneakyGuard
        {
            get
            {
                for (int seat = 0; seat < MatchSeats.Count; seat++)
                {
                    foreach (BoardCritter critter in Rules.Seat(seat).Board)
                    {
                        if ((critter.Keywords & KeywordFlags.Guard) != 0 && !KeywordRules.GatesTheDen(critter.Keywords))
                            return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Cast Smoke Bomb from the seat on turn onto one of its own critters: the first target the legality
        /// walk offers that has not got Sneaky already and, for <see cref="CloakWanted.AGuard"/>, is a Guard.
        /// Committed through the same validate-then-commit gate as every other move, so what the scene draws
        /// is a position the rules produced. False when this position offers no such cast.
        /// </summary>
        public bool Cloak(CloakWanted wanted)
        {
            if (wanted == CloakWanted.Nothing || Rules.Phase != MatchPhase.Playing || Rules.PendingChoice != null)
                return false;

            int       seat  = Rules.SeatOnTurn;
            SeatState state = Rules.Seat(seat);

            foreach (MatchIntent intent in Legality.EnumerateForSeat(_match, seat))
            {
                if (intent is not PlayCardIntent play || !play.Target.IsCritter)
                    continue;

                CardId? held = CardIdInHand(seat, play.Card);
                if (held == null || held != SmokeBomb)
                    continue;

                BoardCritter critter = state.FindCritter(play.Target.Critter);
                if (critter == null || (critter.Keywords & KeywordFlags.Sneaky) != 0)
                    continue;

                if (wanted == CloakWanted.AGuard && (critter.Keywords & KeywordFlags.Guard) == 0)
                    continue;

                return Submit(seat, play);
            }

            return false;
        }

        /// <summary> Which catalogue card an instance in a seat's hand is, or null when it is not in it. </summary>
        CardId? CardIdInHand(int seat, CardInstanceId instance)
        {
            foreach (HandCard card in SecretOps.HandOf(_match, seat))
            {
                if (card.Instance == instance)
                    return card.Card;
            }

            return null;
        }

        /// <summary> Critters whose current Sneaky flag did not come from their card definition. </summary>
        public int DynamicSneakyCount
        {
            get
            {
                int count = 0;
                for (int seat = 0; seat < MatchSeats.Count; seat++)
                {
                    foreach (BoardCritter critter in Rules.Seat(seat).Board)
                    {
                        CardInstance instance = Rules.Instance(critter.Id);
                        if (!critter.HasSneaky || !instance.IsKnown
                            || !_match.Content.Cards.TryGetValue(instance.CardId, out CardInfo info))
                            continue;

                        if ((info.GetKeywordFlags() & KeywordFlags.Sneaky) == 0)
                            count++;
                    }
                }

                return count;
            }
        }

        /// <summary> The Snack in the local hand that has somewhere legal to go, or null. </summary>
        public CardInstanceId? FindHealTrick()
        {
            foreach (MatchIntent intent in Legality.EnumerateForSeat(_match, LocalSeat))
            {
                if (intent is not PlayCardIntent play || play.Target.IsNone)
                    continue;

                CardInstance instance = Rules.TryGetInstance(play.Card);
                if (instance == null)
                    continue;

                foreach (HandCard card in SecretOps.HandOf(_match, LocalSeat))
                {
                    if (card.Instance != play.Card)
                        continue;

                    if (_match.Content.Cards.TryGetValue(card.Card, out CardInfo info) && info.IsSnack)
                        return play.Card;
                }
            }

            return null;
        }

        /// <summary> How much one card in this hand would restore, at the rank it is held at. </summary>
        public int HealAmountOf(CardInstanceId instance)
        {
            foreach (HandCard card in SecretOps.HandOf(_match, LocalSeat))
            {
                if (card.Instance == instance && _match.Content.Cards.TryGetValue(card.Card, out CardInfo info))
                    return TargetPreview.HealAmount(info, card.Rank);
            }

            return 0;
        }

        /// <summary> Light every target one selection may legally be aimed at, off the shared legality walk. </summary>
        public void CollectTargets(BoardViewState state, CardInstanceId? forAttacker, CardInstanceId? forCard)
        {
            foreach (MatchIntent intent in Legality.EnumerateForSeat(_match, LocalSeat))
            {
                EffectTargetRef target;

                if (intent is AttackIntent attack && forAttacker.HasValue && attack.Attacker == forAttacker.Value)
                    target = attack.Target;
                else if (intent is PlayCardIntent play && forCard.HasValue && play.Card == forCard.Value)
                    target = play.Target;
                else
                    continue;

                if (target.IsCritter)
                    state.TargetableCritters.Add(target.Critter);
                else if (target.IsDen && !state.TargetableDens.Contains(target.DenSeat))
                    state.TargetableDens.Add(target.DenSeat);
            }
        }

        /// <summary>
        /// The scene as the board draws it. The legality sets come from the same shared walk the live page
        /// reads, so what is haloed and what is inert is the game's answer rather than the scene's.
        /// </summary>
        public BoardViewState BuildState(MetaDuration? remainingOnClock)
        {
            List<HandCard> hand = SecretOps.HandOf(_match, LocalSeat);

            BoardViewState state = new BoardViewState
            {
                Rules          = Rules,
                IsPreview      = true,
                Visual         = BoardVisualState.FromRules(Rules),
                Config         = _match.Content,
                Weather        = Rules.Weather?.Ref,
                Stakes         = _match.Stakes,
                LocalSeat      = LocalSeat,
                OpponentSeat   = MatchSeats.Other(LocalSeat),
                LocalRoster    = _match.Seats[LocalSeat],
                OpponentRoster = _match.Seats[MatchSeats.Other(LocalSeat)],
                History        = _match.History,
                HandCards      = hand,
                PendingChoice  = HandViews.BuildPendingChoice(_match, LocalSeat),
                Result         = _match.Result,
                AllSeatsAcked  = true,
            };

            // The preview has no presentation clock, so the scrim and the seat's own answer are the same
            // fact here: a scene is a settled state rather than a moment mid-round-trip.
            bool isMulligan = Rules.Phase == MatchPhase.Mulligan && !Rules.Seat(LocalSeat).HasMulliganed;
            state.IsMulliganOpen    = isMulligan;
            state.CanAnswerMulligan = isMulligan;

            foreach (MatchIntent intent in Legality.EnumerateForSeat(_match, LocalSeat))
            {
                if (intent is PlayCardIntent play)
                    state.PlayableCards.Add(play.Card);
                else if (intent is AttackIntent attack)
                    state.Attackers.Add(attack.Attacker);
            }

            if (isMulligan)
            {
                foreach (HandCard card in hand)
                    state.ReplaceableCards.Add(card.Instance);
            }

            state.CanEndTurn       = IsOwnTurn && Rules.PendingChoice == null;
            state.HasSomethingLeft = state.PlayableCards.Count > 0 || state.Attackers.Count > 0;
            state.ActionsLeft      = state.PlayableCards.Count + state.Attackers.Count;
            state.MaxDenHp         = _match.Content.Global.DenStartingHp;
            state.OwnTurnNumber    = Rules.FirstSeat == LocalSeat
                                         ? (Rules.Turn + 1) / 2
                                         : Rules.Turn / 2;

            state.TurnIndicatorState = Rules.Result != null ? "over"
                : isMulligan ? "mulligan"
                : IsOwnTurn ? "yours" : "theirs";

            // The clock is posed rather than read: the scene names how much of the deadline is left and
            // "now" is derived back from the stamp, so every capture draws the same ring.
            state.DeadlineAt  = _match.Pacing.DeadlineAt;
            state.Now         = remainingOnClock.HasValue && _match.Pacing.DeadlineAt.HasValue
                                    ? _match.Pacing.DeadlineAt.Value - remainingOnClock.Value
                                    : _match.CurrentTime;
            state.RingAllowed = remainingOnClock.HasValue;
            state.Heist       = BuildHeistState(state.Now);

            return state;
        }

        /// <summary>
        /// The Heist screen's own state, or null for a scene that ends anywhere else. The same seam the live
        /// page fills, from the same shared functions — the lineup is the real subtraction over the real
        /// played list, and the routing is the one fact off the record.
        /// <para>
        /// The one thing a preview cannot read is a live collection, so it poses the ordinary case: an account
        /// that owns the card at rank two. Every account in this build owns every collectible card, so
        /// "unowned" is a branch the game cannot currently reach and would be a misleading thing to draw here.
        /// </para>
        /// </summary>
        HeistViewState? BuildHeistState(MetaTime now)
        {
            if (_match.Result == null || !HeistPresentation.ShowsHeistScreen(_match.Phase, _match.Result))
                return null;

            int          winnerSeat = _match.Result.WinnerSeat;
            int          loserSeat  = MatchSeats.Other(winnerSeat);
            int          picksOwed  = MatchHeistPolicy.PicksOwedForMatch(_match.Stakes, _match.Result);
            List<CardId> picks      = _match.Result.Heist?.Picks ?? new List<CardId>();
            GlobalConfig global     = _match.Content.Global;

            return new HeistViewState
            {
                Result           = _match.Result,
                Stakes           = _match.Stakes,
                Config           = _match.Content,
                IsWinner         = winnerSeat == LocalSeat,
                Stage            = HeistPresentation.Stage(_match.Phase, _match.Result, LocalSeat, picksOwed),
                PicksOwed        = picksOwed,
                Picks            = picks,
                Lineup           = HeistPresentation.Lineup(
                                       Rules.Result?.PlayedBy(loserSeat),
                                       _match.HeistEligibility?[loserSeat],
                                       picks,
                                       _match.Stakes.Locked(loserSeat),
                                       _match.Stakes.Locked(winnerSeat)),
                AnyAutoDefaulted = _match.Result.Heist?.AnyPickAutoDefaulted ?? false,
                DeadlineAt       = _match.Pacing.DeadlineAt,
                Now              = now,
                AllSeatsAcked    = true,
                MyMoveFor        = _ => MatchHeistPolicy.WinnerGain(owns: true, rank: 2, locked: false, global),
                MyRankFor        = _ => 3,
            };
        }
    }

    /// <summary>
    /// The two decks the scenes are played with: Wanderers plus two clans each, in canonical card order, so
    /// the two seats are not mirror images and both hold a Guard and a Snack.
    /// </summary>
    static class SceneDecks
    {
        public static List<MatchDeckCard> ForSeat(SharedGameConfig config, int seat, int? rank = null)
            => seat == 0
                ? Build(config, rank, "Wanderer", "Kitsune", "Tidepool")
                : Build(config, rank, "Wanderer", "Sunny", "Moonlight");

        /// <summary>
        /// The local seat's list with Moonlight in place of Tidepool and Kitsune moved behind it, so the hand
        /// can hold a Smoke Bomb. <see cref="Build"/> fills to <c>DeckSize</c> in clan order, so which Kitsune
        /// cards make the 25 changes too. The Wanderers stay first, which is what keeps the Garden Snail the
        /// Guard scene needs in the deck.
        /// </summary>
        public static List<MatchDeckCard> WithASmokeBomb(SharedGameConfig config)
            => Build(config, null, "Wanderer", "Moonlight", "Kitsune");

        /// <summary> Tidepool first, so the deck holds Pebble Collector, whose Hello opens a peek. </summary>
        public static List<MatchDeckCard> WithAPeek(SharedGameConfig config)
            => Build(config, null, "Tidepool", "Wanderer", "Kitsune");

        /// <summary>
        /// <paramref name="rank"/> is the rank every card is held at, defaulting to the floor. It is a deck
        /// list rather than a collection, so the rank rides each row — which is what the Heist's own rows
        /// carry, and why a scene can pose a loser who has grown their cards.
        /// </summary>
        static List<MatchDeckCard> Build(SharedGameConfig config, int? rank, params string[] clanOrder)
        {
            List<MatchDeckCard> deck = new List<MatchDeckCard>();

            foreach (string clan in clanOrder)
            {
                List<CardInfo> inClan = new List<CardInfo>();
                foreach (CardInfo card in config.Cards.Values)
                {
                    if (card.Collectible && card.Clan.Ref.ClanId.Value == clan)
                        inClan.Add(card);
                }

                inClan.Sort((a, b) => CardInfo.CompareCanonical(a.CardId, b.CardId));

                foreach (CardInfo card in inClan)
                {
                    if (deck.Count >= config.Global.DeckSize)
                        break;
                    deck.Add(new MatchDeckCard(card.CardId, rank ?? config.Global.RankMin));
                }
            }

            return deck;
        }
    }
}
