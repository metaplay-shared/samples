using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.League;
using Metaplay.Core.League.Player;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for the seasonal tournament (<c>docs/seasonal-tournament.md</c>): the derived bots that fill a
    /// group, the capped run, the tiebreaks, and the milestone and placement claims.
    /// <para>
    /// The rules are in shared code, so they are tested without a server. The SDK's Leagues framework owns the
    /// season schedule, group assignment and season conclusion. These tests cover the game's rules on top of it.
    /// </para>
    /// </summary>
    [TestFixture]
    public class TournamentTests
    {
        static readonly DivisionIndex Group          = new DivisionIndex(league: 0, season: 12, rank: 0, division: 0);
        static readonly MetaTime      SeasonStartsAt = MetaTime.FromMillisecondsSinceEpoch(1_700_000_000_000);
        static readonly MetaTime      SeasonEndsAt   = SeasonStartsAt + MetaDuration.FromDays(7);

        static MetaTime PartWay(double fraction) => SeasonStartsAt + MetaDuration.FromMilliseconds((long)((SeasonEndsAt - SeasonStartsAt).Milliseconds * fraction));

        /// <summary>
        /// The game config that bot names are drawn from. It is not null, because a null config would test only
        /// the fallback naming path and not the one a real client runs.
        /// </summary>
        static readonly SharedGameConfig Config = TestGameConfig.Build();

        #region Bots keep the group populated

        /// <summary>
        /// A player who joins an empty group sees a full leaderboard: every other seat is a bot, and every bot is
        /// marked as one.
        /// </summary>
        [Test]
        public void AJoiningPlayerSeesAFullGroup()
        {
            TournamentDivisionModel division = Division();
            Seat(division, 1, PlayerId(7), wins: 0, scoredMatches: 0);

            List<TournamentEntrant> standings = division.Standings(PartWay(0.5));

            Assert.That(standings, Has.Count.EqualTo(TournamentRules.GroupSize));
            Assert.That(standings.Count(seat => seat.IsBot), Is.EqualTo(TournamentRules.GroupSize - 1));
            Assert.That(standings.Count(seat => !seat.IsBot), Is.EqualTo(1));
            Assert.That(standings.All(seat => seat.Identity != null && !string.IsNullOrEmpty(seat.Identity.DisplayName)), Is.True,
                "every row has a name to draw");
        }

        /// <summary>
        /// Reading the group again gives the same standings, as after a reload. Bots are derived, not stored, so
        /// every read computes the same values.
        /// </summary>
        [Test]
        public void BotsAreTheSameOnEveryRead()
        {
            TournamentDivisionModel first  = Division();
            TournamentDivisionModel second = Division();

            List<TournamentEntrant> a = first.Standings(PartWay(0.4));
            List<TournamentEntrant> b = second.Standings(PartWay(0.4));

            Assert.That(Describe(b), Is.EqualTo(Describe(a)));
        }

        /// <summary>
        /// A bot's progress only increases during the season, and equals its target when the season ends.
        /// </summary>
        [Test]
        public void BotProgressOnlyMovesForwardAndFinishesOnItsTarget()
        {
            int[] targets = TournamentBots.WinTargets(Group);

            for (int seat = 1; seat <= TournamentRules.GroupSize; seat++)
            {
                int previous = 0;
                for (int step = 0; step <= 20; step++)
                {
                    TournamentEntrant bot = TournamentBots.Seat(Config, Group, seat, targets[seat - 1], SeasonStartsAt, SeasonEndsAt, PartWay(step / 20.0));

                    Assert.That(bot.Wins, Is.GreaterThanOrEqualTo(previous), $"seat {seat} went backwards");
                    Assert.That(bot.ScoredMatches, Is.LessThanOrEqualTo(TournamentRules.ScoredMatchCap));
                    Assert.That(bot.ScoredMatches, Is.GreaterThanOrEqualTo(bot.Wins), "a bot cannot win more matches than it played");
                    previous = bot.Wins;
                }

                TournamentEntrant finished = TournamentBots.Seat(Config, Group, seat, targets[seat - 1], SeasonStartsAt, SeasonEndsAt, SeasonEndsAt);
                Assert.That(finished.Wins, Is.EqualTo(targets[seat - 1]));
                Assert.That(finished.ScoredMatches, Is.EqualTo(TournamentRules.ScoredMatchCap));
            }
        }

        /// <summary>Before the season starts, every bot has zero wins and zero scored matches.</summary>
        [Test]
        public void BotsHaveNotPlayedBeforeTheSeasonStarts()
        {
            foreach (TournamentEntrant bot in TournamentBots.Fill(Config, Group, TournamentRules.GroupSize, new HashSet<int>(), SeasonStartsAt, SeasonEndsAt, SeasonStartsAt - MetaDuration.FromHours(1)))
            {
                Assert.That(bot.Wins, Is.Zero);
                Assert.That(bot.ScoredMatches, Is.Zero);
            }
        }

        /// <summary>
        /// A bot is named from the name vocabulary in the config, and <b>publishing a new vocabulary version
        /// renames every bot</b>. The name is the only bot value that depends on more than the group. Placement is
        /// unaffected, but clients on different config versions show different names.
        /// </summary>
        [Test]
        public void BotNamesComeFromThePublishedVocabularyAndMoveWithIt()
        {
            PlayerPublicIdentity first  = TournamentBots.Identity(TestGameConfig.Build(identity: TestGameConfig.PlayerIdentity(generatorVersion: 1)), Group, seat: 3);
            PlayerPublicIdentity second = TournamentBots.Identity(TestGameConfig.Build(identity: TestGameConfig.PlayerIdentity(generatorVersion: 2)), Group, seat: 3);

            Assert.That(first.DisplayName, Does.Not.StartWith(DisplayNameGenerator.FallbackPrefix),
                "a bot is named from the vocabulary, not from the fallback");
            Assert.That(TournamentBots.Identity(TestGameConfig.Build(identity: TestGameConfig.PlayerIdentity(generatorVersion: 1)), Group, seat: 3).DisplayName,
                Is.EqualTo(first.DisplayName), "the same vocabulary always names the same seat the same way");
            Assert.That(second.DisplayName, Is.Not.EqualTo(first.DisplayName),
                "and a new vocabulary version renames it — the one part of a bot that a publish can move");
        }

        /// <summary>
        /// The bot targets depend only on the group, not on the division's declared participant count.
        /// </summary>
        [Test]
        public void TheBotLadderDoesNotDependOnTheDivisionsDeclaredSize()
        {
            TournamentDivisionModel twenty = Division();
            TournamentDivisionModel odd    = Division();
            odd.DesiredParticipantCount = 12;

            List<TournamentEntrant> full  = twenty.Standings(PartWay(0.5));
            List<TournamentEntrant> small = odd.Standings(PartWay(0.5));

            Assert.That(small, Has.Count.EqualTo(12));

            // Every seat on the smaller board has the same bot as on the full board. Deriving the targets from
            // the division's size would change them.
            foreach (TournamentEntrant seat in small)
            {
                TournamentEntrant same = full.Single(other => other.ParticipantIndex == seat.ParticipantIndex);
                Assert.That(seat.Wins, Is.EqualTo(same.Wins), $"seat {seat.ParticipantIndex} moved");
                Assert.That(seat.Identity.DisplayName, Is.EqualTo(same.Identity.DisplayName));
            }
        }

        /// <summary>No bot target exceeds <see cref="TournamentRules.BotMaxWins"/>.</summary>
        [Test]
        public void NoBotBeatsAFullHumanRun()
        {
            foreach (int target in TournamentBots.WinTargets(Group))
                Assert.That(target, Is.InRange(0, TournamentRules.BotMaxWins));
        }

        /// <summary>
        /// A second player who joins takes the next seat, and every remaining bot is unchanged. The leaderboard
        /// must not reshuffle when a player joins.
        /// </summary>
        [Test]
        public void AHumanJoiningLeavesEveryOtherBotUntouched()
        {
            TournamentDivisionModel before = Division();
            Seat(before, 1, PlayerId(7), wins: 3, scoredMatches: 5);

            TournamentDivisionModel after = Division();
            Seat(after, 1, PlayerId(7), wins: 3, scoredMatches: 5);
            Seat(after, 2, PlayerId(8), wins: 0, scoredMatches: 0);

            IEnumerable<string> botsBefore = Describe(before.Standings(PartWay(0.5)).Where(seat => seat.IsBot && seat.ParticipantIndex != 2));
            IEnumerable<string> botsAfter  = Describe(after.Standings(PartWay(0.5)).Where(seat => seat.IsBot));

            Assert.That(botsAfter, Is.EqualTo(botsBefore));
        }

        /// <summary>
        /// Humans take the lowest free seats, and bot targets increase with seat number, so a joining human
        /// replaces the weakest remaining bot rather than one in contention.
        /// </summary>
        [Test]
        public void TheSeatAHumanTakesHoldsTheLowestRankedBot()
        {
            int[] targets = TournamentBots.WinTargets(Group);

            for (int seat = 2; seat <= TournamentRules.GroupSize; seat++)
                Assert.That(targets[seat - 1], Is.GreaterThanOrEqualTo(targets[seat - 2]));
        }

        /// <summary>Two groups in the same season get different bot targets.</summary>
        [Test]
        public void TwoGroupsAreNotTheSameBoard()
        {
            int[] first  = TournamentBots.WinTargets(new DivisionIndex(0, 12, 0, 0));
            int[] second = TournamentBots.WinTargets(new DivisionIndex(0, 12, 0, 1));

            Assert.That(second, Is.Not.EqualTo(first));
        }

        #endregion

        #region The order the group is read in

        /// <summary>
        /// More wins places ahead. On equal wins, fewer losses places ahead. On equal wins and losses, the earlier
        /// finisher places ahead. The stable seat number breaks the last tie, so the order is total.
        /// </summary>
        [TestCase(1, 5, 10, 0, 2, 4, 10, 0)]
        [TestCase(1, 4, 6,  0, 2, 4, 10, 0)]
        [TestCase(1, 4, 6,  2, 2, 4, 6,  9)]
        [TestCase(4, 4, 6,  0, 9, 4, 6,  0)]
        public void TheOrderIsTotal(int aheadSeat, int aheadWins, int aheadScored, int aheadLastWinHours, int behindSeat, int behindWins, int behindScored, int behindLastWinHours)
        {
            TournamentEntrant ahead  = new TournamentEntrant(aheadSeat, false, null, aheadWins, aheadScored, SeasonStartsAt + MetaDuration.FromHours(aheadLastWinHours));
            TournamentEntrant behind = new TournamentEntrant(behindSeat, false, null, behindWins, behindScored, SeasonStartsAt + MetaDuration.FromHours(behindLastWinHours));

            Assert.That(TournamentStandings.Compare(ahead, behind), Is.LessThan(0));
            Assert.That(TournamentStandings.Compare(behind, ahead), Is.GreaterThan(0));
        }

        [Test]
        public void RankingProducesOnePlaceEach()
        {
            List<TournamentEntrant> ranked = TournamentStandings.Rank(new[]
            {
                Seat(3, wins: 2, scoredMatches: 4),
                Seat(1, wins: 7, scoredMatches: 9),
                Seat(2, wins: 7, scoredMatches: 8),
            });

            Assert.That(ranked.Select(seat => seat.ParticipantIndex), Is.EqualTo(new[] { 2, 1, 3 }), "fewer losses breaks the tie on wins");
            Assert.That(TournamentStandings.PlacementOf(ranked, 2), Is.EqualTo(1));
            Assert.That(TournamentStandings.PlacementOf(ranked, 3), Is.EqualTo(3));
            Assert.That(TournamentStandings.PlacementOf(ranked, 99), Is.Zero, "a seat that is not in the group has no place");
        }

        #endregion

        #region The capped run

        [Test]
        public void AWinScoresAPointAndALossScoresNone()
        {
            PlayerModel player = Joined();

            Record(player, 1, rank: 0);
            Record(player, 2, rank: 2);

            Assert.That(player.Tournament.Wins, Is.EqualTo(1));
            Assert.That(player.Tournament.ScoredMatches, Is.EqualTo(2));
            Assert.That(player.Tournament.Losses, Is.EqualTo(1));
        }

        [Test]
        public void OnlyTheFirstTenMatchesCount()
        {
            PlayerModel player = Joined();

            for (int index = 1; index <= 14; index++)
                Record(player, index, rank: 0);

            Assert.That(player.Tournament.ScoredMatches, Is.EqualTo(TournamentRules.ScoredMatchCap));
            Assert.That(player.Tournament.Wins, Is.EqualTo(TournamentRules.ScoredMatchCap));
            Assert.That(player.Tournament.IsCapReached, Is.True);
            Assert.That(player.Record.GamesPlayed, Is.EqualTo(14), "play past the cap is still ordinary play");
        }

        [Test]
        public void AMatchFinishedBeforeJoiningDoesNotCount()
        {
            PlayerModel player = NewPlayer();

            Record(player, 1, rank: 0);

            Assert.That(player.Tournament.HasJoined, Is.False);
            Assert.That(player.Tournament.ScoredMatches, Is.Zero);
        }

        /// <summary>
        /// A result whose delivery was retried can arrive after the player joined. A game completed before the
        /// join belongs to an earlier season, so it uses no attempt in this one.
        /// </summary>
        [Test]
        public void AResultDeliveredAfterJoiningForAGameCompletedBeforeItDoesNotCount()
        {
            PlayerModel player = Joined();

            TestPlayers.MatchResult(1, player.Tournament.JoinedAt - MetaDuration.FromSeconds(1)).Execute(player, commit: true);

            Assert.That(player.Tournament.ScoredMatches, Is.Zero);
            Assert.That(player.Tournament.Wins, Is.Zero);
        }

        [Test]
        public void JoiningTheSameSeasonTwiceDoesNotResetTheRun()
        {
            PlayerModel player = Joined();
            Record(player, 1, rank: 0);

            MetaActionResult again = Join(player, commit: true);

            Assert.That(again, Is.EqualTo(ActionResults.TournamentAlreadyJoined));
            Assert.That(player.Tournament.ScoredMatches, Is.EqualTo(1));
        }

        /// <summary>
        /// The joined season stays recorded after the season ends, so a late match cannot score into a finished
        /// season. Screens check <c>IsRunningAt</c> instead, so a player whose season is over is offered the
        /// next one.
        /// </summary>
        [Test]
        public void AFinishedSeasonIsStillJoinedButIsNoLongerRunning()
        {
            PlayerModel player = Joined();

            Assert.That(player.Tournament.IsRunningAt(PartWay(0.5)), Is.True);
            Assert.That(player.Tournament.IsRunningAt(SeasonEndsAt), Is.False);
            Assert.That(player.Tournament.HasJoined, Is.True, "the stamp stays, or a late match would score into a finished season");

            TestPlayers.MatchResult(9, SeasonEndsAt + MetaDuration.FromSeconds(1)).Execute(player, commit: true);

            Assert.That(player.Tournament.ScoredMatches, Is.Zero);
            Assert.That(player.Record.GamesPlayed, Is.EqualTo(1), "the late match is still ordinary play");
        }

        [Test]
        public void ANewSeasonStartsTheRunOver()
        {
            PlayerModel player = Joined();
            Record(player, 1, rank: 0);
            ClaimMilestone(player, 0, commit: true);

            new PlayerTournamentJoined(13, DivisionId(1), 0, SeasonStartsAt, SeasonEndsAt, TournamentRewardTableId.FromString("tournament"))
                .Execute(player, commit: true);

            Assert.That(player.Tournament.Season, Is.EqualTo(13));
            Assert.That(player.Tournament.ScoredMatches, Is.Zero);
            Assert.That(player.Tournament.ClaimedMilestoneIndices, Is.Empty);
            Assert.That(player.Tournament.SeasonsEntered, Is.EqualTo(2));
        }

        #endregion

        #region Claims

        [Test]
        public void AMilestoneIsPaidOnceAndOnceOnly()
        {
            PlayerModel player = Joined();
            int         before = player.Wallet.Coins;

            Record(player, 1, rank: 0);

            Assert.That(ClaimMilestone(player, 0, commit: true), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Coins, Is.EqualTo(before + 250));

            Assert.That(ClaimMilestone(player, 0, commit: true), Is.EqualTo(ActionResults.TournamentRewardAlreadyClaimed));
            Assert.That(player.Wallet.Coins, Is.EqualTo(before + 250), "a second claim pays nothing");
        }

        /// <summary>
        /// A grant the wallet refuses leaves the milestone unclaimed, so it can be collected later.
        /// <para>
        /// This is why the game does not use the SDK's claim path: that path marks a reward collected even when
        /// <c>Apply</c> changed nothing, so a refused grant would silently lose the reward. Here the refusal is
        /// the action's result, nothing is marked, and the claim works once the wallet has room.
        /// </para>
        /// </summary>
        [Test]
        public void AMilestoneTheWalletRefusesIsNotMarkedClaimed()
        {
            PlayerModel player = Joined(config: TestGameConfig.Build(new RewardBundle(CurrencyAmount.Coins(TestGameConfig.MaxCoins))));
            Record(player, 1, rank: 0);

            Assert.That(ClaimMilestone(player, 0, commit: true), Is.EqualTo(ActionResults.WalletCapExceeded));
            Assert.That(player.Wallet.Coins, Is.EqualTo(TestGameConfig.MaxCoins), "a refused grant moves nothing");
            Assert.That(player.Tournament.ClaimedMilestoneIndices, Is.Empty, "a refused claim is still claimable");
        }

        /// <summary>A placement reward the wallet refuses is not marked claimed and is still pending.</summary>
        [Test]
        public void APlacementTheWalletRefusesIsNotMarkedClaimed()
        {
            PlayerModel player = Joined(config: TestGameConfig.Build(new RewardBundle(CurrencyAmount.Coins(TestGameConfig.MaxCoins))));
            Conclude(player, DivisionId(0), placement: 1);

            Assert.That(ClaimPlacement(player, DivisionId(0), commit: true), Is.EqualTo(ActionResults.WalletCapExceeded));
            Assert.That(player.Wallet.Coins, Is.EqualTo(TestGameConfig.MaxCoins));
            Assert.That(player.Tournament.HasClaimedPlacement(DivisionId(0)), Is.False);
            Assert.That(player.PendingTournamentReward()?.DivisionId, Is.EqualTo(DivisionId(0)), "it is still owed");
            Assert.That(player.Tournament.SeasonsWon, Is.Zero, "and the victory is not recorded either");
        }

        /// <summary>
        /// A claim for a season whose reward table has been removed is refused instead of paying from the current
        /// table. Milestones are claimed by index, so the same index in another table is a different reward.
        /// </summary>
        [Test]
        public void AClaimAgainstAWithdrawnTableRefuses()
        {
            PlayerModel player = NewPlayer();
            new PlayerTournamentJoined(12, DivisionId(0), 0, SeasonStartsAt, SeasonEndsAt, TournamentRewardTableId.FromString("tournament.retired"))
                .Execute(player, commit: true);
            Record(player, 1, rank: 0);

            Assert.That(ClaimMilestone(player, 0, commit: true), Is.EqualTo(ActionResults.NoSuchTournamentMilestone));
            Assert.That(player.Wallet.Coins, Is.EqualTo(TestGameConfig.StartingWallet().AmountOf(CurrencyType.Coins)));
        }

        [Test]
        public void AMilestoneCannotBeClaimedBeforeItIsReached()
        {
            PlayerModel player = Joined();

            Assert.That(ClaimMilestone(player, 0, commit: true), Is.EqualTo(ActionResults.TournamentMilestoneNotReached));
            Assert.That(player.Wallet.Coins, Is.EqualTo(TestGameConfig.StartingWallet().AmountOf(CurrencyType.Coins)));
        }

        /// <summary>
        /// The dry run changes nothing and logs nothing. The SDK executes an action as a dry run and then a
        /// commit, so a claim that paid on the dry run would pay twice.
        /// </summary>
        [Test]
        public void TheDryRunPassMovesNothing()
        {
            List<PlayerEventBase> events = new List<PlayerEventBase>();
            PlayerModel           player = Joined(events);

            Record(player, 1, rank: 0);
            events.Clear();

            int before = player.Wallet.Coins;
            Assert.That(ClaimMilestone(player, 0, commit: false), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Wallet.Coins, Is.EqualTo(before));
            Assert.That(player.Tournament.ClaimedMilestoneIndices, Is.Empty);
            Assert.That(events, Is.Empty);
        }

        /// <summary>A full run reaches every milestone, and claiming them pays the whole participation track.</summary>
        [Test]
        public void AFullRunPaysTheWholeParticipationTrack()
        {
            PlayerModel player = Joined();
            int         coins  = player.Wallet.Coins;
            int         tokens = player.Wallet.SpinTokens;

            for (int index = 1; index <= TournamentRules.ScoredMatchCap; index++)
                Record(player, index, rank: 2);

            for (int milestone = 0; milestone < 4; milestone++)
                Assert.That(ClaimMilestone(player, milestone, commit: true), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Wallet.Coins - coins, Is.EqualTo(1_000));
            Assert.That(player.Wallet.SpinTokens - tokens, Is.EqualTo(1));
        }

        [Test]
        public void APlacementRewardIsPaidOnceAndOnceOnly()
        {
            PlayerModel player = Joined();
            Conclude(player, DivisionId(0), placement: 1);

            int coins = player.Wallet.Coins;
            int gems  = player.Wallet.Gems;

            Assert.That(ClaimPlacement(player, DivisionId(0), commit: true), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Coins - coins, Is.EqualTo(1_500));
            Assert.That(player.Wallet.Gems - gems, Is.EqualTo(50));

            Assert.That(ClaimPlacement(player, DivisionId(0), commit: true), Is.EqualTo(ActionResults.TournamentRewardAlreadyClaimed));
            Assert.That(player.Wallet.Coins - coins, Is.EqualTo(1_500));
        }

        /// <summary>
        /// Joining a new season does not remove an older pending placement reward. The result is kept in the
        /// player's tournament history, and joining the next season does not remove history entries.
        /// </summary>
        [Test]
        public void APendingRewardSurvivesANewSeason()
        {
            PlayerModel player = Joined();
            Conclude(player, DivisionId(0), placement: 2);

            new PlayerTournamentJoined(13, DivisionId(1), 0, SeasonStartsAt, SeasonEndsAt, TournamentRewardTableId.FromString("tournament"))
                .Execute(player, commit: true);

            Assert.That(player.PendingTournamentReward()?.DivisionId, Is.EqualTo(DivisionId(0)));

            int coins = player.Wallet.Coins;
            Assert.That(ClaimPlacement(player, DivisionId(0), commit: true), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Coins - coins, Is.EqualTo(1_000));
            Assert.That(player.PendingTournamentReward(), Is.Null);
        }

        /// <summary>A placement outside every reward band earns no placement reward.</summary>
        [Test]
        public void APlacementOutsideTheBandsEarnsNothing()
        {
            PlayerModel player = Joined();
            Conclude(player, DivisionId(0), placement: 11);

            Assert.That(player.PendingTournamentReward(), Is.Null);
            Assert.That(ClaimPlacement(player, DivisionId(0), commit: true), Is.EqualTo(ActionResults.NoTournamentReward));
        }

        /// <summary>Each win records a victory, but the champion frame is recorded only once.</summary>
        [Test]
        public void ARepeatWinRecordsAnotherVictoryButNotASecondFrame()
        {
            PlayerModel player = Joined();

            Conclude(player, DivisionId(0), placement: 1, cosmetic: CosmeticId.FromString("frame.champion"));
            Conclude(player, DivisionId(1), placement: 1, cosmetic: CosmeticId.FromString("frame.champion"));

            ClaimPlacement(player, DivisionId(0), commit: true);
            ClaimPlacement(player, DivisionId(1), commit: true);

            Assert.That(player.Tournament.SeasonsWon, Is.EqualTo(2));
            Assert.That(player.Tournament.EarnedCosmetics, Has.Count.EqualTo(1));
        }

        /// <summary>A resolved season writes one analytics event, however many times it is observed.</summary>
        [Test]
        public void AResolvedSeasonIsReportedOnce()
        {
            List<PlayerEventBase> events = new List<PlayerEventBase>();
            PlayerModel           player = Joined(events);

            Conclude(player, DivisionId(0), placement: 3);
            events.Clear();

            Assert.That(new PlayerTournamentSeasonConcluded(DivisionId(0)).Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));
            Assert.That(new PlayerTournamentSeasonConcluded(DivisionId(0)).Execute(player, commit: true), Is.EqualTo(ActionResults.TournamentResultAlreadyReported));

            Assert.That(events.OfType<PlayerEventTournamentResolved>().Count(), Is.EqualTo(1));
        }

        #endregion

        #region Analytics

        [Test]
        public void EveryCountedMatchWritesOneRow()
        {
            List<PlayerEventBase> events = new List<PlayerEventBase>();
            PlayerModel           player = Joined(events);
            events.Clear();

            Record(player, 1, rank: 0);
            Record(player, 1, rank: 0);

            List<PlayerEventTournamentMatchCounted> counted = events.OfType<PlayerEventTournamentMatchCounted>().ToList();

            Assert.That(counted, Has.Count.EqualTo(1));
            Assert.That(counted[0].Attempt, Is.EqualTo(1));
            Assert.That(counted[0].WinsAfter, Is.EqualTo(1));
            Assert.That(counted[0].IsWin, Is.True);
        }

        /// <summary>
        /// A claim's economy transaction event and its milestone claim event share one correlation id.
        /// </summary>
        [Test]
        public void AClaimsRowsShareOneCorrelation()
        {
            List<PlayerEventBase> events = new List<PlayerEventBase>();
            PlayerModel           player = Joined(events);

            Record(player, 1, rank: 0);
            events.Clear();

            ClaimMilestone(player, 0, commit: true);

            PlayerEventTournamentMilestoneClaimed claim = events.OfType<PlayerEventTournamentMilestoneClaimed>().Single();
            PlayerEventEconomyTransaction         row   = events.OfType<PlayerEventEconomyTransaction>().Single();

            Assert.That(claim.Correlation, Is.EqualTo(row.Correlation));
            Assert.That(claim.Correlation.IsSet, Is.True);
            Assert.That(row.Feature, Is.EqualTo(EconomyFeature.Tournament));
            Assert.That(row.Reason, Is.EqualTo(EconomyReason.TournamentReward));
        }

        #endregion

        #region Fixtures

        static EntityId PlayerId(int value) => EntityId.Create(EntityKindCore.Player, (ulong)value);
        static EntityId DivisionId(int division) => new DivisionIndex(0, 12 + division, 0, 0).ToEntityId();

        static TournamentEntrant Seat(int seat, int wins, int scoredMatches) =>
            new TournamentEntrant(seat, false, null, wins, scoredMatches, SeasonStartsAt);

        static IEnumerable<string> Describe(IEnumerable<TournamentEntrant> seats) =>
            seats.Select(seat => $"{seat.ParticipantIndex}:{seat.Identity?.DisplayName}:{seat.Wins}/{seat.ScoredMatches}@{seat.LastWinAt}").ToList();

        static TournamentDivisionModel Division()
        {
            TournamentDivisionModel division = new TournamentDivisionModel
            {
                GameConfig              = TestGameConfig.Build(),
                DivisionIndex           = Group,
                StartsAt                = SeasonStartsAt,
                EndsAt                  = SeasonEndsAt,
                DesiredParticipantCount = TournamentRules.GroupSize,
            };

            return division;
        }

        static void Seat(TournamentDivisionModel division, int participantIndex, EntityId playerId, int wins, int scoredMatches)
        {
            TournamentParticipantState participant = division.AddOrUpdateParticipant(
                participantIndex, playerId, new TournamentAvatar(PlayerPublicIdentity.ForBot(playerId, $"Player{participantIndex}", avatarId: null)));

            participant.PlayerContribution = new TournamentContribution
            {
                Wins          = wins,
                ScoredMatches = scoredMatches,
                LastWinAt     = wins > 0 ? PartWay(0.25) : MetaTime.Epoch,
            };
        }

        static PlayerModel NewPlayer(List<PlayerEventBase> events = null, SharedGameConfig config = null)
        {
            PlayerModel player = TestPlayers.New(SeasonStartsAt, config, events);
            player.PlayerSubClientStates[ClientSlotGame.Tournament] = new TournamentClientState();
            return player;
        }

        static MetaActionResult Join(PlayerModel player, bool commit) =>
            new PlayerTournamentJoined(12, DivisionId(0), 0, SeasonStartsAt, SeasonEndsAt, TournamentRewardTableId.FromString("tournament"))
                .Execute(player, commit);

        static PlayerModel Joined(List<PlayerEventBase> events = null, SharedGameConfig config = null)
        {
            PlayerModel player = NewPlayer(events, config);
            Join(player, commit: true);
            return player;
        }

        /// <summary>Record one finished match, which is the only way a tournament attempt is used.</summary>
        static void Record(PlayerModel player, int match, int rank) =>
            TestPlayers.MatchResult(match, PartWay(0.1), rank).Execute(player, commit: true);

        static MetaActionResult ClaimMilestone(PlayerModel player, int index, bool commit) =>
            new PlayerTournamentMilestoneClaim(index).Execute(player, commit);

        static MetaActionResult ClaimPlacement(PlayerModel player, EntityId divisionId, bool commit) =>
            new PlayerTournamentPlacementClaim(divisionId).Execute(player, commit);

        /// <summary>
        /// Add a concluded season to the player's tournament history, as the SDK's synchronized server action does
        /// when the group's result reaches the player.
        /// </summary>
        static void Conclude(PlayerModel player, EntityId divisionId, int placement, CosmeticId cosmetic = null)
        {
            TournamentRewardTableInfo table = TestGameConfig.TournamentRewards();
            TournamentPlacementInfo   band  = table.BandFor(placement);

            TournamentHistoryEntry entry = new TournamentHistoryEntry(
                divisionId,
                DivisionIndex.FromEntityId(divisionId),
                placement,
                wins: 6,
                scoredMatches: 10,
                humanCount: 4,
                groupSize: TournamentRules.GroupSize,
                reward: band?.Reward,
                rewardCosmetic: cosmetic,
                rewardTable: table.Id);

            ((TournamentClientState)player.PlayerSubClientStates[ClientSlotGame.Tournament]).HistoricalDivisions.Add(entry);
        }

        #endregion
    }
}
