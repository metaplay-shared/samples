using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Hand-built positions where the right action is obvious, pinned against the strictly deterministic
    /// profile so an intended change to the heuristic shows up as a diff rather than as a mood
    /// (<c>Docs/bots.md</c>, "Profiles, and the one pinned win rate"). The bulk suites prove the policy is legal and terminates;
    /// these are what say it is not stupid.
    /// </summary>
    [TestFixture]
    public class PolicyTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        static BotPolicy Policy(BotProfile profile = null, ulong seed = 0)
            => new BotPolicy(Config, profile ?? BotProfile.StrictlyDeterministic, seed);

        // ---------------------------------------------------------------- lethal

        [Test]
        public void ALethalSwingGoesAtTheDenRatherThanAtTheTrade()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(1).Board("MeadowMouse");
            scenario.Seat(0).Mana(0).DeckOf(4);
            scenario.Seat(1).Den(15).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            MatchIntent choice = Policy().ChooseAction(engine.BuildSeatView(0), 0);

            Assert.That(choice, Is.InstanceOf<AttackIntent>());
            Assert.That(((AttackIntent)choice).Attacker, Is.EqualTo(frog));
            Assert.That(((AttackIntent)choice).Target, Is.EqualTo(EffectTargetRef.Den(1)));
        }

        [Test]
        public void ALethalBurnIsCastAtTheDen()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId foxfire  = scenario.Seat(0).Hand("Foxfire");
            scenario.Seat(0).Mana(2).DeckOf(4);
            scenario.Seat(1).Den(15).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            MatchIntent choice = Policy().ChooseAction(engine.BuildSeatView(0), 0);

            Assert.That(choice, Is.InstanceOf<PlayCardIntent>());
            Assert.That(((PlayCardIntent)choice).Card, Is.EqualTo(foxfire));
            Assert.That(((PlayCardIntent)choice).Target, Is.EqualTo(EffectTargetRef.Den(1)));
        }

        [Test]
        public void AGuardInTheWayOfLethalIsClearedWithTheSmallestSwingThatDoesIt()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");  // 15/15, the smallest that clears
            scenario.Seat(0).Board("StrayGoat");                            // 20/15, kept for the Den
            CardInstanceId snail    = scenario.Seat(1).Board("GardenSnail"); // 0/15 Guard
            scenario.Seat(0).Mana(0).DeckOf(4);
            scenario.Seat(1).Den(20).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            MatchIntent choice = Policy().ChooseAction(engine.BuildSeatView(0), 0);

            Assert.That(choice, Is.InstanceOf<AttackIntent>());
            Assert.That(((AttackIntent)choice).Attacker, Is.EqualTo(frog), "the 4-attack body is what the Den needs");
            Assert.That(((AttackIntent)choice).Target, Is.EqualTo(EffectTargetRef.OnCritter(snail)));
        }

        [Test]
        public void AWeatherThatHealsOnDeathIsCountedAgainstTheLethal()
        {
            // Picnic Day heals a Den whenever its owner's critter dies, and that resolves before the Den is
            // checked — so every Guard the plan clears gives back some of what the swing is about to take. The
            // same board is lethal without it and is not lethal with it, and the two answers are visibly
            // different: the lethal plan opens with the smallest swing that clears the Guard, and the ordinary
            // scorer opens with the one that actually kills it.
            Assert.That(FirstAttackerAgainstAGuard("AcornRain"), Is.EqualTo("MeadowMouse"), "the lethal plan opens with its smallest swing");
            Assert.That(FirstAttackerAgainstAGuard("PicnicDay"), Is.EqualTo("PondFrog"), "one point of healing takes the lethal away");
        }

        /// <summary> Four Den hit points behind a Guard, and exactly four damage on the board to reach it with. </summary>
        static string FirstAttackerAgainstAGuard(string weatherId)
        {
            Scenario scenario = new Scenario(Config, weatherId);
            scenario.Seat(0).Board("MeadowMouse");
            scenario.Seat(0).Board("PondFrog");
            scenario.Seat(0).Board("StrayGoat");
            scenario.Seat(1).Board("GardenSnail");
            scenario.Seat(0).Mana(0).DeckOf(4);
            scenario.Seat(1).Den(20).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            AttackIntent choice = (AttackIntent)Policy().ChooseAction(engine.BuildSeatView(0), 0);
            return CardLookup.CardId(engine.Model, choice.Attacker).Value;
        }

        [Test]
        public void AGuardThatCannotBeClearedDoesNotBecomeALethalPlan()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Board("MeadowMouse");        // 5/10, nowhere near enough
            scenario.Seat(1).Board("OldMossback");        // 35/40 Guard
            scenario.Seat(0).Mana(0).DeckOf(4);
            scenario.Seat(1).Den(5).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();
            SeatView    view   = engine.BuildSeatView(0);

            // The swing at the wall is the only thing on offer besides ending the turn, so the choice is real:
            // a lethal check that believed a Den on one hit point was reachable would take it.
            Assert.That(view.LegalActions.Count, Is.EqualTo(2));

            Assert.That(Policy().ChooseAction(view, 0), Is.Null, "a hopeless swing is not a plan");
        }

        [Test]
        public void ARankTrackMovesWhereTheLethalThresholdIs()
        {
            // Foxfire sits on TrickAmount5: 15 damage at rank 1, 17 at rank 5. A policy that read the printed
            // number instead of the rank-track one would decline the rank-5 lethal and take the rank-1 one, so
            // the same Den hit points are asked twice with only the rank moved. The gap the question fits
            // through is two points wide rather than one now, which is the finer domain doing its job.
            Assert.That(LethalWithFoxfireAt(rank: 1, denHp: 16), Is.False, "fifteen damage does not finish sixteen hit points");
            Assert.That(LethalWithFoxfireAt(rank: 5, denHp: 16), Is.True, "the track's two extra points do");
        }

        /// <summary>
        /// Whether the policy burns the Den. A body worth far more than the trick's face damage sits beside
        /// it, so the scorer always prefers the body — which makes "it cast the trick at the Den" mean "the
        /// lethal branch fired" and nothing else.
        /// </summary>
        static bool LethalWithFoxfireAt(int rank, int denHp)
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId foxfire  = scenario.Seat(0).Hand("Foxfire", rank);
            scenario.Seat(0).Hand("PondFrog");
            scenario.Seat(0).Mana(5).DeckOf(4);
            scenario.Seat(1).Den(denHp).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            MatchIntent choice = Policy().ChooseAction(engine.BuildSeatView(0), 0);
            return choice is PlayCardIntent play && play.Card == foxfire && play.Target == EffectTargetRef.Den(1);
        }

        // ---------------------------------------------------------------- trades and the race

        [Test]
        public void AFreeFavourableTradeBeatsGoingFace()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId owl      = scenario.Seat(0).Board("GreyOwl");  // 15/30: kills a 15/15 and lives
            CardInstanceId frog     = scenario.Seat(1).Board("PondFrog"); // 15/15
            scenario.Seat(0).Mana(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            MatchIntent choice = Policy().ChooseAction(engine.BuildSeatView(0), 0);

            Assert.That(choice, Is.InstanceOf<AttackIntent>());
            Assert.That(((AttackIntent)choice).Attacker, Is.EqualTo(owl));
            Assert.That(((AttackIntent)choice).Target, Is.EqualTo(EffectTargetRef.OnCritter(frog)));
        }

        [Test]
        public void ATradeThatLosesTheBiggerBodyIsRefused()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Board("StrayGoat");   // 20/15, cost 4
            scenario.Seat(1).Board("PondFrog");    // 15/15, cost 3: kills the goat back
            scenario.Seat(0).Mana(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            MatchIntent choice = Policy().ChooseAction(engine.BuildSeatView(0), 0);

            Assert.That(choice, Is.InstanceOf<AttackIntent>());
            Assert.That(((AttackIntent)choice).Target, Is.EqualTo(EffectTargetRef.Den(1)), "the Den takes it instead");
        }

        [Test]
        public void TheTurnEndsRatherThanTakingAMoveWorthNothing()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("Undertow");   // an empty enemy board leaves only your own critter to bounce
            scenario.Seat(0).Board("PondFrog", hasAttacked: true);
            scenario.Seat(0).Mana(4).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();
            SeatView    view   = engine.BuildSeatView(0);

            // Affordable, legal, and the only thing besides ending the turn — so declining it is a judgement
            // rather than the absence of an option.
            Assert.That(view.LegalActions.Count, Is.EqualTo(2));

            Assert.That(Policy().ChooseAction(view, 0), Is.Null, "an unspent acorn beats a wasted card");
        }

        [Test]
        public void AGuardThatWillNotGateIsNotPaidForAsOne()
        {
            // A Sneaky Guard does not shut the Den, so under a Weather whose aura grants Sneaky a Guard
            // body buys none of the turn a Guard is bought for. The Sunbeam Retriever is a 10/15 with Guard
            // for three; the Stray Goat is a 20/15 vanilla for four. With the Den under a clock the Retriever
            // is the right play in clear weather and the wrong one under the aura, and a scorer that ORed the
            // aura in without re-asking whether the result gates would buy the stall either way.
            Assert.That(PreferredUnderWeather(null), Is.EqualTo("SunbeamRetriever"), "a Guard that gates is worth the body");
            Assert.That(PreferredUnderWeather(SneakyAura()), Is.EqualTo("StrayGoat"), "a Guard that cannot gate is just a 2/3");
        }

        /// <summary>
        /// A Weather granting Sneaky to every critter. No shipped Weather does, and the aura's arithmetic in
        /// the scorer is exactly what this case is about, so the row is synthetic rather than the case being
        /// dropped along with the content that used to supply it.
        /// </summary>
        static WeatherInfo SneakyAura()
            => new WeatherInfo(
                WeatherId.FromString("TestSneakyAura"), "Test Sneaky Aura",
                auraKeyword: MetaRef<KeywordInfo>.FromItem(Config.Keywords[KeywordId.FromString("Sneaky")]));

        /// <summary> Which of the two cards in hand the policy plays, with the Den under enough pressure to matter. </summary>
        static string PreferredUnderWeather(WeatherInfo weather)
        {
            Scenario scenario = new Scenario(Config).Weather(weather);
            scenario.Seat(0).Hand("SunbeamRetriever");
            scenario.Seat(0).Hand("StrayGoat");
            scenario.Seat(0).Den(30).Mana(4).DeckOf(4);
            scenario.Seat(1).Board("BoulderBoar");   // 30/25: the clock the Guard would answer
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            PlayCardIntent choice = (PlayCardIntent)Policy().ChooseAction(engine.BuildSeatView(0), 0);
            return CardLookup.CardId(engine.Model, choice.Card).Value;
        }

        // ---------------------------------------------------------------- the health-domain weights
        //
        // Every weight in BotPolicy.Scoring's health-domain block multiplies a quantity that grew five-fold
        // with F2's stat domain, and each has to be divided by the quantum or the policy is silently
        // re-tuned. F2's first pass missed seven of them and 525 tests noticed nothing, so these four cases
        // exist to make the block enforceable: each is a position whose decision *flips* if its weight goes
        // back to five times what it is. They were found by planting the old value and measuring, not by
        // deriving a score, and the planted-value answer is recorded beside each one.

        [Test]
        public void ASnacktimeSwingIsWorthItsLifegainAndNotFiveTimesIt()
        {
            // Biscuit Hound is a 15/15 Snacktime, awake, with a 125-hit-point Den across the table and a
            // 10/10 body in hand for the two mana available. The swing is worth its face damage plus a
            // lifegain kicker; the body is worth more than both. At SnacktimeFacePoint × 5 the kicker alone
            // outweighs the body and the bot swings instead — which is how it valued Snacktime for a whole
            // wave.
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Board("BiscuitHound");
            CardInstanceId beaver = scenario.Seat(0).Hand("BusyBeaver");
            scenario.Seat(0).Mana(2).DeckOf(4);
            scenario.Seat(1).Den(125).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            MatchIntent choice = Policy().ChooseAction(engine.BuildSeatView(0), 0);

            Assert.That(choice, Is.InstanceOf<PlayCardIntent>(), "at five times the weight this is the Snacktime swing");
            Assert.That(((PlayCardIntent)choice).Card, Is.EqualTo(beaver));
        }

        [Test]
        public void ZoomiesIsWorthAPointOfAttackAndNotFiveTimesIt()
        {
            // A 10/5 Zoomies for two against a 15/15 vanilla for three, with three mana on the table. The
            // bigger body wins: being able to swing this turn is worth a point of attack, not a body's worth
            // of it. At AttackPoint × 5 the Zoomies card wins instead.
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("TrailRabbit");
            CardInstanceId frog = scenario.Seat(0).Hand("PondFrog");
            scenario.Seat(0).Mana(3).DeckOf(4);
            scenario.Seat(1).Den(125).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            MatchIntent choice = Policy().ChooseAction(engine.BuildSeatView(0), 0);

            Assert.That(choice, Is.InstanceOf<PlayCardIntent>());
            Assert.That(((PlayCardIntent)choice).Card, Is.EqualTo(frog), "at five times the weight this is Trail Rabbit");
        }

        [Test]
        public void HealingAnUnthreatenedOwnDenLosesToDrawingACard()
        {
            // Forty hit points down with nothing on the other side of the table: a card is worth more than
            // topping up a Den nobody can reach. At HealPoint × 5 restoring twenty hit points outscores every
            // two-mana play in the pool, which is what the missed weight did.
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("WarmBiscuit");
            CardInstanceId notes = scenario.Seat(0).Hand("FieldNotes");
            scenario.Seat(0).Den(85).Mana(2).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            MatchIntent choice = Policy().ChooseAction(engine.BuildSeatView(0), 0);

            Assert.That(choice, Is.InstanceOf<PlayCardIntent>());
            Assert.That(((PlayCardIntent)choice).Card, Is.EqualTo(notes), "at five times the weight this is Warm Biscuit at the Den");
        }

        [Test]
        public void ACountedAmountIsWorthItsPerUnitToTheBot()
        {
            // Nine-Tail Matriarch is a 25/20 whose Hello deals five to the enemy Den for each other Kitsune
            // played; Moose Wanderer is a 25/30 for the same six mana. With two Kitsune behind it the
            // Matriarch's ten points of reach beat the five points of extra body. A bot that read the counter
            // as one per unit rather than five would value that reach at two and take the Moose — which is
            // what the second evaluator did until the arithmetic became `EffectAmount.ValueFrom`, shared with
            // the engine's.
            Scenario       scenario  = new Scenario(Config);
            CardInstanceId matriarch = scenario.Seat(0).Hand("NineTailMatriarch");
            scenario.Seat(0).Hand("MooseWanderer");
            scenario.Seat(0).Played("EmberKit");
            scenario.Seat(0).Played("Foxfire");
            scenario.Seat(0).Mana(6).DeckOf(4);
            scenario.Seat(1).Den(125).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            MatchIntent choice = Policy().ChooseAction(engine.BuildSeatView(0), 0);

            Assert.That(choice, Is.InstanceOf<PlayCardIntent>());
            Assert.That(((PlayCardIntent)choice).Card, Is.EqualTo(matriarch), "one per unit instead of five makes this Moose Wanderer");
        }

        // ---------------------------------------------------------------- tie-breaks

        [Test]
        public void AnExactTieIsBrokenWithoutTouchingTheSeed()
        {
            // Two identical enemy bodies: the two attacks score the same to the point, so the tie-break is the
            // whole decision. It has to be the rules' own enumeration order, and it has to be the same answer
            // whatever seed the match was dealt from.
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Board("GreyOwl");
            CardInstanceId first = scenario.Seat(1).Board("PondFrog");
            scenario.Seat(1).Board("PondFrog");
            scenario.Seat(0).Mana(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            SeatView view = engine.BuildSeatView(0);

            for (ulong seed = 1; seed <= 20; seed++)
            {
                MatchIntent choice = Policy(BotProfile.Strongest, seed).ChooseAction(view, 0);
                Assert.That(choice, Is.InstanceOf<AttackIntent>());
                Assert.That(((AttackIntent)choice).Target, Is.EqualTo(EffectTargetRef.OnCritter(first)), $"seed {seed}");
            }
        }

        // ---------------------------------------------------------------- the mulligan

        [Test]
        public void TheMulliganKeepsCheapAndThrowsExpensive()
        {
            for (ulong seed = 1; seed <= 40; seed++)
            {
                MatchEngine engine = MatchEngine.Create(
                    new MatchSetup(seed, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config)));

                for (int seat = 0; seat < MatchSeats.Count; seat++)
                {
                    SeatView       view   = engine.BuildSeatView(seat);
                    MulliganIntent choice = (MulliganIntent)Policy().ChooseAction(view, seat);

                    foreach (HandCard card in view.Hand)
                    {
                        CardInfo info     = Config.Cards[card.Card];
                        bool     replaced = choice.Replace.Contains(card.Instance);
                        // Derived, not delivered — the same call the policy makes. See HandCard.
                        int      cost     = ManaRules.CostToPlay(Config, view.Rules, seat, card);

                        if (!info.Collectible)
                            Assert.That(replaced, Is.False, $"seed {seed}: the compensation card was put back");
                        else if (cost <= 3)
                            Assert.That(replaced, Is.False, $"seed {seed}: {card.Card} at {cost} is castable early");
                        else if (cost >= 5)
                            Assert.That(replaced, Is.True, $"seed {seed}: {card.Card} at {cost} is not an opening card");
                    }

                    // And it is an intent the rules accept, which is the half a threshold rule can still fail.
                    Assert.That(engine.Submit(seat, choice), Is.EqualTo(MatchIntentResults.Success));
                }
            }
        }

        // ---------------------------------------------------------------- the peek

        [Test]
        public void ThePeekChoiceIsTheSameOneALapsedSeatWouldGet()
        {
            // The engine's default and the policy's rule have to be one rule. A player who steps away mid-peek
            // must not get a different game from one whose seat was covered.
            MatchEngine engine = PeekHeld();
            int         seat   = engine.Rules.PendingChoice.Seat;

            // Two ways out of the same position: the policy answers one copy, the deadline lapses on the other.
            MatchEngine byLapse = HiddenState.Wrap(HiddenState.Clone(engine.State, Config));

            MatchIntent answered = Policy().ChooseAction(engine.BuildSeatView(seat), seat);
            Assert.That(engine.Submit(seat, answered), Is.EqualTo(MatchIntentResults.Success));

            Assert.That(
                byLapse.ExpireDeadline(MatchDeadlineKind.EffectChoice, null),
                Is.EqualTo(MatchDeadlineOutcome.Applied));

            Assert.That(engine.ComputeRulesHash(), Is.EqualTo(byLapse.ComputeRulesHash()), "the two rules disagreed");
        }

        /// <summary>
        /// A game held on a peek over three cards of three different costs, so the choice turns on the cost
        /// ordering rather than on a tie-break between identical cards.
        /// </summary>
        static MatchEngine PeekHeld()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId pebble   = scenario.Seat(0).Hand("PebbleCollector");
            scenario.Seat(0).Deck("MeadowMouse");
            scenario.Seat(0).Deck("MooseWanderer");
            scenario.Seat(0).Deck("BusyBeaver");
            scenario.Seat(0).DeckOf(3);
            scenario.Seat(0).Mana(4);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, pebble);
            Assert.That(engine.Rules.PendingChoice, Is.Not.Null, "the peek did not hold");
            return engine;
        }

        [Test]
        public void ThePeekKeepsTheCostliestRevealedCard()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId pebble   = scenario.Seat(0).Hand("PebbleCollector");
            scenario.Seat(0).Deck("MeadowMouse");
            scenario.Seat(0).Deck("MooseWanderer");
            scenario.Seat(0).Deck("BusyBeaver");
            scenario.Seat(0).DeckOf(3);
            scenario.Seat(0).Mana(4);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, pebble);
            Assert.That(engine.Rules.PendingChoice, Is.Not.Null);

            EffectChoiceIntent choice = (EffectChoiceIntent)Policy().ChooseAction(engine.BuildSeatView(0), 0);

            Assert.That(choice.Keep.Count, Is.EqualTo(1));
            Assert.That(CardLookup.CardId(engine.Model, engine.Revealed(0)[choice.Keep[0]]), Is.EqualTo(CardId.FromString("MooseWanderer")));
        }

        // ---------------------------------------------------------------- the engine's own default

        [Test]
        public void ALapsedTurnDeadlineWithNoPolicyPlaysTheStrongestProfile()
        {
            // The engine's fallback is what cover and auto-play get when a host names no policy, and every
            // other call site in the suite passes one explicitly — so without this the default is the one path
            // nothing walks. A lethal on the board is the cheapest way to tell the strongest profile from
            // anything that merely plays legally.
            MatchEngine engine = LethalOnBoard();

            Assert.That(
                engine.ExpireDeadline(MatchDeadlineKind.Turn, autoPlay: null),
                Is.EqualTo(MatchDeadlineOutcome.Applied));

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete), "the default declined a lethal");
            Assert.That(engine.Result.WinnerSeat, Is.EqualTo(0));
        }

        [Test]
        public void APlayedOutRemainderWithNoPolicyPlaysTheStrongestProfile()
        {
            MatchEngine engine = LethalOnBoard();

            engine.PlayOutRemainder(policy: null);

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete));
            Assert.That(engine.Result.WinnerSeat, Is.EqualTo(0), "the default did not take the game it was handed");
        }

        /// <summary> Seat 0 to act, with exactly enough on the board to finish it this turn. </summary>
        static MatchEngine LethalOnBoard()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Board("StrayGoat");   // 20/15
            scenario.Seat(0).Mana(0).DeckOf(6);
            scenario.Seat(1).Den(20).DeckOf(6);
            return scenario.OnTurn(0).Build(MatchTimings.Default);
        }

        // ---------------------------------------------------------------- the structural property

        [Test]
        public void ThePolicyHoldsNothingItCouldCheatWith()
        {
            // "A decision is a pure function of (seat view, seed)" is enforced by what the policy is allowed to
            // hold and by what it is allowed to be handed: content, a profile and a seed. An authoritative
            // type anywhere on the surface would be a second input, and a seat view is only as honest as the
            // thing reading it.
            const BindingFlags Everything =
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;

            List<FieldInfo> state = new List<FieldInfo>();
            foreach (FieldInfo field in typeof(BotPolicy).GetFields(Everything))
            {
                // A compile-time constant is a number in the source, not state.
                if (field.IsLiteral)
                    continue;

                Assert.That(Authoritative(field.FieldType), Is.False, $"BotPolicy.{field.Name} could see past the seat view");

                // A static that is not a constant is shared across every match in the process, which would
                // make one seat's decision depend on another's.
                Assert.That(field.IsStatic, Is.False, $"BotPolicy.{field.Name} is mutable state shared between matches");
                state.Add(field);
            }

            Assert.That(state.Count, Is.EqualTo(3), "the policy's whole input is the content, the profile and the seed");

            // …and nothing it exposes hands one in or gives one back. The reachable surface is what a host
            // could call, so it is the surface the promise has to hold across.
            foreach (MethodInfo method in typeof(BotPolicy).GetMethods(Everything))
            {
                if (method.IsPrivate)
                    continue;

                Assert.That(Authoritative(method.ReturnType), Is.False, $"BotPolicy.{method.Name} returns an authoritative type");
                foreach (ParameterInfo parameter in method.GetParameters())
                    Assert.That(Authoritative(parameter.ParameterType), Is.False, $"BotPolicy.{method.Name} takes an authoritative {parameter.ParameterType.Name}");
            }
        }

        /// <summary>
        /// The types a policy has no business holding: the model root and its secret half, and the public
        /// rules state too — a policy reasons over a <see cref="SeatView"/>, which names the public members
        /// it needs one at a time rather than handing over a root somebody could walk.
        /// </summary>
        static bool Authoritative(System.Type type)
        {
            System.Type[] forbidden =
            {
                typeof(MatchEngine), typeof(MatchModel), typeof(MatchSecrets), typeof(SeatSecrets),
                typeof(MatchRulesState), typeof(MatchPacing), typeof(HiddenCard),
                typeof(SeatState), typeof(CardInstance), typeof(BoardCritter), typeof(PendingEffectChoice),
                typeof(RandomPCG), typeof(MatchSetup),
            };

            foreach (System.Type one in forbidden)
            {
                if (type == one || type == one.MakeArrayType())
                    return true;
            }

            return false;
        }

        [Test]
        public void TheSameViewAndSeedAlwaysGiveTheSameAction()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PondFrog");
            scenario.Seat(0).Hand("Foxfire");
            scenario.Seat(0).Board("GreyOwl");
            scenario.Seat(0).Mana(6).DeckOf(4);
            scenario.Seat(1).Board("PondFrog");
            scenario.Seat(1).Board("MeadowMouse");
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            foreach (BotProfile profile in BotProfile.All)
            {
                SeatView    view  = engine.BuildSeatView(0);
                MatchIntent first = new BotPolicy(Config, profile, 4242).ChooseAction(view, 0);

                for (int again = 0; again < 5; again++)
                {
                    MatchIntent repeat = new BotPolicy(Config, profile, 4242).ChooseAction(engine.BuildSeatView(0), 0);
                    Assert.That(Describe(repeat), Is.EqualTo(Describe(first)), $"{profile} wandered");
                }
            }
        }

        static string Describe(MatchIntent intent) => SelfPlayHarness.Describe(intent);
    }
}
