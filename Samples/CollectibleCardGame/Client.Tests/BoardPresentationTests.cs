using Game.Client.Services;
using Game.Logic;
using Metaplay.Core;

namespace Game.Client.Tests;

/// <summary>
/// The board's trailing rules: what is on screen while the presentation is behind the server, and what is
/// withheld until it has caught up.
/// <para>
/// Every rule here is one a live board could only be observed to get wrong, and each of them is a lie of a
/// specific kind: a ring draining for a turn the player has not been shown, a turn indicator inviting input
/// the board cannot honour, a result panel replacing the hit that won the match.
/// </para>
/// </summary>
[TestFixture]
public class BoardPresentationTests
{
    static readonly MetaTime Start = MetaTime.FromMillisecondsSinceEpoch(1_000_000);

    static AuthoritativeTime At(long offsetMs) => new AuthoritativeTime(Start + MetaDuration.FromMilliseconds(offsetMs));

    static MatchEvent Event(int seat) => new CardDrawnEvent(seat, handCount: 3, deckCount: 20);

    static EffectResolvedEvent EffectMarker()
        => new EffectResolvedEvent(EffectStepId.FromString("BurnDen1"), 0, CardInstanceId.None);

    static List<MatchEvent> Events(int count)
    {
        List<MatchEvent> events = new List<MatchEvent>(count);
        for (int ndx = 0; ndx < count; ndx++)
            events.Add(Event(0));

        return events;
    }

    static BoardPresentation Fresh(out BoardPresentationTuning tuning, long beatMs = 100)
    {
        tuning = new BoardPresentationTuning { BeatLength = MetaDuration.FromMilliseconds(beatMs) };
        return new BoardPresentation(tuning);
    }

    static BoardVisualState Visual(int actionCount, int hand = 3, int mana = 5, int denHp = 20)
        => new BoardVisualState(
            turn: 1,
            actionCount,
            MatchPhase.Playing,
            seatOnTurn: 0,
            new BoardVisualSeat(denHp, mana, maxMana: 5, hand, deckCount: 20, unseenCount: 10),
            new BoardVisualSeat(denHp: 20, mana: 5, maxMana: 5, handCount: 3, deckCount: 20, unseenCount: 10));

