using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;
using System.Diagnostics;

namespace Game.Logic.Tests
{
    /// <summary>
    /// What self-play checks about profiles rather than about the engine: that a mistake is seeded and
    /// reproducible, that it never reaches outside the plausible band, that the lethal branch is exempt from
    /// it at every strength, and — bluntly, once — that the strongest profile is connected to anything at all.
    /// </summary>
    [TestFixture]
    public class ProfileTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        /// <summary> The degenerate end of the range: it departs from the top action every single time. </summary>
        static readonly BotProfile AlwaysDeviates = BotProfile.Tuned("AlwaysDeviates", 10000, 100 * BotProfile.ManaValueUnit);

        // ---------------------------------------------------------------- the strongest profiles

        [Test]
        public void TheStrongestProfileNeverReadsTheSeed()
        {
            // It is what cover, auto-play and the Heist auto-default use, and a seed that changed its play
            // would mean an absent player's cards were being played differently depending on the deal.
            string baseline = ScriptOf(BotProfile.Strongest, matchSeed: 0, gameSeed: 31337);

            for (ulong seed = 1; seed <= 8; seed++)
                Assert.That(ScriptOf(BotProfile.Strongest, seed, gameSeed: 31337), Is.EqualTo(baseline), $"match seed {seed} changed the play");
        }

        [Test]
        public void TheStrictlyDeterministicProfilePlaysTheStrongestProfilesGame()
        {
            // The balance harness reads from one and cover reads from the other, so they have to be the same
            // player: a win-rate signal from a different bot measures a different bot.
            for (ulong game = 1; game <= 12; game++)
            {
                Assert.That(
                    ScriptOf(BotProfile.StrictlyDeterministic, matchSeed: game, gameSeed: game),
                    Is.EqualTo(ScriptOf(BotProfile.Strongest, matchSeed: game, gameSeed: game)),
                    $"game {game}");
            }
        }

        // ---------------------------------------------------------------- the mistake mechanism

        [Test]
        public void AWeakProfileActuallyDepartsFromTheTopAction()
        {
            // The mechanism has to be wired to something. A profile whose mistakes never happened would pass
            // every other test in this file.
            int departures = CountDepartures(BotProfile.Sloppy, out int decisions);

            TestContext.Out.WriteLine($"sloppy departed on {departures} of {decisions} decisions");
            Assert.That(decisions, Is.GreaterThan(50));
            Assert.That(departures, Is.GreaterThan(0), "a weak profile that never departs is a dead mechanism");
        }

        [Test]
        public void TheSameSeedMakesTheSameMistakeInTheSamePlace()
        {
            for (ulong game = 1; game <= 8; game++)
            {
                Assert.That(
                    ScriptOf(BotProfile.Casual, matchSeed: game, gameSeed: game),
                    Is.EqualTo(ScriptOf(BotProfile.Casual, matchSeed: game, gameSeed: game)),
                    $"game {game} replayed differently");
            }

            // …and a different seed really does make a different game, or the check above proves nothing.
            bool anyDiffer = false;
            for (ulong game = 1; game <= 8; game++)
                anyDiffer |= ScriptOf(BotProfile.Casual, matchSeed: game, gameSeed: 99) != ScriptOf(BotProfile.Casual, matchSeed: game + 1000, gameSeed: 99);

            Assert.That(anyDiffer, Is.True, "the match seed has to reach the mistakes");
        }

        [Test]
        public void AMistakeNeverReachesOutsideThePlausibleBand()
        {
            // "Never a random discard" is the promise, and it is directly checkable: whatever a weak profile
            // picks has to be an action the scorer put within the band of its own top choice. Anything below
            // that floor is a move with no plausible reading behind it.
            MatchEngine engine = RichPosition().OnTurn(0).Build();
            SeatView    view   = engine.BuildSeatView(0);

            List<int> scores = new BotPolicy(Config, BotProfile.Strongest).ScoreLegalActions(view, 0);
            int       top    = 0;
            for (int ndx = 1; ndx < scores.Count; ndx++)
            {
                if (scores[ndx] > scores[top])
                    top = ndx;
            }

            int worst = scores[0];
            foreach (int score in scores)
                worst = score < worst ? score : worst;

            Assert.That(worst, Is.LessThan(scores[top] - BotProfile.Practiced.CandidateBandWidth),
                "the position has to hold an action outside the band, or the check proves nothing");

            foreach (BotProfile profile in new[] { BotProfile.Practiced, BotProfile.Casual, BotProfile.Sloppy })
            {
                for (ulong seed = 1; seed <= 60; seed++)
                {
                    MatchIntent picked = new BotPolicy(Config, profile, seed).ChooseAction(view, 0);
                    int         index  = IndexInLegalSet(view, picked);

                    Assert.That(index, Is.GreaterThanOrEqualTo(0), $"{profile} at seed {seed}: outside the legal set");
                    Assert.That(scores[index], Is.GreaterThanOrEqualTo(scores[top] - profile.CandidateBandWidth),
                        $"{profile} at seed {seed} reached below its own band");
                }
            }
        }

