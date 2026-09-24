using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests the observation actions a client may submit. They are safe to accept from a client because they
    /// change no player state and carry only closed enums.
    /// </summary>
    [TestFixture]
    public class ClientObservationTests
    {
        static PlayerModel NewPlayer(List<PlayerEventBase> captured)
        {
            PlayerModel player = new PlayerModel();
            player.AnalyticsEventHandler = new AnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>((context, payload) => captured.Add(payload));
            return player;
        }

        [Test]
        public void AScreenViewChangesNothingAtAll()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            byte[] before = TestPlayers.Snapshot(player);

            Assert.That(new PlayerObserveScreenViewed(ShellScreen.Events).Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));

            Assert.That(TestPlayers.Snapshot(player), Is.EqualTo(before), "a client observation moved player state");
            Assert.That(captured, Has.Count.EqualTo(1));
            Assert.That(((PlayerEventScreenViewed)captured[0]).Screen, Is.EqualTo(ShellScreen.Events));
        }

        [Test]
        public void ADryRunEmitsNothing()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            new PlayerObserveScreenViewed(ShellScreen.Events).Execute(player, commit: false);
            new PlayerObservePromotedEntrySelected(PromotedEntryPlacement.Teaser, ShellScreen.Home, ShellScreen.Shop).Execute(player, commit: false);

            Assert.That(captured, Is.Empty, "a dry run wrote an event, so every replay would double-count it");
        }

        [TestCase(ShellScreen.Unknown)]
        [TestCase((ShellScreen)999)]
        public void AScreenOutsideTheClosedSetIsRefused(ShellScreen screen)
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Assert.That(new PlayerObserveScreenViewed(screen).Execute(player, commit: true), Is.EqualTo(ActionResults.UnknownObservation));
            Assert.That(captured, Is.Empty);
        }

        [Test]
        public void APromotedEntryCarriesItsPlacementAndBothScreens()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            byte[] before = TestPlayers.Snapshot(player);

            Assert.That(new PlayerObservePromotedEntrySelected(PromotedEntryPlacement.NextActionCard, ShellScreen.Home, ShellScreen.DailyReward)
                .Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));

            Assert.That(TestPlayers.Snapshot(player), Is.EqualTo(before));

            PlayerEventPromotedEntrySelected observed = (PlayerEventPromotedEntrySelected)captured.Single();
            Assert.That(observed.Placement, Is.EqualTo(PromotedEntryPlacement.NextActionCard));
            Assert.That(observed.From, Is.EqualTo(ShellScreen.Home));
            Assert.That(observed.Destination, Is.EqualTo(ShellScreen.DailyReward));
        }

        [TestCase(PromotedEntryPlacement.Unknown, ShellScreen.Home, ShellScreen.Shop)]
        [TestCase(PromotedEntryPlacement.Teaser, ShellScreen.Unknown, ShellScreen.Shop)]
        [TestCase(PromotedEntryPlacement.Teaser, ShellScreen.Home, (ShellScreen)77)]
        public void APromotedEntryOutsideTheClosedSetIsRefused(PromotedEntryPlacement placement, ShellScreen from, ShellScreen destination)
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Assert.That(new PlayerObservePromotedEntrySelected(placement, from, destination).Execute(player, commit: true), Is.EqualTo(ActionResults.UnknownObservation));
            Assert.That(captured, Is.Empty);
        }
    }
}
