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
    /// Tests <see cref="AnalyticsCorrelationId"/>, the key that joins the events emitted by one cause.
    /// </summary>
    [TestFixture]
    public class AnalyticsCorrelationTests
    {
        static readonly EntityId Player = EntityId.Create(EntityKindCore.Player, 42);
        static readonly MetaTime Now    = MetaTime.FromMillisecondsSinceEpoch(1_700_000_000_000);

        [Test]
        public void OneCauseGivesOneId()
        {
            AnalyticsCorrelationId minted = AnalyticsCorrelationId.Create(Player, Now, "seating");
            AnalyticsCorrelationId again  = AnalyticsCorrelationId.Create(Player, Now, "seating");

            Assert.That(again, Is.EqualTo(minted));
        }

        [Test]
        public void ADifferentPlayerInstantOrCauseGivesADifferentId()
        {
            AnalyticsCorrelationId baseline = AnalyticsCorrelationId.Create(Player, Now, "seating");

            Assert.That(AnalyticsCorrelationId.Create(EntityId.Create(EntityKindCore.Player, 43), Now, "seating"), Is.Not.EqualTo(baseline));
            Assert.That(AnalyticsCorrelationId.Create(Player, Now + MetaDuration.FromMilliseconds(1), "seating"), Is.Not.EqualTo(baseline));
            Assert.That(AnalyticsCorrelationId.Create(Player, Now, "claim"), Is.Not.EqualTo(baseline));
        }

        /// <summary>
        /// Checks that a created id is never <see cref="AnalyticsCorrelationId.None"/>, which means the event has
        /// no related events. <see cref="AnalyticsCorrelationId.Create"/> must avoid it even for an input that
        /// hashes to zero.
        /// </summary>
        [Test]
        public void AnIdIsNeverTheOneThatMeansNothing()
        {
            Assert.That(AnalyticsCorrelationId.None.IsSet, Is.False);

            for (int index = 0; index < 2000; index++)
            {
                AnalyticsCorrelationId id = AnalyticsCorrelationId.Create(
                    EntityId.Create(EntityKindCore.Player, (ulong)index),
                    Now + MetaDuration.FromMilliseconds(index),
                    "seating");

                Assert.That(id.IsSet, Is.True);
            }
        }

        /// <summary>Checks that distinct causes get distinct ids. A collision would join unrelated rows.</summary>
        [Test]
        public void DistinctCausesDoNotCollide()
        {
            HashSet<AnalyticsCorrelationId> seen = new HashSet<AnalyticsCorrelationId>();

            for (int index = 0; index < 2000; index++)
                seen.Add(AnalyticsCorrelationId.Create(Player, Now + MetaDuration.FromMilliseconds(index), "seating"));

            Assert.That(seen, Has.Count.EqualTo(2000));
        }

        [Test]
        public void ACorrelationIdReadsTheSameInEveryCulture()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;

            try
            {
                AnalyticsCorrelationId id = AnalyticsCorrelationId.Create(Player, Now, "seating");

                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                string invariant = id.ToString();

                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(id.ToString(), Is.EqualTo(invariant));
                Assert.That(invariant, Has.Length.EqualTo(16));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }
    }
}