        [Test]
        public void ThePlausibleBandWidens()
        {
            // Two profiles, same position, same seeds: the wider band reaches further down the ranking. This
            // is the parameter doing what it says, not a strength claim.
            MatchEngine engine = RichPosition().OnTurn(0).Build();
            SeatView    view   = engine.BuildSeatView(0);

            int narrow = DistinctChoices(view, BotProfile.Tuned("Narrow", 10000, 1 * BotProfile.ManaValueUnit));
            int wide   = DistinctChoices(view, BotProfile.Tuned("Wide", 10000, 40 * BotProfile.ManaValueUnit));

            TestContext.Out.WriteLine($"band width reached {narrow} distinct actions narrow, {wide} wide");
            Assert.That(wide, Is.GreaterThan(narrow));
        }

        [Test]
        public void AZeroWidthBandIsNoMistakeAtAll()
        {
            // The two parameters are an and, not an or: a mistake chance with nothing plausible to move to is
            // not a mistake, and a profile configured that way plays the strongest profile's game.
            MatchEngine engine = RichPosition().OnTurn(0).Build();
            SeatView    view   = engine.BuildSeatView(0);

            BotProfile  noBand = BotProfile.Tuned("NoBand", 10000, 0);
            MatchIntent top    = new BotPolicy(Config, BotProfile.Strongest).ChooseAction(view, 0);

            Assert.That(noBand.MakesMistakes, Is.False);
            for (ulong seed = 1; seed <= 20; seed++)
                Assert.That(Same(new BotPolicy(Config, noBand, seed).ChooseAction(view, 0), top), Is.True, $"seed {seed}");
        }

        // ---------------------------------------------------------------- the already-won floor

        [Test]
        public void ALethalCastIsTakenAtEveryStrengthToo()
        {
            // The exemption is over the whole lethal branch, not just its attacks: a burn that finishes the
            // game is the same promise.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId foxfire  = scenario.Seat(0).Hand("Foxfire");
            scenario.Seat(0).Hand("PondFrog");
            scenario.Seat(0).Mana(5).DeckOf(4);
            scenario.Seat(1).Den(15).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();
            SeatView    view   = engine.BuildSeatView(0);

            foreach (BotProfile profile in new[] { BotProfile.Casual, BotProfile.Sloppy, AlwaysDeviates })
            {
                for (ulong seed = 1; seed <= 30; seed++)
                {
                    MatchIntent choice = new BotPolicy(Config, profile, seed).ChooseAction(view, 0);
                    Assert.That(choice, Is.InstanceOf<PlayCardIntent>(), $"{profile} at seed {seed}");
                    Assert.That(((PlayCardIntent)choice).Card, Is.EqualTo(foxfire), $"{profile} at seed {seed}");
                    Assert.That(((PlayCardIntent)choice).Target, Is.EqualTo(EffectTargetRef.Den(1)), $"{profile} at seed {seed}");
                }
            }
        }

        [Test]
        public void NoProfileEverDeclinesALethalItCanSee()
        {
            // The literal, checkable form of "it does not throw a game that is already won"
            // (Docs/bots.md). Anything broader would need a look-ahead the policy does not have.
            // Two bodies and a trade on offer, so declining the lethal is an available and superficially
            // reasonable-looking thing to do.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(0).Board("MeadowMouse");
            scenario.Seat(1).Board("BusyBeaver");
            scenario.Seat(0).Mana(0).DeckOf(4);
            scenario.Seat(1).Den(15).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();
            SeatView    view   = engine.BuildSeatView(0);

            foreach (BotProfile profile in new[] { BotProfile.Practiced, BotProfile.Casual, BotProfile.Sloppy, AlwaysDeviates })
            {
                for (ulong seed = 1; seed <= 40; seed++)
                {
                    MatchIntent choice = new BotPolicy(Config, profile, seed).ChooseAction(view, 0);
                    Assert.That(choice, Is.InstanceOf<AttackIntent>(), $"{profile} at seed {seed} declined a lethal swing");
                    Assert.That(((AttackIntent)choice).Attacker, Is.EqualTo(frog), $"{profile} at seed {seed}");
                    Assert.That(((AttackIntent)choice).Target, Is.EqualTo(EffectTargetRef.Den(1)), $"{profile} at seed {seed}");
                }
            }
        }