    [Test]
    public void AFreshBoardIsNotTrailing()
    {
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(31);

        Assert.That(presentation.IsTrailing, Is.False);
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(31));
        Assert.That(presentation.ShowsTurnIndicator, Is.True);
    }

    [Test]
    public void DenDamageReportAndHealthChangePresentOneImpactWithoutDroppingEitherEvent()
    {
        BoardPresentation presentation = Fresh(out _);
        int after = 31;
        presentation.SnapTo(Visual(30));
        DamageDealtEvent damage = new DamageDealtEvent(new CardInstanceId(4), EffectTargetRef.Den(0), 4, false);
        DenDamagedEvent den = new DenDamagedEvent(0, 4, 16);
        presentation.EnqueueStep(after, new MatchEvent[] { damage, den }, Visual(after, denHp: 16));

        presentation.Update(At(0));
        Assert.That(presentation.CurrentBeat!.Kind, Is.EqualTo(BoardBeatKind.DenDamage));
        Assert.That(presentation.CurrentBeat.Event, Is.SameAs(den));
        Assert.That(presentation.CurrentBeat.Amount, Is.EqualTo(4));
        Assert.That(presentation.CurrentBeat.Target.DenSeat, Is.Zero);
        Assert.That(presentation.CurrentBeat.Events, Is.EqualTo(new MatchEvent[] { damage, den }));
        Assert.That(presentation.PresentedHistory, Is.EqualTo(new MatchEvent[] { damage, den }));

        presentation.Update(At(100));
        Assert.That(presentation.CurrentBeat, Is.Null, "one attack on the Den must not schedule a second impact");
        Assert.That(presentation.IsTrailing, Is.False);
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(after));
        Assert.That(presentation.VisualState!.Seat(0).DenHp, Is.EqualTo(16));
    }

    [TestCase(1, 4)]
    [TestCase(0, 3)]
    public void UnrelatedDenDamageReportsRemainSeparate(int denSeat, int amount)
    {
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(Visual(30));
        presentation.EnqueueStep(31, new MatchEvent[]
        {
            new DamageDealtEvent(new CardInstanceId(4), EffectTargetRef.Den(0), 4, false),
            new DenDamagedEvent(denSeat, amount, 20 - amount),
        });

        presentation.Update(At(0));
        Assert.That(presentation.CurrentBeat!.Kind, Is.EqualTo(BoardBeatKind.Damage));
        Assert.That(presentation.CurrentBeat.Events.Count, Is.EqualTo(1));
        presentation.Update(At(100));
        Assert.That(presentation.CurrentBeat!.Kind, Is.EqualTo(BoardBeatKind.DenDamage));
        Assert.That(presentation.CurrentBeat.Events.Count, Is.EqualTo(1));
    }

    [Test]
    public void BubbleAbsorptionAndPopShareOneBeatAndOneBlockedImpact()
    {
        BoardPresentation presentation = Fresh(out _);
        CardInstanceId target = new CardInstanceId(9);
        BoardVisualState before = Visual(30);
        before.Seat(1).Board.Add(new BoardVisualCritter(target, null, 0, 1, 2, 4, 0,
            KeywordFlags.Bubble, false, false, true));
        presentation.SnapTo(before);
        DamageDealtEvent damage = new DamageDealtEvent(new CardInstanceId(4), EffectTargetRef.OnCritter(target), 3, true);
        BubblePoppedEvent pop = new BubblePoppedEvent(target);
        int after = 31;
        presentation.EnqueueStep(after, new MatchEvent[] { damage, pop });

        presentation.Update(At(0));
        Assert.That(presentation.CurrentBeat!.Kind, Is.EqualTo(BoardBeatKind.Damage));
        Assert.That(presentation.CurrentBeat.Event, Is.SameAs(damage));
        Assert.That(presentation.CurrentBeat.Events, Is.EqualTo(new MatchEvent[] { damage, pop }));
        List<BoardImpact> impacts = BoardImpact.FromEvents(presentation.CurrentBeat.Events);
        Assert.That(impacts, Has.Count.EqualTo(1));
        Assert.That(impacts[0].Kind, Is.EqualTo("blocked"));
        Assert.That(impacts[0].Amount, Is.Zero);
        presentation.Update(At(50));
        Assert.That(presentation.VisualState!.FindCritter(target)!.BubbleIntact, Is.True);
        Assert.That(presentation.VisualState.FindCritter(target)!.CurrentHealth, Is.EqualTo(4));

        presentation.Update(At(100));
        Assert.That(presentation.CurrentBeat, Is.Null, "the shield pop must not add an empty second beat");
        Assert.That(presentation.IsTrailing, Is.False);
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(after));
        Assert.That(presentation.VisualState!.FindCritter(target)!.BubbleIntact, Is.False);
        Assert.That(presentation.VisualState.FindCritter(target)!.CurrentHealth, Is.EqualTo(4));
        Assert.That(presentation.PresentedHistory, Is.EqualTo(new MatchEvent[] { damage, pop }));
    }

    [TestCase(false, 9)]
    [TestCase(true, 10)]
    public void UnrelatedBubblePopDoesNotMergeWithDamage(bool absorbed, int poppedInstance)
    {
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(Visual(30));
        presentation.EnqueueStep(31, new MatchEvent[]
        {
            new DamageDealtEvent(new CardInstanceId(4), EffectTargetRef.OnCritter(new CardInstanceId(9)), 3, absorbed),
            new BubblePoppedEvent(new CardInstanceId(poppedInstance)),
        });

        presentation.Update(At(0));
        Assert.That(presentation.CurrentBeat!.Events.Count, Is.EqualTo(1));
        presentation.Update(At(100));
        Assert.That(presentation.CurrentBeat!.Kind, Is.EqualTo(BoardBeatKind.Bubble));
        Assert.That(presentation.CurrentBeat.Events.Count, Is.EqualTo(1));
    }

    [Test]
    public void TrailingEffectMarkersShareTheVisibleBeatAndItsFinalSnapshot()
    {
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(Visual(30));
        int after = 31;
        MatchEvent[] events = { new DenDamagedEvent(0, 4, 16), EffectMarker(), EffectMarker() };
        presentation.EnqueueStep(after, events, Visual(after, denHp: 16));

        presentation.Update(At(0));
        Assert.That(presentation.CurrentBeat!.Kind, Is.EqualTo(BoardBeatKind.DenDamage));
        Assert.That(presentation.CurrentBeat.Duration.Milliseconds, Is.EqualTo(100));
        Assert.That(presentation.CurrentBeat.Events, Is.EqualTo(events));
        Assert.That(presentation.PresentedHistory, Is.EqualTo(events));
        Assert.That(presentation.VisualState!.Seat(0).DenHp, Is.EqualTo(20));
        presentation.Update(At(100));
        Assert.That(presentation.CurrentBeat, Is.Null);
        Assert.That(presentation.IsTrailing, Is.False);
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(after));
        Assert.That(presentation.VisualState!.Seat(0).DenHp, Is.EqualTo(16));
    }

    [Test]
    public void MarkerOnlyStepCommitsHistoryActionCountAndSnapshotImmediately()
    {
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(Visual(30));
        int after = 31;
        MatchEvent[] markers = { EffectMarker(), EffectMarker() };
        presentation.EnqueueStep(after, markers, Visual(after, mana: 2));

        presentation.Update(At(0));
        Assert.That(presentation.CurrentBeat, Is.Null);
        Assert.That(presentation.IsTrailing, Is.False);
        Assert.That(presentation.PresentedHistory, Is.EqualTo(markers));
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(after));
        Assert.That(presentation.VisualState!.ActionCount, Is.EqualTo(after));
        Assert.That(presentation.VisualState.Seat(0).Mana, Is.EqualTo(2));
    }

    [Test]
    public void LeadingEffectMarkerStartsFollowingVisibleBeatOnTheSameUpdate()
    {
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(Visual(30));
        int after = 31;
        DenDamagedEvent damage = new DenDamagedEvent(0, 4, 16);
        MatchEvent[] events = { EffectMarker(), damage };
        presentation.EnqueueStep(after, events, Visual(after, denHp: 16));

        presentation.Update(At(0));
        Assert.That(presentation.CurrentBeat!.Event, Is.SameAs(damage));
        Assert.That(presentation.CurrentBeat.Progress, Is.Zero);
        Assert.That(presentation.CurrentBeat.Duration.Milliseconds, Is.EqualTo(100));
        Assert.That(presentation.PresentedHistory, Is.EqualTo(events));
        presentation.Update(At(99));
        Assert.That(presentation.CurrentBeat!.Event, Is.SameAs(damage));
        Assert.That(presentation.VisualState!.Seat(0).DenHp, Is.EqualTo(20));
        presentation.Update(At(100));
        Assert.That(presentation.IsTrailing, Is.False);
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(after));
        Assert.That(presentation.VisualState!.Seat(0).DenHp, Is.EqualTo(16));
    }

    [Test]
    public void MarkerArrivingDuringVisibleBeatNeitherInterruptsNorAddsAWaitAtItsEnd()
    {
        BoardPresentation presentation = Fresh(out _);
        int before = 30;
        int damageStep = 31;
        int markerStep = 32;
        presentation.SnapTo(Visual(before));
        DenDamagedEvent damage = new DenDamagedEvent(0, 4, 16);
        EffectResolvedEvent marker = EffectMarker();
        presentation.EnqueueStep(damageStep, new MatchEvent[] { damage }, Visual(damageStep, denHp: 16));
        presentation.Update(At(0));
        int sequence = presentation.CurrentBeat!.Sequence;
        presentation.EnqueueStep(markerStep, new MatchEvent[] { marker }, Visual(markerStep, denHp: 16));

        presentation.Update(At(50));
        Assert.That(presentation.CurrentBeat!.Sequence, Is.EqualTo(sequence));
        Assert.That(presentation.CurrentBeat.Duration.Milliseconds, Is.EqualTo(100));
        Assert.That(presentation.CurrentBeat.Progress, Is.EqualTo(.5f));
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(before));
        Assert.That(presentation.PresentedHistory, Is.EqualTo(new MatchEvent[] { damage }));
        presentation.Update(At(100));
        Assert.That(presentation.CurrentBeat, Is.Null);
        Assert.That(presentation.IsTrailing, Is.False);
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(markerStep));
        Assert.That(presentation.VisualState!.ActionCount, Is.EqualTo(markerStep));
        Assert.That(presentation.VisualState.Seat(0).DenHp, Is.EqualTo(16));
        Assert.That(presentation.PresentedHistory, Is.EqualTo(new MatchEvent[] { damage, marker }));
    }

    [Test]
    public void ADecisionBurstCompressesWithoutDroppingEventsOrInterruptingTheCurrentBeat()
    {
        BoardPresentation presentation = new BoardPresentation(new BoardPresentationTuning());
        presentation.SnapTo(10);
        presentation.EnqueueStep(11, Events(1));
        presentation.Update(At(0));
        int sequence = presentation.CurrentBeat!.Sequence;
        presentation.EnqueueStep(20, Events(40));
        presentation.PrepareForDecision(At(100));
        presentation.Update(At(100));
        Assert.That(presentation.CurrentBeat!.Sequence, Is.EqualTo(sequence));
        int shown = 1;
        for (long time = 116; time < 3100 && presentation.IsTrailing; time += 16)
        {
            presentation.Update(At(time));
            if (presentation.CurrentBeat != null && presentation.CurrentBeat.Sequence != sequence)
            {
                shown++;
                sequence = presentation.CurrentBeat.Sequence;
            }
        }
        Assert.That(presentation.IsTrailing, Is.False);
        Assert.That(shown, Is.EqualTo(41));
        Assert.That(presentation.PresentedHistory.Count, Is.EqualTo(41));

        // Thinking/network silence does not leave an unfinished beat, and the next update starts fresh.
        presentation.Update(At(10000));
        presentation.EnqueueStep(21, Events(1));
        presentation.Update(At(10000));
        Assert.That(presentation.CurrentBeat!.Progress, Is.Zero);
        Assert.That(presentation.CurrentBeat.Duration, Is.EqualTo(MetaDuration.FromMilliseconds(560)));
    }

    [Test]
    public void APlayedRampKeepsItsEffectSeparateFromTheFlightCost()
    {
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(Visual(10));
        ManaChangedEvent ramp = new ManaChangedEvent(0, 3, 6);
        presentation.EnqueueStep(11, new MatchEvent[]
        {
            new CardPlayedEvent(0, new CardInstanceId(42), CardId.FromString("AcornHoard"), 1,
                EffectTargetRef.None, 2),
            new ManaChangedEvent(0, 3, 5),
            ramp,
        });
        presentation.Update(At(0));
        Assert.That(presentation.CurrentBeat!.Events.Count, Is.EqualTo(2));
        presentation.Update(At(101));
        Assert.That(presentation.RunningBeat, Is.SameAs(ramp));
    }

    [Test]
    public void QueuedTricksKeepAReadableRevealEvenDuringDecisionCatchUp()
    {
        BoardPresentation presentation = new BoardPresentation(new BoardPresentationTuning());
        presentation.SnapTo(Visual(10));
        List<MatchEvent> events = new List<MatchEvent>
        {
            new CardPlayedEvent(1, new CardInstanceId(42), CardId.FromString("AcornHoard"), 0,
                EffectTargetRef.None, 2),
        };
        events.AddRange(Events(40));
        presentation.EnqueueStep(11, events);
        presentation.PrepareForDecision(At(0));
        presentation.Update(At(0));
        Assert.That(presentation.CurrentBeat!.Duration.Milliseconds, Is.GreaterThanOrEqualTo(400));
        presentation.Update(At(200));
        Assert.That(presentation.CurrentBeat.Kind, Is.EqualTo(BoardBeatKind.CardPlay));
    }

    [Test]
    public void AQueuedStepMakesTheBoardTrailUntilItHasPlayedOut()
    {
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(10);

        presentation.EnqueueStep(11, Events(2));
        Assert.That(presentation.IsTrailing, Is.True);
        Assert.That(presentation.ShowsTurnIndicator, Is.False, "a trailing board must never invite input it cannot honour");

        presentation.Update(At(0));
        Assert.That(presentation.RunningBeat, Is.Not.Null);
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(10), "the board has not reached the new step yet");

        presentation.Update(At(100));
        presentation.Update(At(200));
        presentation.Update(At(300));

        Assert.That(presentation.IsTrailing, Is.False);
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(11));
    }

    [Test]
    public void AStepIsReachedOnlyAfterItsLastEvent()
    {
        BoardPresentation presentation = Fresh(out _);
        int before = 10;
        int after = 11;
        presentation.SnapTo(before);

        presentation.EnqueueStep(after, Events(2));
        presentation.Update(At(0));
        presentation.Update(At(100));

        Assert.That(presentation.PresentedActionCount, Is.EqualTo(before),
            "the step's action count is not presented before its last beat");

        presentation.Update(At(101));
        presentation.Update(At(201));

        Assert.That(presentation.PresentedActionCount, Is.EqualTo(after));
    }

    [Test]
    public void VisualStateChangesWhenItsBeatFinishesAndReconcilesAtTheStepBoundary()
    {
        BoardPresentation presentation = Fresh(out _);
        int before = 20;
        int after = 21;
        BoardVisualState initial = Visual(before, hand: 4, mana: 5);
        BoardVisualState authoritativeFinal = Visual(after, hand: 3, mana: 2, denHp: 19);
        presentation.SnapTo(initial);

        List<MatchEvent> events = new List<MatchEvent>
        {
            new CardPlayedEvent(0, new CardInstanceId(7), CardId.FromString("test"), rank: 2, EffectTargetRef.None, manaSpent: 3),
            new ManaChangedEvent(0, mana: 2, maxMana: 5),
        };
        presentation.EnqueueStep(after, events, authoritativeFinal);

        presentation.Update(At(0));
        Assert.That(presentation.VisualState!.Seat(0).HandCount, Is.EqualTo(4), "the card is still travelling during its beat");
        Assert.That(presentation.VisualState.Seat(0).Mana, Is.EqualTo(2), "spending accompanies the flight");
        Assert.That(presentation.VisualState.Seat(0).DenHp, Is.EqualTo(20), "future authoritative values must stay withheld");

        presentation.Update(At(100));
        Assert.That(presentation.VisualState!.Seat(0).HandCount, Is.EqualTo(3));
        Assert.That(presentation.VisualState!.Seat(0).Mana, Is.EqualTo(2));
        Assert.That(presentation.VisualState.Seat(0).DenHp, Is.EqualTo(19), "reconciliation repairs fields with no event of their own");
        Assert.That(presentation.VisualState.ActionCount, Is.EqualTo(after));
    }

    [Test]
    public void APlayCarriesItsConfirmedArrivalWithoutAnIdleResourceOrEntryBeat()
    {
        BoardPresentation presentation = Fresh(out _);
        int before = 20;
        int after = 21;
        CardInstanceId instance = new CardInstanceId(7);
        CardId card = CardId.FromString("test");
        presentation.SnapTo(Visual(before));
        List<MatchEvent> events = new List<MatchEvent>
        {
            new CardPlayedEvent(0, instance, card, 2, EffectTargetRef.None, 3),
            new ManaChangedEvent(0, 2, 5),
            new CritterEnteredPlayEvent(0, instance, card, 4, 6, KeywordFlags.Guard, true),
            new StatsChangedEvent(instance, 5, 7, 0),
            new DenDamagedEvent(1, 2, 18),
        };
        presentation.EnqueueStep(after, events);
        Assert.That(presentation.PresentedHistory, Is.Empty);
        presentation.Update(At(0));
        Assert.That(presentation.QueueLength, Is.EqualTo(1), "the meaningful damage still has its own beat");
        Assert.That(presentation.CurrentBeat!.EnteringCritter!.Attack, Is.EqualTo(5));
        Assert.That(presentation.CurrentBeat.EnteringCritter.IsSleepy, Is.True);
        Assert.That(presentation.CurrentBeat.EnteringCritter.Rank, Is.EqualTo(2));
        Assert.That(presentation.VisualState!.Seat(0).Board, Is.Empty, "one preview owns the travelling card");
        Assert.That(presentation.PresentedHistory.Count, Is.EqualTo(4));
        presentation.Update(At(100));
        Assert.That(presentation.VisualState.FindCritter(instance)!.Attack, Is.EqualTo(5));
        Assert.That(presentation.CurrentBeat!.Kind, Is.EqualTo(BoardBeatKind.DenDamage));
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(before));
        presentation.Update(At(200));
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(after));
    }

    [Test]
    public void ReducedMotionAppliesEveryEventEvenWithoutAFinalSnapshot()
    {
        BoardPresentation presentation = Fresh(out BoardPresentationTuning tuning);
        tuning.ReducedMotion = true;
        presentation.SnapTo(Visual(20));
        presentation.EnqueueStep(21, new MatchEvent[]
        {
            new ManaChangedEvent(0, 2, 5),
            new DenDamagedEvent(1, 2, 18),
        });
        presentation.Update(At(0));
        Assert.That(presentation.VisualState!.Seat(0).Mana, Is.EqualTo(2));
        Assert.That(presentation.VisualState.Seat(1).DenHp, Is.EqualTo(18));
        Assert.That(presentation.PresentedHistory.Count, Is.EqualTo(2));
    }

    [Test]
    public void SnappingHistoryRevealsTheSnapshotAndThenWithholdsQueuedFuture()
    {
        BoardPresentation presentation = Fresh(out BoardPresentationTuning tuning);
        presentation.SnapTo(Visual(20), Events(3));
        presentation.EnqueueStep(21, Events(2));
        Assert.That(presentation.PresentedHistory.Count, Is.EqualTo(3));
        presentation.Update(At(0));
        Assert.That(presentation.PresentedHistory.Count, Is.EqualTo(4));

        // However deep the queue gets, the future stays withheld: the queue compresses rather than
        // discarding, so a large step adds nothing to the presented history until its beats run.
        presentation.EnqueueStep(30, Events(21));
        Assert.That(presentation.PresentedHistory.Count, Is.EqualTo(4));
        Assert.That(presentation.QueueLength, Is.GreaterThan(20), "no bound discards a queued beat");
    }

    [Test]
    public void CurrentBeatDescribesMotionWithoutExposingHiddenCardIdentity()
    {
        BoardPresentation presentation = Fresh(out _, beatMs: 200);
        presentation.SnapTo(Visual(30));
        CardInstanceId attacker = new CardInstanceId(4);
        CardInstanceId defender = new CardInstanceId(9);
        presentation.EnqueueStep(
            31,
            new[] { new AttackDeclaredEvent(attacker, EffectTargetRef.OnCritter(defender)) },
            Visual(31));

        presentation.Update(At(0));
        presentation.Update(At(50));

        BoardBeat beat = presentation.CurrentBeat!;
        Assert.That(beat.Kind, Is.EqualTo(BoardBeatKind.Attack));
        Assert.That(beat.Source, Is.EqualTo(attacker));
        Assert.That(beat.Target, Is.EqualTo(EffectTargetRef.OnCritter(defender)));
        Assert.That(beat.AffectedInstances, Is.EqualTo(new[] { attacker }));
        Assert.That(beat.Progress, Is.EqualTo(0.25f).Within(0.001f));
        Assert.That(beat.RemainingDuration.Milliseconds, Is.EqualTo(150),
            "a delayed browser render must not replay the elapsed portion of the beat");
    }

    [Test]
    public void RetaliationDoesNotShakeTheReturningAttacker()
    {
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(Visual(30));
        CardInstanceId attacker = new CardInstanceId(4);
        CardInstanceId defender = new CardInstanceId(9);
        presentation.EnqueueStep(
            31,
            new MatchEvent[]
            {
                new AttackDeclaredEvent(attacker, EffectTargetRef.OnCritter(defender)),
                new DamageDealtEvent(attacker, EffectTargetRef.OnCritter(defender), 2, absorbedByBubble: false),
                new DamageDealtEvent(defender, EffectTargetRef.OnCritter(attacker), 1, absorbedByBubble: false),
            });

        presentation.Update(At(0));
        Assert.That(presentation.CurrentBeat!.SuppressImpact, Is.False);

        presentation.Update(At(100));
        Assert.That(presentation.CurrentBeat!.Target.Critter, Is.EqualTo(defender));
        Assert.That(presentation.CurrentBeat.SuppressImpact, Is.False, "the defender still shows the hit");

        presentation.Update(At(200));
        Assert.That(presentation.CurrentBeat!.Target.Critter, Is.EqualTo(attacker));
        Assert.That(presentation.CurrentBeat.SuppressImpact, Is.True,
            "the attacker's retaliation beat returns the lunging card instead of shaking it in place");
    }

    [Test]
    public void ConsecutiveSameKindBeatsHaveDistinctAnimationSequences()
    {
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(Visual(30));
        presentation.EnqueueStep(31, Events(2), Visual(31));

        presentation.Update(At(0));
        int firstSequence = presentation.CurrentBeat!.Sequence;

        presentation.Update(At(100));
        int secondSequence = presentation.CurrentBeat!.Sequence;

        Assert.That(secondSequence, Is.GreaterThan(firstSequence),
            "an affected element needs a new identity even when its CSS animation class did not change");
    }

    [Test]
    public void ReducedMotionReconcilesToTheAuthoritativeVisualStateInOneFrame()
    {
        BoardPresentation presentation = Fresh(out BoardPresentationTuning tuning);
        tuning.ReducedMotion = true;
        int before = 40;
        int after = 41;
        presentation.SnapTo(Visual(before, denHp: 20));
        presentation.EnqueueStep(
            after,
            new MatchEvent[] { new DenDamagedEvent(0, amount: 4, newHp: 16), new MatchEndedEvent(null!) },
            Visual(after, denHp: 16));

        presentation.Update(At(0));

        Assert.That(presentation.VisualState!.Seat(0).DenHp, Is.EqualTo(16));
        Assert.That(presentation.VisualState.ActionCount, Is.EqualTo(after));
        Assert.That(presentation.CurrentBeat, Is.Null);
        Assert.That(presentation.IsTrailing, Is.False);
    }

    [Test]
    public void RevealedRankAndPublicZoneFollowTheCardAcrossBeats()
    {
        BoardVisualState initial = Visual(50, hand: 4);
        CardInstanceId instance = new CardInstanceId(12);
        CardId card = CardId.FromString("ranked-critter");

        BoardVisualState played = BoardVisualReducer.Apply(
            initial,
            new CardPlayedEvent(0, instance, card, rank: 3, EffectTargetRef.None, manaSpent: 2));
        BoardVisualState entered = BoardVisualReducer.Apply(
            played,
            new CritterEnteredPlayEvent(0, instance, card, attack: 20, maxHealth: 30, KeywordFlags.Guard, isSleepy: true));

        Assert.That(entered.Seat(0).Board.Single().Rank, Is.EqualTo(3));
        Assert.That(entered.KnownCards[instance].Place, Is.EqualTo(CardPlace.Board));

        BoardVisualState died = BoardVisualReducer.Apply(
            entered,
            new CritterDiedEvent(instance, 0, CritterDeathCause.LethalDamage));

        Assert.That(died.Seat(0).Board, Is.Empty);
        Assert.That(died.Seat(0).Graveyard, Is.EqualTo(new[] { instance }));
        Assert.That(died.KnownCards[instance].Place, Is.EqualTo(CardPlace.Graveyard));
    }

    [Test]
    public void CountOnlyDrawBeatCarriesNoCardIdentity()
    {
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(Visual(60));
        presentation.EnqueueStep(
            70,
            new[] { new CardDrawnEvent(1, handCount: 4, deckCount: 19) },
            Visual(70));

        presentation.Update(At(0));

        BoardBeat beat = presentation.CurrentBeat!;
        Assert.That(beat.Kind, Is.EqualTo(BoardBeatKind.Draw));
        Assert.That(beat.Source, Is.EqualTo(CardInstanceId.None));
        Assert.That(beat.AffectedInstances, Is.Empty);
    }

    [Test]
    public void TheQueueCompressesAsItGrows()
    {
        // An opponent playing quickly can queue beats faster than they play them, so the beats get shorter
        // rather than the board falling further behind.
        BoardPresentation presentation = Fresh(out BoardPresentationTuning tuning, beatMs: 400);

        MetaDuration atRest = presentation.EffectiveBeatLength();

        presentation.EnqueueStep(11, Events(tuning.CompressAfter));
        MetaDuration underLoad = presentation.EffectiveBeatLength();

        Assert.That(underLoad, Is.LessThan(atRest));
        Assert.That(underLoad.Milliseconds, Is.EqualTo(atRest.Milliseconds / 2).Within(1));
    }

    [Test]
    public void ABeatWhoseWindowIsInThePastIsTreatedAsOverRatherThanAsWaiting()
    {
        // A reading from before the window began can only mean the clock moved after the window was stamped.
        // Ending an animation early is a dropped frame; failing to end it is a stuck board — and beats are
        // what withhold the terminal panel, so a beat that never ends is a match that never shows its result.
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(10);
        presentation.EnqueueStep(11, Events(1));

        presentation.Update(At(10_000));
        Assert.That(presentation.RunningBeat, Is.Not.Null);

        // The clock jumped backwards — a device correction, or an offset that just improved.
        presentation.Update(At(0));

        Assert.That(presentation.IsTrailing, Is.False);
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(11));
    }

    [Test]
    public void ReducedMotionCollapsesBeatsWithoutChangingTheDoctrine()
    {
        // State still only moves on a server update, and the terminal panel is still withheld until the
        // finish has been shown — even when showing it takes one frame.
        BoardPresentation presentation = Fresh(out BoardPresentationTuning tuning);
        tuning.ReducedMotion = true;

        presentation.SnapTo(10);
        presentation.EnqueueStep(20, Events(5));

        Assert.That(presentation.ShowsTerminalPanel(hasResult: true), Is.False, "the finish has not been shown yet");

        presentation.Update(At(0));

        Assert.That(presentation.IsTrailing, Is.False);
        Assert.That(presentation.PresentedActionCount, Is.EqualTo(20));
        Assert.That(presentation.ShowsTerminalPanel(hasResult: true), Is.True);
    }

    [Test]
    public void TheRingIsWithheldWhileTheBoardTrails()
    {
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(10);

        MetaTime deadline = Start + MetaDuration.FromSeconds(30);

        Assert.That(presentation.ShowsDeadlineRing(At(0), deadline), Is.True);

        presentation.EnqueueStep(11, Events(1));
        Assert.That(presentation.ShowsDeadlineRing(At(0), deadline), Is.False);
    }

    [Test]
    public void TheRingIsWithheldEntirelyWhenNoDeadlineIsInForce()
    {
        // "No deadline in force" is a state the board carries rather than a deadline of length zero: a ring
        // that drains to nothing and is then not acted on is a lie, so there is no ring at all and the board
        // keeps taking input.
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(10);

        Assert.That(presentation.ShowsDeadlineRing(At(0), null), Is.False);
    }

    [Test]
    public void TheRingIsWithheldOnceTheDeadlineHasLapsed()
    {
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(10);

        MetaTime deadline = Start + MetaDuration.FromSeconds(5);

        Assert.That(presentation.ShowsDeadlineRing(At(0), deadline), Is.True);
        Assert.That(presentation.ShowsDeadlineRing(At(5_000), deadline), Is.False);
        Assert.That(presentation.ShowsDeadlineRing(At(9_000), deadline), Is.False);
    }

    [Test]
    public void TheTerminalPanelIsWithheldUntilTheFinishHasPlayedOut()
    {
        // The lethal blow lands, the Den's hearts run out, the board settles — then the panel. It matters
        // more here than a results panel would: the Heist takes the whole window, so one
        // that arrived early would replace the finish rather than overlap it.
        BoardPresentation presentation = Fresh(out _);
        presentation.SnapTo(90);
        presentation.EnqueueStep(91, Events(3));

        Assert.That(presentation.ShowsTerminalPanel(hasResult: true), Is.False);

        presentation.Update(At(0));
        presentation.Update(At(100));
        presentation.Update(At(200));
        presentation.Update(At(300));
        presentation.Update(At(400));

        Assert.That(presentation.IsTrailing, Is.False);
        Assert.That(presentation.ShowsTerminalPanel(hasResult: true), Is.True);
        Assert.That(presentation.ShowsTerminalPanel(hasResult: false), Is.False);
    }
}
