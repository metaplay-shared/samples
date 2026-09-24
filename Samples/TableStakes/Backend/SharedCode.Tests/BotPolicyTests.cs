using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using static Game.Logic.Tests.MatchTestDeals;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests <see cref="BotPolicy"/>: decisions on hand-built positions, determinism, seeded mistakes, and the
    /// entry points that play a seat for an absent human.
    /// </summary>
    [TestFixture]
    public class BotPolicyTests
    {
        const ulong SeedA = 20260831UL;
        const ulong SeedB = 4242UL;

        static readonly ulong[] Seeds = new ulong[] { SeedA, SeedB, 1UL, 7UL, 0xDEADBEEFUL };

        #region The pinned positions

        /// <summary>
        /// Returns the view of the seat on turn after <paramref name="played"/> is played on <paramref name="deck"/>,
        /// with seat 0 leading.
        /// </summary>
        static MatchSeatView SeatOnTurn(List<Card> deck, params Card[] played)
        {
            MatchEngine engine = MatchTestDeals.Engine(SeedA, deck, startingLeaderSeat: 0);
            MatchTestDeals.PlayInOrder(engine, played);
            return MatchSeatView.ForSeat(engine, engine.SeatOnTurn);
        }

        static void AssertSharpPlaysForEverySeed(MatchSeatView view, Card expected)
        {
            foreach (ulong seed in Seeds)
                Assert.That(BotPolicy.ChooseCard(view, TestBotConfig.Sharp, seed), Is.EqualTo(expected), $"seed {seed}");
        }

        /// <summary>
        /// Seat 3 must follow hearts and holds three of them, all of which beat the seven on the table.
        /// </summary>
        static List<Card> CheapestWinnerDeck() =>
            MatchTestDeals.BuildDeck(
                seat0: new Card[] { Hearts(Rank.Five), Clubs(Rank.Three), Clubs(Rank.Four), Clubs(Rank.Five), Clubs(Rank.Six) },
                seat1: new Card[] { Hearts(Rank.Seven), Diamonds(Rank.Two), Diamonds(Rank.Three), Diamonds(Rank.Four), Diamonds(Rank.Five) },
                seat2: new Card[] { Hearts(Rank.Two), Diamonds(Rank.Six), Diamonds(Rank.Seven), Diamonds(Rank.Eight), Diamonds(Rank.Nine) },
                seat3: new Card[] { Hearts(Rank.Eight), Hearts(Rank.King), Hearts(Rank.Ace), Clubs(Rank.Seven), Clubs(Rank.Eight) },
                trumpCard: Spades(Rank.Two));

        /// <summary>The cheapest-winner position dealt by an engine with the given RNG seed, with seat 3 on turn.</summary>
        static MatchEngine CheapestWinnerEngine(ulong engineSeed)
        {
            MatchEngine engine = MatchTestDeals.Engine(engineSeed, CheapestWinnerDeck(), startingLeaderSeat: 0);
            MatchTestDeals.PlayInOrder(engine, Hearts(Rank.Five), Hearts(Rank.Seven), Hearts(Rank.Two));
            return engine;
        }

        static MatchSeatView CheapestWinnerPosition() => MatchSeatView.ForSeat(CheapestWinnerEngine(SeedA), 3);

        [Test]
        public void Pinned_TakesTheTrickWithTheCheapestCardThatWinsIt()
        {
            MatchSeatView view = CheapestWinnerPosition();

            Assert.That(view.GetLegalPlays(), Has.Count.EqualTo(3), "the position must offer a wrong answer for the right one to mean anything");
            AssertSharpPlaysForEverySeed(view, Hearts(Rank.Eight));
        }

        [Test]
        public void Pinned_PlaysTheOnlyLegalCardWhenTheSuitForcesIt()
        {
            MatchSeatView view = SeatOnTurn(
                MatchTestDeals.BuildDeck(
                    seat0: new Card[] { Hearts(Rank.Five), Clubs(Rank.Three), Clubs(Rank.Four), Clubs(Rank.Five), Clubs(Rank.Six) },
                    seat1: new Card[] { Hearts(Rank.Seven), Diamonds(Rank.Two), Diamonds(Rank.Three), Diamonds(Rank.Four), Diamonds(Rank.Five) },
                    seat2: new Card[] { Hearts(Rank.Two), Diamonds(Rank.Six), Diamonds(Rank.Seven), Diamonds(Rank.Eight), Diamonds(Rank.Nine) },
                    seat3: new Card[] { Hearts(Rank.Eight), Clubs(Rank.Seven), Clubs(Rank.Eight), Clubs(Rank.Nine), Clubs(Rank.Ten) },
                    trumpCard: Spades(Rank.Two)),
                Hearts(Rank.Five), Hearts(Rank.Seven), Hearts(Rank.Two));

            Assert.That(view.GetLegalPlays(), Has.Count.EqualTo(1), "the seat holds one heart and four clubs; only the heart is legal");
            foreach (BotProfile profile in TestBotConfig.All)
            {
                foreach (ulong seed in Seeds)
                    Assert.That(BotPolicy.ChooseCard(view, profile, seed), Is.EqualTo(Hearts(Rank.Eight)), $"profile {profile}, seed {seed}");
            }
        }

        [Test]
        public void Pinned_LosesWithTheCheapestCardWhenTheTrickIsGone()
        {
            MatchSeatView view = SeatOnTurn(
                MatchTestDeals.BuildDeck(
                    seat0: new Card[] { Hearts(Rank.Five), Clubs(Rank.Three), Clubs(Rank.Four), Clubs(Rank.Five), Clubs(Rank.Six) },
                    seat1: new Card[] { Hearts(Rank.King), Diamonds(Rank.Two), Diamonds(Rank.Three), Diamonds(Rank.Four), Diamonds(Rank.Five) },
                    seat2: new Card[] { Hearts(Rank.Four), Diamonds(Rank.Six), Diamonds(Rank.Seven), Diamonds(Rank.Eight), Diamonds(Rank.Nine) },
                    seat3: new Card[] { Hearts(Rank.Two), Hearts(Rank.Three), Hearts(Rank.Queen), Clubs(Rank.Seven), Clubs(Rank.Eight) },
                    trumpCard: Spades(Rank.Two)),
                Hearts(Rank.Five), Hearts(Rank.King), Hearts(Rank.Four));

            Assert.That(view.GetLegalPlays(), Has.Count.EqualTo(3), "three hearts, none of which beats the king");
            AssertSharpPlaysForEverySeed(view, Hearts(Rank.Two));
        }

        [Test]
        public void Pinned_TrumpsWithItsCheapestTrumpWhenVoidAndTheTrickIsWinnable()
        {
            MatchSeatView view = SeatOnTurn(
                MatchTestDeals.BuildDeck(
                    seat0: new Card[] { Hearts(Rank.Five), Clubs(Rank.Three), Clubs(Rank.Four), Clubs(Rank.Five), Clubs(Rank.Six) },
                    seat1: new Card[] { Hearts(Rank.King), Diamonds(Rank.Two), Diamonds(Rank.Three), Diamonds(Rank.Four), Diamonds(Rank.Five) },
                    seat2: new Card[] { Hearts(Rank.Two), Diamonds(Rank.Six), Diamonds(Rank.Seven), Diamonds(Rank.Eight), Diamonds(Rank.Nine) },
                    seat3: new Card[] { Spades(Rank.Three), Spades(Rank.King), Diamonds(Rank.Ten), Diamonds(Rank.Jack), Clubs(Rank.Seven) },
                    trumpCard: Spades(Rank.Two)),
                Hearts(Rank.Five), Hearts(Rank.King), Hearts(Rank.Two));

            Assert.That(view.GetLegalPlays(), Has.Count.EqualTo(5), "void in hearts, so every card is legal");
            AssertSharpPlaysForEverySeed(view, Spades(Rank.Three));
        }

        [Test]
        public void Pinned_DiscardsRatherThanWastingATrumpItCannotWinWith()
        {
            // Seat 2 has trumped with the nine, so neither of seat 3's trumps can win the trick. Seat 3 should
            // discard its cheapest plain card instead of a trump.
            MatchSeatView view = SeatOnTurn(
                MatchTestDeals.BuildDeck(
                    seat0: new Card[] { Hearts(Rank.Five), Clubs(Rank.Three), Clubs(Rank.Four), Clubs(Rank.Five), Clubs(Rank.Six) },
                    seat1: new Card[] { Hearts(Rank.King), Diamonds(Rank.Three), Diamonds(Rank.Five), Diamonds(Rank.Six), Diamonds(Rank.Seven) },
                    seat2: new Card[] { Spades(Rank.Nine), Diamonds(Rank.Eight), Diamonds(Rank.Ten), Diamonds(Rank.Jack), Diamonds(Rank.Queen) },
                    seat3: new Card[] { Spades(Rank.Two), Spades(Rank.Three), Diamonds(Rank.Four), Diamonds(Rank.Nine), Clubs(Rank.Two) },
                    trumpCard: Spades(Rank.Four)),
                Hearts(Rank.Five), Hearts(Rank.King), Spades(Rank.Nine));

            Assert.That(view.GetLegalPlays(), Has.Count.EqualTo(5), "void in hearts, so every card is legal");
            AssertSharpPlaysForEverySeed(view, Clubs(Rank.Two));
        }

        [Test]
        public void Pinned_LeadsTheCardNoUnseenCardOutranks()
        {
            MatchSeatView view = SeatOnTurn(
                MatchTestDeals.BuildDeck(
                    seat0: new Card[] { Hearts(Rank.Ace), Clubs(Rank.Three), Clubs(Rank.Four), Diamonds(Rank.Five), Diamonds(Rank.Six) },
                    seat1: new Card[] { Clubs(Rank.Five), Clubs(Rank.Six), Clubs(Rank.Seven), Clubs(Rank.Eight), Clubs(Rank.Nine) },
                    seat2: new Card[] { Diamonds(Rank.Seven), Diamonds(Rank.Eight), Diamonds(Rank.Nine), Diamonds(Rank.Ten), Diamonds(Rank.Jack) },
                    seat3: new Card[] { Hearts(Rank.Two), Hearts(Rank.Three), Hearts(Rank.Four), Hearts(Rank.Five), Hearts(Rank.Six) },
                    trumpCard: Spades(Rank.Two)));

            Assert.That(view.GetLegalPlays(), Has.Count.EqualTo(5), "the leader may play anything");
            AssertSharpPlaysForEverySeed(view, Hearts(Rank.Ace));
        }

        [Test]
        public void Pinned_LeadsItsMasterTrumpAheadOfItsMasterPlainCard()
        {
            MatchSeatView view = SeatOnTurn(
                MatchTestDeals.BuildDeck(
                    seat0: new Card[] { Spades(Rank.Ace), Hearts(Rank.Ace), Clubs(Rank.Three), Clubs(Rank.Four), Diamonds(Rank.Five) },
                    seat1: new Card[] { Clubs(Rank.Five), Clubs(Rank.Six), Clubs(Rank.Seven), Clubs(Rank.Eight), Clubs(Rank.Nine) },
                    seat2: new Card[] { Diamonds(Rank.Seven), Diamonds(Rank.Eight), Diamonds(Rank.Nine), Diamonds(Rank.Ten), Diamonds(Rank.Jack) },
                    seat3: new Card[] { Hearts(Rank.Two), Hearts(Rank.Three), Hearts(Rank.Four), Hearts(Rank.Five), Hearts(Rank.Six) },
                    trumpCard: Spades(Rank.Two)));

            AssertSharpPlaysForEverySeed(view, Spades(Rank.Ace));
        }

        [Test]
        public void Pinned_LeadsCheaplyWhenItHoldsNothingThatCommandsTheTrick()
        {
            MatchSeatView view = SeatOnTurn(
                MatchTestDeals.BuildDeck(
                    seat0: new Card[] { Hearts(Rank.Three), Diamonds(Rank.Four), Clubs(Rank.Five), Spades(Rank.Six), Spades(Rank.Seven) },
                    seat1: new Card[] { Hearts(Rank.Four), Hearts(Rank.Five), Hearts(Rank.Six), Hearts(Rank.Seven), Hearts(Rank.Eight) },
                    seat2: new Card[] { Diamonds(Rank.Five), Diamonds(Rank.Six), Diamonds(Rank.Seven), Diamonds(Rank.Eight), Diamonds(Rank.Nine) },
                    seat3: new Card[] { Clubs(Rank.Six), Clubs(Rank.Seven), Clubs(Rank.Eight), Clubs(Rank.Nine), Clubs(Rank.Ten) },
                    trumpCard: Spades(Rank.Two)));

            AssertSharpPlaysForEverySeed(view, Hearts(Rank.Three));
        }

        #endregion

        #region Purity and legality

        [Test]
        public void ADecisionIsAPureFunctionOfTheSeatViewAndTheSeed()
        {
            MatchSeatView view = CheapestWinnerPosition();

            // The same deal with a different engine RNG seed. The decision must not depend on it, because the
            // engine's RNG state is hidden information that no seat view exposes.
            MatchSeatView rebuilt = MatchSeatView.ForSeat(CheapestWinnerEngine(SeedB), 3);

            foreach (BotProfile profile in TestBotConfig.All)
            {
                foreach (ulong seed in Seeds)
                {
                    BotDecision first  = BotPolicy.Decide(view, profile, MatchTimings.Default, seed);
                    BotDecision second = BotPolicy.Decide(rebuilt, profile, MatchTimings.Default, seed);

                    Assert.That(second.Card, Is.EqualTo(first.Card), $"profile {profile}, seed {seed}");
                    Assert.That(second.ThinkDelay, Is.EqualTo(first.ThinkDelay), $"profile {profile}, seed {seed}");
                    Assert.That(second.PlayIndex, Is.EqualTo(first.PlayIndex), $"profile {profile}, seed {seed}");
                }
            }
        }

        [Test]
        public void EveryProfileAlwaysChoosesFromTheLegalSet()
        {
            for (int gameNdx = 0; gameNdx < 60; gameNdx++)
            {
                ulong       dealSeed = 0xA11CE000UL + (ulong)gameNdx;
                MetaTime    now      = MatchTestDeals.T0;
                MatchEngine engine   = MatchEngine.Create(dealSeed, MatchTimings.Instant, now);

                while (!engine.IsFinished)
                {
                    if (engine.TurnPhase == MatchTurnPhase.ResolvingTrick)
                    {
                        engine.Advance(engine.ResolvePauseEndsAt);
                        continue;
                    }

                    int           seat  = engine.SeatOnTurn;
                    MatchSeatView view  = MatchSeatView.ForSeat(engine, seat);
                    List<Card>    legal = view.GetLegalPlays();

                    foreach (BotProfile profile in TestBotConfig.All)
                    {
                        foreach (ulong seed in Seeds)
                        {
                            Card card = BotPolicy.ChooseCard(view, profile, seed);
                            Assert.That(SelfPlayHarness.Contains(legal, card), Is.True, $"deal seed {dealSeed}, profile {profile}, seed {seed}: {card} is not legal");
                        }
                    }

                    engine.PlayCard(seat, engine.PlayIndex, legal[0], now);
                }
            }
        }

        [Test]
        public void NegativeControl_ASeatThatIsNotOnTurnIsNotAskedForACard()
        {
            MatchSeatView notOnTurn = MatchSeatView.ForSeat(CheapestWinnerEngine(SeedB), 0);

            Assert.That(notOnTurn.IsOnTurn, Is.False);
            Assert.That(notOnTurn.GetLegalPlays(), Is.Empty);
            Assert.That(() => BotPolicy.ChooseCard(notOnTurn, TestBotConfig.Sharp, SeedA), Throws.InstanceOf<InvalidOperationException>());
        }

        #endregion

        #region Deliberate imperfection

        [Test]
        public void TheStrongestProfileNeverDeviatesFromItsBestCard()
        {
            MatchSeatView view = CheapestWinnerPosition();
            Card          best = BotPolicy.RankLegalPlays(view)[0];

            for (ulong seed = 0; seed < 400UL; seed++)
                Assert.That(BotPolicy.ChooseCard(view, TestBotConfig.Sharp, seed), Is.EqualTo(best), $"seed {seed}");
        }

        [Test]
        public void AnImperfectProfileErrsSometimesAndOnlyOntoItsRunnerUp()
        {
            MatchSeatView view   = CheapestWinnerPosition();
            List<Card>    ranked = BotPolicy.RankLegalPlays(view);

            Assert.That(ranked, Has.Count.EqualTo(3), "the position must offer more than one plausible card");

            int mistakes = 0;
            for (ulong seed = 0; seed < 400UL; seed++)
            {
                Card card = BotPolicy.ChooseCard(view, TestBotConfig.Casual, seed);
                if (card == ranked[0])
                    continue;

                mistakes++;
                Assert.That(card, Is.EqualTo(ranked[1]), $"seed {seed}: the mistake was not the runner-up");
            }

            // The bounds are wide enough around the Casual profile's mistake chance that only a broken or ignored
            // setting fails them.
            Assert.That(mistakes, Is.GreaterThan(40), "the profile never erred, so its mistake chance is being ignored");
            Assert.That(mistakes, Is.LessThan(160), "the profile erred far more often than its mistake chance");
        }

        [Test]
        public void TheTestProfilesBehaveAsTheirNamesPromise()
        {
            MatchSeatView view   = CheapestWinnerPosition();
            List<Card>    legal  = view.GetLegalPlays();
            List<Card>    sorted = new List<Card>(legal);
            sorted.Sort();

            HashSet<Card> randomChoices = new HashSet<Card>();
            for (ulong seed = 0; seed < 200UL; seed++)
            {
                randomChoices.Add(BotPolicy.ChooseCard(view, TestBotConfig.TestRandom, seed));
                Assert.That(BotPolicy.ChooseCard(view, TestBotConfig.TestDeterministic, seed), Is.EqualTo(sorted[0]), $"seed {seed}");
            }

            Assert.That(randomChoices, Has.Count.EqualTo(legal.Count), "the random profile did not reach every legal card");
        }

        #endregion

        #region Playing on behalf of an absent human

        [Test]
        public void OnlyAProfileThatNeverErrsMayPlayForAnAbsentHuman()
        {
            Assert.That(BotPolicy.MayPlayForAnAbsentHuman(BotProfiles.Strongest), Is.True);

            foreach (BotProfile profile in new[] { TestBotConfig.Steady, TestBotConfig.Casual, TestBotConfig.TestRandom, TestBotConfig.TestDeterministic })
                Assert.That(BotPolicy.MayPlayForAnAbsentHuman(profile), Is.False, $"profile {profile}");
        }

        [Test]
        public void TheAbsentHumanEntryPointsCannotBeHandedAProfileAtAll()
        {
            // These entry points always play at full strength because they take no BotProfile parameter. This
            // fails if one is added.
            string[] entryPoints = new string[] { nameof(BotPolicy.DecideAutoPlay), nameof(BotPolicy.DecideCover), nameof(BotPolicy.DecidePlayOut) };
            foreach (string name in entryPoints)
            {
                MethodInfo method = typeof(BotPolicy).GetMethod(name, BindingFlags.Public | BindingFlags.Static);
                Assert.That(method, Is.Not.Null, name);
                foreach (ParameterInfo parameter in method.GetParameters())
                    Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(BotProfile)), $"{name} accepts a profile");
            }
        }

        [Test]
        public void TheAbsentHumanEntryPointsPlayTheStrongCardEvenWhereAWeakProfileWouldErr()
        {
            MatchSeatView view   = CheapestWinnerPosition();
            List<Card>    ranked = BotPolicy.RankLegalPlays(view);

            ulong erringSeed = ulong.MaxValue;
            for (ulong seed = 0; seed < 400UL; seed++)
            {
                if (BotPolicy.ChooseCard(view, TestBotConfig.Casual, seed) != ranked[0])
                {
                    erringSeed = seed;
                    break;
                }
            }
            Assert.That(erringSeed, Is.Not.EqualTo(ulong.MaxValue), "no seed made the weak profile err, so the comparison proves nothing");

            Assert.That(BotPolicy.DecideAutoPlay(view, erringSeed).Card, Is.EqualTo(ranked[0]), $"seed {erringSeed}");
            Assert.That(BotPolicy.DecideCover(view, MatchTimings.Default, erringSeed, ownerConnected: true).Card, Is.EqualTo(ranked[0]), $"seed {erringSeed}");
            Assert.That(BotPolicy.DecideCover(view, MatchTimings.Default, erringSeed, ownerConnected: false).Card, Is.EqualTo(ranked[0]), $"seed {erringSeed}");
            Assert.That(BotPolicy.DecidePlayOut(view, erringSeed).Card, Is.EqualTo(ranked[0]), $"seed {erringSeed}");
        }

        [Test]
        public void ADeadlineLapseIsAnsweredImmediatelyAndAPlayedOutTableHasNoDelay()
        {
            MatchSeatView view = CheapestWinnerPosition();

            foreach (ulong seed in Seeds)
            {
                Assert.That(BotPolicy.DecideAutoPlay(view, seed).ThinkDelay, Is.EqualTo(MetaDuration.Zero), $"seed {seed}");
                Assert.That(BotPolicy.DecidePlayOut(view, seed).ThinkDelay, Is.EqualTo(MetaDuration.Zero), $"seed {seed}");
            }
        }

        [Test]
        public void ACoveredSeatWhoseOwnerIsConnectedPlaysOnTheSlowReclaimDelay()
        {
            MatchSeatView view    = CheapestWinnerPosition();
            MatchTimings  timings = MatchTimings.Default;

            foreach (ulong seed in Seeds)
            {
                Assert.That(BotPolicy.DecideCover(view, timings, seed, ownerConnected: true).ThinkDelay,
                    Is.EqualTo(timings.CoveredSeatReclaimDelay), $"seed {seed}");

                MetaDuration absentOwnerDelay = BotPolicy.DecideCover(view, timings, seed, ownerConnected: false).ThinkDelay;
                Assert.That(absentOwnerDelay, Is.GreaterThanOrEqualTo(timings.BotThinkDelayMin), $"seed {seed}");
                Assert.That(absentOwnerDelay, Is.LessThanOrEqualTo(timings.BotThinkDelayOccasionalMax), $"seed {seed}");
            }
        }

        [Test]
        public void ADecisionNamesThePlayIndexItWasDecidedFor()
        {
            MatchSeatView view = CheapestWinnerPosition();

            Assert.That(BotPolicy.Decide(view, TestBotConfig.Sharp, MatchTimings.Default, SeedA).PlayIndex, Is.EqualTo(view.Board.PlayIndex));
            Assert.That(BotPolicy.DecideAutoPlay(view, SeedA).Seat, Is.EqualTo(view.Seat));
        }

        #endregion

        #region Pacing

        [Test]
        public void ThinkDelaysStayInsideTheHostsBandsAndReachTheLongOne()
        {
            MatchSeatView view    = CheapestWinnerPosition();
            MatchTimings  timings = MatchTimings.Default;

            int longThinks    = 0;
            int ordinaryDraws = 0;
            for (ulong seed = 0; seed < 500UL; seed++)
            {
                MetaDuration delay = BotPolicy.DrawThinkDelay(view, TestBotConfig.Sharp, timings, seed);
                Assert.That(delay, Is.GreaterThanOrEqualTo(timings.BotThinkDelayMin), $"seed {seed}");
                Assert.That(delay, Is.LessThanOrEqualTo(timings.BotThinkDelayOccasionalMax), $"seed {seed}");

                if (delay > timings.BotThinkDelayMax)
                    longThinks++;
                else
                    ordinaryDraws++;
            }

            Assert.That(longThinks, Is.GreaterThan(0), "no move ever took the longer band");
            Assert.That(ordinaryDraws, Is.GreaterThan(longThinks), "the longer band was not the occasional one");
        }

        [Test]
        public void ZeroedTimingsGiveZeroDelay()
        {
            MatchSeatView view = CheapestWinnerPosition();

            foreach (BotProfile profile in TestBotConfig.All)
            {
                for (ulong seed = 0; seed < 100UL; seed++)
                    Assert.That(BotPolicy.DrawThinkDelay(view, profile, MatchTimings.Instant, seed), Is.EqualTo(MetaDuration.Zero), $"profile {profile}, seed {seed}");
            }
        }

        [Test]
        public void AProfileWithNoLongThinkChanceNeverTakesTheLongerBand()
        {
            // Negative control for the think-delay draw. A draw that ignored the profile would still land inside
            // the delay bands, so this checks that a zero long-think chance never reaches the longer band.
            MatchSeatView view    = CheapestWinnerPosition();
            MatchTimings  timings = MatchTimings.Default;

            for (ulong seed = 0; seed < 500UL; seed++)
                Assert.That(BotPolicy.DrawThinkDelay(view, TestBotConfig.TestRandom, timings, seed), Is.LessThanOrEqualTo(timings.BotThinkDelayMax), $"seed {seed}");
        }

        #endregion

        #region The roster

        [Test]
        public void AProfileIsDrawnPerSeatAndReproducesFromTheTableSeed()
        {
            HashSet<string> drawn = new HashSet<string>();
            for (ulong seed = 0; seed < 200UL; seed++)
            {
                for (int seat = 0; seat < MatchRules.NumSeats; seat++)
                {
                    BotProfile profile = BotProfiles.DrawForSeat(TestBotConfig.Opponents, seed, seat);
                    Assert.That(BotProfiles.DrawForSeat(TestBotConfig.Opponents, seed, seat), Is.SameAs(profile), $"seed {seed}, seat {seat}");
                    Assert.That(TestBotConfig.Opponents, Does.Contain(profile), $"seed {seed}, seat {seat}");
                    drawn.Add(profile.Id.Value);
                }
            }

            Assert.That(drawn, Has.Count.EqualTo(TestBotConfig.Opponents.Count), "the draw never reached part of the published strengths");
        }

        [Test]
        public void ThePublishedStrengthsAreWhatTheDrawReachesForAndTheirValuesAreWhatATableCarries()
        {
            // A seat is dealt one of the published rows, as a copy of the row's values rather than a reference to
            // the row, so publishing new config cannot change the bots at a table that is already dealt.
            List<BotProfile> published = BotConfig.DrawableProfiles(TestGameConfig.Build());
            Assert.That(published, Has.Count.EqualTo(TestBotConfig.ProfileRows().Count));

            BotProfile drawn = BotProfiles.DrawForSeat(published, seed: 7UL, seat: 1);
            BotProfileInfo row = null;
            foreach (BotProfileInfo candidate in TestBotConfig.ProfileRows())
            {
                if (candidate.Id == drawn.Id)
                    row = candidate;
            }

            Assert.That(row, Is.Not.Null, "the draw produced a strength that is not published");
            Assert.That(drawn.Mode, Is.EqualTo(BotDecisionMode.Heuristic), "a published opponent always plays the heuristic");
            Assert.That(drawn.MistakeChancePercent, Is.EqualTo(row.MistakeChancePercent));
            Assert.That(drawn.LongThinkChancePercent, Is.EqualTo(row.LongThinkChancePercent));
        }

        [Test]
        public void ASeatCarryingNoStrengthAtAllIsPlayedAtFullStrength()
        {
            // A persisted table can have a bot seat with no stored profile. Such a seat plays the card that
            // BotProfiles.Strongest would choose.
            MatchEngine engine = MatchTestDeals.Engine(SeedA, CheapestWinnerDeck(), startingLeaderSeat: 0);

            List<MatchSeat>  seats    = MatchTestDeals.Seats(numHumanSeats: 0, hasArrived: true);
            List<BotProfile> profiles = new List<BotProfile> { null, null, null, null };

            MatchModel model = new MatchModel();
            model.Setup(engine, seats, profiles, MatchTestDeals.T0);
            MatchTestDeals.Start(model, MatchTestDeals.T0);

            int seat = model.Board.SeatOnTurn;
            Assert.That(model.GetSeatBotProfile(seat), Is.Null, "this fixture is meant to seat a bot with no strength");
            Assert.That(
                MatchHost.DecideBotMove(model, seat, SeedA).Card,
                Is.EqualTo(BotPolicy.ChooseCard(MatchSeatView.ForSeat(engine, seat), BotProfiles.Strongest, SeedA)));
        }

        [Test]
        public void AnArchiveWithNoPublishedStrengthsDealsTheStrongestProfileRatherThanNone()
        {
            // The config build refuses an empty profile list, but an archive built without the list can still be
            // loaded. Every seat then gets BotProfiles.Strongest.
            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
                Assert.That(BotProfiles.DrawForSeat(new List<BotProfile>(), seed: 3UL, seat: seat), Is.SameAs(BotProfiles.Strongest), $"seat {seat}");
        }

        #endregion
    }
}