        [Test]
        public void AProfileThatAlwaysDeviatesStillPlaysLegalGamesToTheEnd()
        {
            SelfPlayHarness.RunBatch(
                "always-deviates",
                SelfPlayStreams.Profiles,
                SelfPlayRun.Games(standard: 20, deep: 800),
                index => new SelfPlayGameSpec
                {
                    Config = Config,
                    Seat0  = SelfPlaySeats.Profile(AlwaysDeviates),
                    Seat1  = SelfPlaySeats.Profile(AlwaysDeviates),
                    Checks = SelfPlayChecks.All,
                    // Every action of this game also runs against a follower, and the two models are
                    // compared after each one. This is the refactor's proof, and it is on for every batch.
                    MirrorOnFollower = true,
                });
        }

        // ---------------------------------------------------------------- the wiring smoke test

        [Test]
        public void StrongestVersusDegraded_FirstPlayerRotated_IsAWiringSmokeTestNotAnArena()
        {
            // Read the name. This is not a strength measurement and must never be treated as one:
            // Docs/bots.md rejects a bot-strength arena outright, because a bare win rate over a batch
            // is unresolvable without a confidence interval and an unrotated schedule reports the
            // first-player advantage The Acorn exists to offset as a strength difference.
            //
            // What is checked is far blunter: that the strongest profile, against a badly degraded one with
            // first player rotated across the batch, does not lose at a rate that would only make sense if a
            // profile's weighting were wired backwards or a mistake rate were pinned near total. The threshold
            // is wide enough that ordinary variance never trips it.
            int       pairs     = SelfPlayRun.Games(standard: 40, deep: 300);
            int       wins      = 0;
            int       decided   = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int index = 0; index < pairs; index++)
            {
                ulong seed = SelfPlayRun.GameSeed(SelfPlayStreams.WiringSmokeTest, index);

                // The same seed both ways, so the deal and the first seat are identical and the only thing
                // that moved is which side of the table the strong profile is sitting at.
                decided += Score(seed, strongSeat: 0, ref wins);
                decided += Score(seed, strongSeat: 1, ref wins);
            }

            stopwatch.Stop();
            TestContext.Out.WriteLine($"wiring smoke test: the strongest profile took {wins} of {decided} decided games over {pairs} rotated pairs, {stopwatch.ElapsedMilliseconds} ms");

