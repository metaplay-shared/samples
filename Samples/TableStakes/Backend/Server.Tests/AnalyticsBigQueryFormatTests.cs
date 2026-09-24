using Metaplay.Cloud.Analytics;
using Metaplay.Core;
using Metaplay.Core.Analytics;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Server.Tests
{
    /// <summary>
    /// Builds the BigQuery formatters over every analytics event, the same way the server does at startup.
    /// <para>
    /// The formatters refuse payload fields that BigQuery has no column type for, such as <c>ulong</c>. The server
    /// builds them in <c>Application</c>'s startup singletons, so such a field stops the server from booting. This
    /// fixture catches that in a test instead.
    /// </para>
    /// </summary>
    [TestFixture]
    public class AnalyticsBigQueryFormatTests
    {
        static AnalyticsEventRegistry Registry => MetaplayServices.Get<AnalyticsEventRegistry>();

        /// <summary>
        /// The registry contains the game's own events. Otherwise the tests below would pass on the SDK's events
        /// alone and check nothing.
        /// </summary>
        [Test]
        public void TheRegistryHoldsTheGamesOwnEvents()
        {
            IReadOnlyList<Type> gameEvents = Registry.AllEventSpecs
                .Select(spec => spec.Type)
                .Where(type => !type.Assembly.GetName().Name.StartsWith("Metaplay.", StringComparison.Ordinal))
                .ToList();

            Assert.That(gameEvents, Is.Not.Empty, "no game analytics events were loaded, so the format checks below prove nothing");
        }

        /// <summary>
        /// Builds the v1 formatter over every registered event, as the server does at boot. It throws on the first
        /// payload field whose type BigQuery cannot store.
        /// </summary>
        [Test]
        public void EveryEventFormatsForBigQueryV1()
        {
            Assert.DoesNotThrow(() => new BigQueryFormatter(Registry));
        }

        /// <summary>The same check for the v2 formatter, which the server also builds.</summary>
        [Test]
        public void EveryEventFormatsForBigQueryV2()
        {
            Assert.DoesNotThrow(() => new BigQueryFormatterV2(Registry));
        }
    }
}
