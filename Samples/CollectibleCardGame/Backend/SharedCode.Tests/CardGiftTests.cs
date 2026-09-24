using Metaplay.Core.Model;
using Metaplay.Core;
using Metaplay.Core.Player;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    [TestFixture]
    public class CardGiftTests
    {
        static PlayerModel Player(ulong id = 123)
            => PlayerModelUtil.CreateNewPlayerModel<PlayerModel>(MetaTime.FromMillisecondsSinceEpoch(1000),
                TestGameConfig.Shared, EntityId.Create(EntityKindCore.Player, id), "Gift tester");

        [Test]
        public void CompletedMatchesBankProgressAndClaimGrantsExactlyOneUnownedCard()
        {
            PlayerModel player = Player();
            int interval = player.GameConfig.Global.MatchesPerCardGift;
            Assert.That(new PlayerClaimCardGift().Execute(player, true), Is.EqualTo(ActionResults.CardGiftNotReady));
            for (int i = 0; i < interval; i++)
                new PlayerApplyMatchResult(MatchAccountOutcome.Loss, false, 0, null).Execute(player, true);
            HashSet<CardId> owned = new HashSet<CardId>(player.Collection.Keys);
            Assert.That(new PlayerClaimCardGift().Execute(player, false), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Collection.Count, Is.EqualTo(owned.Count));
            Assert.That(new PlayerClaimCardGift().Execute(player, true), Is.EqualTo(MetaActionResult.Success));
            Assert.Multiple(() =>
            {
                Assert.That(player.Collection.Count, Is.EqualTo(owned.Count + 1));
                Assert.That(owned.Contains(player.UnseenCardGift), Is.False);
                Assert.That(player.Collection[player.UnseenCardGift], Is.EqualTo(player.GameConfig.Global.RankMin));
                Assert.That(player.CardGiftProgress, Is.Zero);
                Assert.That(player.CardGiftsClaimed, Is.EqualTo(1));
                Assert.That(new PlayerClaimCardGift().Execute(player, true), Is.EqualTo(ActionResults.CardGiftNotReady));
            });
        }

        [Test]
        public void ConfigControlsTheIntervalAndPracticeEligibility()
        {
            PlayerModel player = Player();
            GlobalConfig global = player.GameConfig.Global;
            int interval = global.MatchesPerCardGift;
            bool practice = global.CardGiftIncludesPractice;
            try
            {
                global.MatchesPerCardGift = 2;
                global.CardGiftIncludesPractice = false;
                new PlayerApplyMatchResult(MatchAccountOutcome.Win, false, 0, null).Execute(player, true);
                Assert.That(player.CardGiftProgress, Is.Zero);
                new PlayerApplyMatchResult(MatchAccountOutcome.Draw, true, 0, null).Execute(player, true);
                Assert.That(CardGiftPolicy.IsReady(player), Is.False);
                new PlayerApplyMatchResult(MatchAccountOutcome.Loss, true, 0, null).Execute(player, true);
                Assert.That(CardGiftPolicy.IsReady(player), Is.True);
                global.MatchesPerCardGift = 0;
                Assert.That(CardGiftPolicy.IsReady(player), Is.False);
                new PlayerApplyMatchResult(MatchAccountOutcome.Win, true, 0, null).Execute(player, true);
                Assert.That(player.CardGiftProgress, Is.EqualTo(2));
            }
            finally
            {
                global.MatchesPerCardGift = interval;
                global.CardGiftIncludesPractice = practice;
            }
        }

        [Test]
        public void ReplayChoosesTheSameCardAndBankedGiftsNeverDuplicate()
        {
            PlayerModel a = Player();
            PlayerModel b = Player();
            int missing = CardGiftPolicy.Candidates(a).Count;
            a.CardGiftProgress = b.CardGiftProgress = missing * a.GameConfig.Global.MatchesPerCardGift;
            for (int i = 0; i < missing; i++)
            {
                new PlayerClaimCardGift().Execute(a, true);
                new PlayerClaimCardGift().Execute(b, true);
                Assert.That(a.UnseenCardGift, Is.EqualTo(b.UnseenCardGift));
                new PlayerDismissCardGift(a.UnseenCardGift).Execute(a, true);
                new PlayerDismissCardGift(b.UnseenCardGift).Execute(b, true);
            }
            Assert.That(CardGiftPolicy.Candidates(a), Is.Empty);
            a.CardGiftProgress = 100;
            Assert.That(new PlayerClaimCardGift().Execute(a, true), Is.EqualTo(ActionResults.CardGiftNotReady));
            Assert.That(a.CardGiftProgress, Is.EqualTo(100));
        }

        [Test]
        public void OutstandingGiftCannotBeOverwrittenOrDismissedByAStaleCard()
        {
            PlayerModel player = Player();
            player.CardGiftProgress = 2 * player.GameConfig.Global.MatchesPerCardGift;
            new PlayerClaimCardGift().Execute(player, true);
            CardId first = player.UnseenCardGift;
            Assert.That(new PlayerClaimCardGift().Execute(player, true), Is.EqualTo(ActionResults.CardGiftNotReady));
            new PlayerDismissCardGift(first).Execute(player, true);
            new PlayerClaimCardGift().Execute(player, true);
            Assert.That(new PlayerDismissCardGift(first).Execute(player, true), Is.EqualTo(ActionResults.CardGiftNotReady));
            Assert.That(player.UnseenCardGift, Is.Not.EqualTo(first));
        }
    }
}