            Assert.That(decided, Is.GreaterThan(pairs), "too few games were decided to say anything at all");
            Assert.That(wins * 100 / decided, Is.GreaterThan(30), "the strongest profile is losing at a rate only a wiring fault explains");
        }

        static int Score(ulong seed, int strongSeat, ref int wins)
        {
            SelfPlaySeat strong = SelfPlaySeats.Strongest;
            SelfPlaySeat weak   = SelfPlaySeats.Profile(AlwaysDeviates);

            SelfPlayGameRecord record = SelfPlayHarness.RunGame(new SelfPlayGameSpec
            {
                Config            = Config,
                Seed              = seed,
                Seat0             = strongSeat == 0 ? strong : weak,
                Seat1             = strongSeat == 0 ? weak : strong,
                Checks            = SelfPlayChecks.None,
                    // Every action of this game also runs against a follower, and the two models are
                    // compared after each one. This is the refactor's proof, and it is on for every batch.
                    MirrorOnFollower = true,
                DeterminismStride = 0,
            });

            if (record.IsDraw)
                return 0;

            if (record.WinnerSeat == strongSeat)
                wins++;

            return 1;
        }

        // ---------------------------------------------------------------- fixtures

        /// <summary> A position with enough on the table that several actions score close together. </summary>
        static Scenario RichPosition()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PondFrog");
            scenario.Seat(0).Hand("BusyBeaver");
            scenario.Seat(0).Hand("Foxfire");
            scenario.Seat(0).Hand("FieldNotes");
            // A card whose only legal targets are bad ones, so the ranking has a floor worth staying above.
            scenario.Seat(0).Hand("Undertow");
            scenario.Seat(0).Board("GreyOwl");
            scenario.Seat(0).Board("StrayGoat");
            scenario.Seat(0).Mana(8).DeckOf(6);
            scenario.Seat(1).Board("PondFrog");
            scenario.Seat(1).Board("WiseTortoise");
            scenario.Seat(1).Board("MeadowMouse");
            scenario.Seat(1).DeckOf(6);
            return scenario;
        }

        static int DistinctChoices(SeatView view, BotProfile profile)
        {
            List<string> seen = new List<string>();
            for (ulong seed = 1; seed <= 80; seed++)
            {
                string choice = SelfPlayHarness.Describe(new BotPolicy(Config, profile, seed).ChooseAction(view, 0));
                if (!seen.Contains(choice))
                    seen.Add(choice);
            }

            return seen.Count;
        }

        static int CountDepartures(BotProfile profile, out int decisions)
        {
            int departures = 0;
            int seen       = 0;

            for (ulong game = 1; game <= 6; game++)
            {
                MatchEngine engine   = Fresh(game);
                BotPolicy   weak     = new BotPolicy(Config, profile, game);
                BotPolicy   strongest = new BotPolicy(Config, BotProfile.Strongest, game);

                for (int step = 0; step < 600 && engine.Phase != MatchPhase.Complete; step++)
                {

                    int      seat = engine.Pending.Kind == MatchPendingKind.AwaitingEffectChoice ? engine.Rules.PendingChoice.Seat : engine.Rules.SeatOnTurn;
                    SeatView view = engine.BuildSeatView(seat);

                    MatchIntent weakChoice   = weak.ChooseAction(view, seat) ?? new EndTurnIntent();
                    MatchIntent strongChoice = strongest.ChooseAction(view, seat) ?? new EndTurnIntent();

                    seen++;
                    if (!Same(weakChoice, strongChoice))
                        departures++;

                    engine.Submit(seat, weakChoice);
                }
            }

            decisions = seen;
            return departures;
        }

        static MatchEngine Fresh(ulong seed)
        {
            MatchEngine engine = MatchEngine.Create(
                new MatchSetup(seed, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config)));

            engine.Mulligan(0);
            engine.Mulligan(1);
            return engine;
        }

        /// <summary>
        /// One game's whole decision script as a single string. Deliberately not a list: two lists compare by
        /// reference under <c>Equals</c>, which would make "these two runs differ" true of every pair and the
        /// check below unable to fail.
        /// </summary>
        static string ScriptOf(BotProfile profile, ulong matchSeed, ulong gameSeed)
        {
            MatchEngine  engine = Fresh(gameSeed);
            BotPolicy    policy = new BotPolicy(Config, profile, matchSeed);
            List<string> script = new List<string>();

            for (int step = 0; step < 4000 && engine.Phase != MatchPhase.Complete; step++)
            {

                int         seat   = engine.Pending.Kind == MatchPendingKind.AwaitingEffectChoice ? engine.Rules.PendingChoice.Seat : engine.Rules.SeatOnTurn;
                MatchIntent intent = policy.ChooseAction(engine.BuildSeatView(seat), seat) ?? new EndTurnIntent();

                script.Add($"turn {engine.Turn} action {engine.ActionCount} s{seat} {SelfPlayHarness.Describe(intent)}");
                engine.Submit(seat, intent);
            }

            return string.Join("\n", script);
        }

        /// <summary>
        /// Byte equality rather than a comparison of display strings: two different intents that happen to
        /// describe the same way would otherwise read as one decision, and a description is written for a
        /// person rather than as an identity.
        /// </summary>
        static bool Same(MatchIntent a, MatchIntent b) => SelfPlayHarness.SameIntent(a, b);

        /// <summary> Where in the legal set a choice sits. A null choice is "end the turn", which is always in it. </summary>
        static int IndexInLegalSet(SeatView view, MatchIntent intent)
        {
            MatchIntent looking = intent ?? new EndTurnIntent();

            for (int ndx = 0; ndx < view.LegalActions.Count; ndx++)
            {
                if (Same(view.LegalActions[ndx], looking))
                    return ndx;
            }

            return -1;
        }
    }
}
