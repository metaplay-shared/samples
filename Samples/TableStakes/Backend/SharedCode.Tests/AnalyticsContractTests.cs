using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Enforces the analytics contract in <c>docs/analytics.md</c>: the event code registry, aliases, Dashboard
    /// keywords, payload privacy, descriptions and serialization. Each rule is a test so that a change that
    /// breaks it fails the build instead of relying on review.
    /// </summary>
    [TestFixture]
    public class AnalyticsContractTests
    {
        static bool IsSdkName(string name) => name != null && name.StartsWith("Metaplay.", StringComparison.Ordinal);

        /// <summary>
        /// Every loaded assembly that could declare one of this game's analytics events: each non-SDK assembly
        /// that references the SDK.
        /// <para>
        /// The list is a scan rather than a fixed assembly so that an event declared in the server assembly is
        /// covered too. A scan only sees loaded assemblies, so this fixture is also compiled into
        /// <c>Server.Tests</c>.
        /// </para>
        /// </summary>
        static IReadOnlyList<Assembly> GameAssemblies =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .Where(assembly => !IsSdkName(assembly.GetName().Name))
                .Where(assembly => assembly.GetReferencedAssemblies().Any(reference => IsSdkName(reference.Name)))
                .ToList();

        /// <summary>Returns the loadable types of <paramref name="assembly"/>, skipping types whose dependencies fail to load.</summary>
        static IEnumerable<Type> TypesOf(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        }

        static bool IsGameType(Type type) => type != null && GameAssemblies.Contains(type.Assembly);

        /// <summary>Every <c>[AnalyticsEvent]</c> type declared in <see cref="GameAssemblies"/>.</summary>
        static IReadOnlyList<Type> DeclaredEvents =>
            GameAssemblies
                .SelectMany(TypesOf)
                .Where(type => type.GetCustomAttribute<AnalyticsEventAttribute>(inherit: false) != null)
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ToList();

        /// <summary>Every analytics event the SDK declares.</summary>
        static IReadOnlyList<Type> SdkEvents =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && IsSdkName(assembly.GetName().Name))
                .SelectMany(TypesOf)
                .Where(type => type.GetCustomAttribute<AnalyticsEventAttribute>(inherit: false) != null)
                .ToList();

        static AnalyticsEventAttribute AttributeOf(Type type) => type.GetCustomAttribute<AnalyticsEventAttribute>(inherit: false);

        static IReadOnlyList<string> KeywordsOf(Type type) =>
            type.GetCustomAttributes<AnalyticsEventKeywordsAttribute>(inherit: false)
                .SelectMany(attribute => attribute.Keywords)
                .ToList();

        /// <summary>Every keyword an event may carry: the domains and the qualifiers.</summary>
        static IReadOnlyList<string> Vocabulary => AnalyticsKeywords.Domains.Concat(AnalyticsKeywords.Qualifiers).ToList();

        /// <summary>The keys that more than one item of <paramref name="items"/> has.</summary>
        static IEnumerable<TKey> Duplicates<TItem, TKey>(IEnumerable<TItem> items, Func<TItem, TKey> key, IEqualityComparer<TKey> comparer = null) =>
            items.GroupBy(key, comparer).Where(group => group.Count() > 1).Select(group => group.Key);

        /// <summary>
        /// The <c>[MetaMember]</c> properties and fields of an event's payload, including those of intermediate
        /// base classes. The walk stops at <see cref="PlayerEventBase"/>, which the SDK owns.
        /// </summary>
        static IReadOnlyList<MemberInfo> PayloadOf(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            List<MemberInfo> payload = new List<MemberInfo>();

            for (Type level = type; level != null && level != typeof(PlayerEventBase) && level != typeof(object); level = level.BaseType)
            {
                payload.AddRange(level.GetProperties(flags).Cast<MemberInfo>()
                    .Concat(level.GetFields(flags))
                    .Where(member => member.GetCustomAttribute<MetaMemberAttribute>(inherit: false) != null));
            }

            return payload;
        }

        /// <summary>
        /// The <see cref="IStringId"/> types a payload may carry, by exact type.
        /// <para>
        /// The analytics export writes a <see cref="IStringId"/> as a string, so <see cref="ReachableFrom"/>
        /// treats one as free text unless it is on this list. Each type here has been checked to hold only a
        /// game config id, with no path from player input.
        /// </para>
        /// </summary>
        static readonly IReadOnlyList<Type> VettedConfigIdTypes = new[]
        {
            typeof(EconomyContentId),

            // A cosmetic's catalogue id, authored in game config. The identity event carries the starting avatar.
            typeof(CosmeticId),

            // The seasonal tournament's reward table, authored in game config. Tournament events carry it so a
            // claim can be matched to the table that promised it.
            typeof(TournamentRewardTableId),

            // The daily-reward table and one of its steps, authored in GameConfigSource/. The claim event carries
            // both.
            typeof(DailyRewardTableId),
            typeof(DailyRewardStepId),

            // The ids a mission row carries. MissionId and MissionSetId are authored in game config.
            // MissionInstanceId is built from a cadence, a schedule boundary and a config mission id, so none of
            // it comes from a player.
            typeof(MissionId),
            typeof(MissionSetId),
            typeof(MissionInstanceId),

            // The first-week schedule and one of its days, authored in GameConfigSource/. Both first-week events
            // carry them, because a player stays on the schedule they started, which can differ from the active one.
            typeof(FirstWeekScheduleId),
            typeof(FirstWeekDayId),

            // The wheel table and one of its sectors, authored in GameConfigSource/. The spin-resolved event
            // carries both.
            typeof(WheelTableId),
            typeof(WheelSectorId),
        };

        /// <summary>
        /// Every type a payload field can reach: through a nullable, an array element, a generic argument, the
        /// string inside an unvetted <see cref="IStringId"/>, and the payload members of this game's own types. The
        /// walk does not enter SDK types.
        /// <para>
        /// The string inside a <see cref="IStringId"/> has no <c>[MetaMember]</c>, so following members alone would
        /// not find it. The walk adds <see cref="string"/> for every <see cref="IStringId"/> that is not in
        /// <see cref="VettedConfigIdTypes"/>.
        /// </para>
        /// </summary>
        static IEnumerable<Type> ReachableFrom(Type type, HashSet<Type> visited)
        {
            if (type == null || !visited.Add(type))
                yield break;

            yield return type;

            if (typeof(IStringId).IsAssignableFrom(type) && !VettedConfigIdTypes.Contains(type))
                yield return typeof(string);

            Type underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
                foreach (Type reached in ReachableFrom(underlying, visited))
                    yield return reached;

            if (type.IsArray)
                foreach (Type reached in ReachableFrom(type.GetElementType(), visited))
                    yield return reached;

            if (type.IsGenericType)
                foreach (Type argument in type.GetGenericArguments())
                foreach (Type reached in ReachableFrom(argument, visited))
                    yield return reached;

            if (IsGameType(type))
                foreach (MemberInfo member in PayloadOf(type))
                foreach (Type reached in ReachableFrom(MemberType(member), visited))
                    yield return reached;
        }

        static Type MemberType(MemberInfo member) =>
            member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;

        /// <summary>One instance of every event the game emits, for the tests that need a real payload.</summary>
        static IReadOnlyList<PlayerEventBase> RepresentativeEvents()
        {
            EntityId               match       = EntityId.Create(EntityKindGame.Match, 12345);
            AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(EntityId.Create(EntityKindCore.Player, 7), MetaTime.FromMillisecondsSinceEpoch(1_700_000_000_000), "seating");

            return new PlayerEventBase[]
            {
                new PlayerEventMatchStarted(match, botSeats: 2, MetaDuration.FromSeconds(7), correlation),
                new PlayerEventMatchFinished(match, MatchPhase.Ended, rank: 1, tricksWon: 2, humanOpponents: 1, finishedByPlayer: true, MatchSeatLossReason.None, MetaDuration.FromSeconds(210)),
                new PlayerEventMatchSeatLost(match, MatchSeatLossReason.DeliberateLeave),
                new PlayerEventMatchmakingWait(MetaDuration.FromSeconds(7), humansGathered: 2, wasSeated: true, correlation),
                new PlayerEventScreenViewed(ShellScreen.Home),
                new PlayerEventPromotedEntrySelected(PromotedEntryPlacement.NextActionCard, ShellScreen.Home, ShellScreen.SpinWheel),
                new PlayerEventEconomyTransaction(CurrencyType.Coins, CurrencyFlow.Sink, amount: 2500, balanceBefore: 3000, balanceAfter: 500, EconomyReason.CosmeticPurchase, EconomyFeature.Cosmetics, EconomyContentId.FromString("frame.brass"), correlation),
                new PlayerEventEconomySpendRejected(CurrencyType.Gems, requested: 500, balance: 100, EconomyReason.OfferPurchase, EconomyFeature.Offers, EconomyContentId.FromString("offer.lucky_spin"), correlation),
                new PlayerEventIdentityInitialized(generatorVersion: 1, nameLength: 11, usedFallback: false, avatarId: null),
                new PlayerEventNameChangeRejected(DisplayNameRefusal.ReservedName, submittedLength: 8, nameChangeCount: 2),
                new PlayerEventNameChangeAccepted(DisplayNameOrigin.Generated, previousLength: 11, newLength: 7, nameChangeCount: 1),
                new PlayerEventTournamentJoined(season: 12, group: 0, elapsedTenths: 3, TournamentRewardTableId.FromString("tournament.v1")),
                new PlayerEventTournamentMatchCounted(season: 12, match, isWin: true, attempt: 4, winsAfter: 3),
                new PlayerEventTournamentMilestoneClaimed(season: 12, milestone: 1, scoredMatches: 3, TournamentRewardTableId.FromString("tournament.v1"), correlation),
                new PlayerEventTournamentResolved(season: 12, placement: 4, wins: 6, scoredMatches: 10, humanCount: 3, botCount: 17),
                new PlayerEventTournamentPlacementRewardClaimed(season: 12, placement: 1, band: 1, TournamentRewardTableId.FromString("tournament.v1"), CosmeticId.FromString("frame.champion"), correlation),
                new PlayerEventDailyRewardClaimed(
                    DailyRewardTableId.FromString("daily.v1"), DailyRewardStepId.FromString("daily.v1.step7"), step: 7,
                    streakBefore: 6, streakAfter: 7, resetStreak: false, usedGrace: true, completedCycle: true,
                    claimOrdinal: 7, correlation),
                new PlayerEventCosmeticPurchased(
                    correlation, CosmeticId.FromString("frame.sapphire"), CosmeticKind.Frame,
                    CurrencyType.Coins, price: 2500, balanceAfter: 500, ownedCount: 2),
                new PlayerEventCosmeticEquipped(
                    CosmeticId.FromString("frame.sapphire"), CosmeticKind.Frame, CosmeticId.FromString("frame.silver"), onPurchase: true),
                new PlayerEventMissionCompleted(
                    MissionInstanceId.FromString("Daily:1756944000000:daily.win1"), MissionSetId.FromString("missions.daily.v1"), MissionId.FromString("daily.win1"),
                    MissionCadence.Daily, MissionObjective.MatchesWon, target: 1, progressBefore: 0, progressAfter: 1),
                new PlayerEventMissionRewardClaimed(
                    correlation, MissionInstanceId.FromString("Daily:1756944000000:daily.win1"), MissionSetId.FromString("missions.daily.v1"), MissionId.FromString("daily.win1"),
                    MissionCadence.Daily, claimOrdinal: 3, inGrace: false),
                new PlayerEventFirstWeekDayCompleted(
                    FirstWeekScheduleId.FromString("firstweek.v1"), FirstWeekDayId.FromString("firstweek.v1.day3"), day: 3,
                    target: 2, progressBefore: 1, progressAfter: 2, dayIndex: 2, isFinalDay: false),
                new PlayerEventFirstWeekRewardClaimed(
                    correlation, FirstWeekScheduleId.FromString("firstweek.v1"), FirstWeekDayId.FromString("firstweek.v1.day7"), day: 7,
                    claimOrdinal: 5, claimedDays: 5, missedDays: 2, isFinalDay: true),
                new PlayerEventWheelSpinResolved(
                    WheelTableId.FromString("wheel.v1"), WheelSectorId.FromString("coins.1000"), sector: 6, WheelPrizeTier.Rare,
                    CurrencyType.Coins, rewardAmount: 1000, spinOrdinal: 3, spinTokensAfter: 2, correlation),
                new PlayerEventWeeklyEventTargetReached(
                    MetaGuid.FromTimeAndValue(new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc), 42),
                    targetPoints: 100, pointsBefore: 96, pointsAfter: 104, scoringMatches: 38),
                new PlayerEventWeeklyEventRewardClaimed(
                    correlation, MetaGuid.FromTimeAndValue(new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc), 42),
                    targetPoints: 100, pointsScored: 104, claimOrdinal: 2),
                new PlayerEventPersonalizedOffersPreferenceChanged(enabled: false),
            };
        }

        #region The registry's own shape

        /// <summary>
        /// Checks that the ranges cover the game's code band with no gap and no overlap, so every code has
        /// exactly one owner.
        /// </summary>
        [Test]
        public void TheRangesTileTheWholeBand()
        {
            IReadOnlyList<AnalyticsEventCodes.Range> ranges = AnalyticsEventCodes.Ranges;

            Assert.That(ranges[0].First, Is.EqualTo(AnalyticsEventCodes.FirstGameCode));
            Assert.That(ranges[ranges.Count - 1].Last, Is.EqualTo(AnalyticsEventCodes.LastGameCode));

            for (int index = 0; index < ranges.Count; index++)
            {
                Assert.That(ranges[index].First, Is.LessThanOrEqualTo(ranges[index].Last), $"range {index} runs backwards");

                if (index > 0)
                    Assert.That(ranges[index].First, Is.EqualTo(ranges[index - 1].Last + 1),
                        $"range {index} ({ranges[index].Owner}) does not start where {ranges[index - 1].Owner} ends");
            }
        }

        [Test]
        public void NoOwnerHoldsTwoRanges()
        {
            Assert.That(Duplicates(AnalyticsEventCodes.Ranges, range => range.Owner), Is.Empty, "a range is allocated to an owner once and never split");
        }

        [Test]
        public void NoCodeIsAllocatedTwice()
        {
            Assert.That(Duplicates(AnalyticsEventCodes.Registry, entry => entry.Code), Is.Empty, "a code is retired, never recycled");
        }

        /// <summary>
        /// Compares aliases case-insensitively, because two aliases that differ only in case look like one metric
        /// to a reader but are two to a query.
        /// </summary>
        [Test]
        public void NoAliasIsUsedTwice()
        {
            Assert.That(Duplicates(AnalyticsEventCodes.Registry, entry => entry.Alias, StringComparer.OrdinalIgnoreCase), Is.Empty,
                "an alias is retired, never recycled — including after the event is gone");
        }

        [Test]
        public void NoTypeNameIsUsedTwice()
        {
            Assert.That(Duplicates(AnalyticsEventCodes.Registry, entry => entry.TypeName, StringComparer.Ordinal), Is.Empty);
        }

        [Test]
        public void EveryCodeIsInsideTheBandTheSdkLeavesToTheGame()
        {
            foreach (AnalyticsEventCodes.Entry entry in AnalyticsEventCodes.Registry)
                Assert.That(entry.Code, Is.InRange(AnalyticsEventCodes.FirstGameCode, AnalyticsEventCodes.LastGameCode),
                    $"{entry.Alias} is outside 1..999, which the SDK owns");
        }

        /// <summary>
        /// Checks that each code is in its owner's range. This catches a feature that took the next free code in
        /// another owner's range.
        /// </summary>
        [Test]
        public void EveryCodeSitsInItsOwnersRange()
        {
            foreach (AnalyticsEventCodes.Entry entry in AnalyticsEventCodes.Registry)
            {
                AnalyticsEventCodes.Range range = AnalyticsEventCodes.RangeOf(entry.Code);

                Assert.That(range, Is.Not.Null, $"{entry.Alias} ({entry.Code}) falls in no range");
                Assert.That(range.Owner, Is.EqualTo(entry.Owner),
                    $"{entry.Alias} is owned by {entry.Owner} but {entry.Code} is in the {range.Owner} range {range.First}..{range.Last}");
            }
        }

        [Test]
        public void NothingIsAllocatedInAnUnallocatedOrLegacyRange()
        {
            foreach (AnalyticsEventCodes.Entry entry in AnalyticsEventCodes.Registry)
            {
                Assert.That(entry.Owner, Is.Not.EqualTo(AnalyticsOwner.Unallocated),
                    $"{entry.Alias} sits in the unallocated band; reserve a range for it in AnalyticsEventCodes first");
                Assert.That(entry.Owner, Is.Not.EqualTo(AnalyticsOwner.Legacy),
                    $"{entry.Alias} sits in the legacy band, which takes no new allocations");
            }
        }

        #endregion

        #region The four names

        static readonly Regex SnakeCase = new Regex("^[a-z]+(_[a-z]+)*$", RegexOptions.CultureInvariant);

        [Test]
        public void EveryAliasIsLowerSnakeCase()
        {
            foreach (AnalyticsEventCodes.Entry entry in AnalyticsEventCodes.Registry)
                Assert.That(SnakeCase.IsMatch(entry.Alias), Is.True,
                    $"'{entry.Alias}' is not lower snake_case; one camelCase outlier survives review and fails a Dashboard query nobody thinks to check");
        }

        /// <summary>
        /// Checks that no alias contains a version, a config id or a currency name. Such an alias would change
        /// when the content changes, which splits the metric's history.
        /// </summary>
        [Test]
        public void NoAliasCarriesAnythingDynamic()
        {
            string[] forbidden = { "v1", "v2", "config", "variant", "experiment", "coin", "gem", "token", "usd", "eur" };

            foreach (AnalyticsEventCodes.Entry entry in AnalyticsEventCodes.Registry)
            {
                Assert.That(entry.Alias.Any(char.IsDigit), Is.False,
                    $"'{entry.Alias}' carries a digit; a version or an ID belongs in the payload, not in the name");

                foreach (string word in forbidden)
                    Assert.That(entry.Alias.Split('_'), Has.None.EqualTo(word),
                        $"'{entry.Alias}' names something that can change without the event changing");
            }
        }

        /// <summary>
        /// Checks that an alias equals its type name without the <c>PlayerEvent</c> prefix, in snake case, unless
        /// the row sets <see cref="AnalyticsEventCodes.Entry.AliasFollowsTypeName"/> to false.
        /// <para>
        /// The alias never changes, but the C# type may be renamed. A row whose type was renamed after its event
        /// shipped keeps its alias and opts out of this check.
        /// </para>
        /// </summary>
        [Test]
        public void ANewAliasFollowsItsTypeNameUnlessItSaysOtherwise()
        {
            foreach (AnalyticsEventCodes.Entry entry in AnalyticsEventCodes.Registry.Where(e => e.AliasFollowsTypeName))
                Assert.That(AnalyticsEventCodes.AliasFor(entry.TypeName), Is.EqualTo(entry.Alias),
                    $"{entry.TypeName} and '{entry.Alias}' name two different things. If the alias is frozen against a renamed type, say so with aliasFollowsTypeName: false.");
        }

        [Test]
        public void EveryTypeNameIsAPlayerEventName()
        {
            foreach (AnalyticsEventCodes.Entry entry in AnalyticsEventCodes.Registry)
            {
                Assert.That(entry.TypeName, Does.StartWith("PlayerEvent"), $"{entry.TypeName} is not named as a player event");
                Assert.That(entry.TypeName.Length, Is.GreaterThan("PlayerEvent".Length));
            }
        }

        [Test]
        public void EveryEventDeclaresTheAliasTheRegistryGaveIt()
        {
            foreach (Type type in DeclaredEvents)
            {
                AnalyticsAliasAttribute alias = type.GetCustomAttribute<AnalyticsAliasAttribute>(inherit: false);
                Assert.That(alias, Is.Not.Null, $"{type.Name} declares no [AnalyticsAlias]");

                AnalyticsEventCodes.Entry entry = AnalyticsEventCodes.EntryOf(AttributeOf(type).TypeCode);
                Assert.That(alias.Alias, Is.EqualTo(entry.Alias), $"{type.Name} declares an alias the registry does not know");
            }
        }

        /// <summary>Checks that every event has a sentence-case Dashboard display name and a non-trivial doc string.</summary>
        [Test]
        public void EveryEventHasADisplayNameAndADocString()
        {
            foreach (Type type in DeclaredEvents)
            {
                AnalyticsEventAttribute attribute = AttributeOf(type);

                Assert.That(attribute.DisplayName, Is.Not.Null.And.Not.Empty, $"{type.Name} has no display name");
                Assert.That(attribute.DisplayName, Does.Not.Contain("_"), $"{type.Name}'s display name is an alias, not a sentence");
                Assert.That(attribute.DisplayName, Is.Not.EqualTo(type.Name), $"{type.Name}'s display name is just the type name");
                Assert.That(char.IsUpper(attribute.DisplayName[0]), Is.True, $"{type.Name}'s display name is not sentence case");

                Assert.That(attribute.DocString, Is.Not.Null.And.Not.Empty, $"{type.Name} has no doc string");
                Assert.That(attribute.DocString.Length, Is.GreaterThan(20), $"{type.Name}'s doc string says nothing useful");
            }
        }

        #endregion

        #region Declared events against the registry

        /// <summary>
        /// Checks that every declared event has a live registry row at its code, with its type name.
        /// </summary>
        [Test]
        public void EveryDeclaredEventIsRegistered()
        {
            foreach (Type type in DeclaredEvents)
            {
                int                       code  = AttributeOf(type).TypeCode;
                AnalyticsEventCodes.Entry entry = AnalyticsEventCodes.EntryOf(code);

                Assert.That(entry, Is.Not.Null,
                    $"{type.Name} uses code {code}, which is in no registry row. Allocate it in AnalyticsEventCodes first.");
                Assert.That(entry.TypeName, Is.EqualTo(type.Name),
                    $"code {code} is registered to {entry.TypeName} but is declared by {type.Name}");
                Assert.That(entry.Status, Is.EqualTo(AnalyticsEventStatus.Live),
                    $"{type.Name} exists but its registry row still says {entry.Status}");
            }
        }

        [Test]
        public void EveryLiveRegistryRowHasATypeBehindIt()
        {
            IEnumerable<string> declared = DeclaredEvents.Select(type => type.Name);

            foreach (AnalyticsEventCodes.Entry entry in AnalyticsEventCodes.Registry.Where(e => e.Status == AnalyticsEventStatus.Live))
                Assert.That(declared, Contains.Item(entry.TypeName),
                    $"{entry.TypeName} is registered as live but no such event type exists");
        }

        /// <summary>A reserved row holds a code for an unwritten event type. Fails when the type exists but the row is still reserved.</summary>
        [Test]
        public void EveryReservedRowIsStillWaitingForItsFeature()
        {
            IEnumerable<string> declared = DeclaredEvents.Select(type => type.Name);

            foreach (AnalyticsEventCodes.Entry entry in AnalyticsEventCodes.Registry.Where(e => e.Status == AnalyticsEventStatus.Reserved))
                Assert.That(declared, Does.Not.Contain(entry.TypeName),
                    $"{entry.TypeName} exists now; flip its registry row to Live in the same change");
        }

        [Test]
        public void EveryDeclaredEventIsAPlayerEvent()
        {
            foreach (Type type in DeclaredEvents)
                Assert.That(typeof(PlayerEventBase).IsAssignableFrom(type), Is.True,
                    $"{type.Name} is an analytics event but not a player event; the contract covers the player log");
        }

        /// <summary>
        /// Checks that no registry code is used by an SDK event in the SDK version this game builds against.
        /// </summary>
        [Test]
        public void NoGameCodeCollidesWithAnSdkEvent()
        {
            Dictionary<int, string> sdkCodes = new Dictionary<int, string>();
            foreach (Type type in SdkEvents)
                sdkCodes[AttributeOf(type).TypeCode] = type.Name;

            Assert.That(sdkCodes, Is.Not.Empty, "no SDK analytics events were found, so this test proves nothing");

            foreach (AnalyticsEventCodes.Entry entry in AnalyticsEventCodes.Registry)
                Assert.That(sdkCodes.ContainsKey(entry.Code), Is.False,
                    $"{entry.Alias} claims {entry.Code}, which the SDK's {(sdkCodes.TryGetValue(entry.Code, out string sdk) ? sdk : "?")} already uses");
        }

        #endregion

        #region Keywords

        /// <summary>
        /// Checks the keywords a type's <c>[AnalyticsEventKeywords]</c> attributes declare: each is in the
        /// vocabulary, none repeats, at most one is a domain, and the count is within the budget.
        /// <para>
        /// This does not check that a domain keyword is present. An event may add keywords, including its domain,
        /// per instance (<c>docs/analytics.md</c>, "Dashboard keywords"), which a type-level check cannot see.
        /// <see cref="EveryRowReachesTheDashboardInsideTheKeywordBudget"/> checks the final keywords on instances.
        /// </para>
        /// </summary>
        [Test]
        public void EveryKeywordATypeDeclaresIsVettedAndInsideTheBudget()
        {
            foreach (Type type in DeclaredEvents)
            {
                IReadOnlyList<string> keywords = KeywordsOf(type);

                Assert.That(keywords.Count, Is.LessThanOrEqualTo(AnalyticsKeywords.MaxPerEvent), $"{type.Name} declares {keywords.Count} keywords");
                Assert.That(keywords, Is.Unique, $"{type.Name} repeats a keyword");

                int domains = keywords.Count(keyword => AnalyticsKeywords.Domains.Contains(keyword));
                Assert.That(domains, Is.LessThanOrEqualTo(1), $"{type.Name} declares {domains} domain keywords, and an event belongs to one domain");

                foreach (string keyword in keywords)
                    Assert.That(Vocabulary, Contains.Item(keyword), $"{type.Name} uses '{keyword}', which is not in the vocabulary");
            }
        }

        /// <summary>
        /// Checks the keywords each representative event reaches the Dashboard with: exactly one domain, all in
        /// the vocabulary, no repeats, and within the budget.
        /// <para>
        /// The test reads <see cref="AnalyticsEventBase.Keywords"/>, the SDK's merge of the attributes on the
        /// whole base chain with the keywords the instance adds. Recomputing the merge here could miss a keyword
        /// on a base class or one added per instance, such as the <c>Source</c> or <c>Sink</c> direction on a
        /// currency row.
        /// </para>
        /// </summary>
        [Test]
        public void EveryRowReachesTheDashboardInsideTheKeywordBudget()
        {
            foreach (PlayerEventBase analyticsEvent in RepresentativeEvents())
            {
                string   name     = analyticsEvent.GetType().Name;
                string[] keywords = analyticsEvent.Keywords.ToArray();

                Assert.That(keywords, Is.Not.Empty, $"{name} reaches the Dashboard with no keyword, so no filter finds it");
                Assert.That(keywords, Is.Unique, $"{name} reaches the Dashboard carrying a keyword twice");
                Assert.That(keywords.Length, Is.LessThanOrEqualTo(AnalyticsKeywords.MaxPerEvent),
                    $"{name} reaches the Dashboard with {keywords.Length} keywords");

                int domains = keywords.Count(keyword => AnalyticsKeywords.Domains.Contains(keyword));
                Assert.That(domains, Is.EqualTo(1), $"{name} reaches the Dashboard with {domains} domain keywords");

                foreach (string keyword in keywords)
                    Assert.That(Vocabulary, Contains.Item(keyword), $"{name} reaches the Dashboard under '{keyword}', which is not in the vocabulary");
            }
        }

        /// <summary>
        /// Checks that a currency row carries only the keyword of the direction it moved. If every row carried
        /// both, the Dashboard's <c>Sink</c> filter would also show every grant (<c>docs/analytics.md</c>,
        /// "Dashboard keywords").
        /// <para>
        /// The assertion reads <see cref="AnalyticsEventBase.Keywords"/>, so it also fails if the SDK stops
        /// merging the per-instance keyword into the type's keywords.
        /// </para>
        /// </summary>
        [Test]
        public void ACurrencyRowIsFiledUnderTheDirectionItMoved()
        {
            AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(
                EntityId.Create(EntityKindCore.Player, 7), MetaTime.FromMillisecondsSinceEpoch(1_700_000_000_000), "spin");

            PlayerEventEconomyTransaction spend = new PlayerEventEconomyTransaction(
                CurrencyType.SpinTokens, CurrencyFlow.Sink, amount: 1, balanceBefore: 1, balanceAfter: 0,
                EconomyReason.WheelSpinCost, EconomyFeature.SpinWheel, contentId: null, correlation);

            PlayerEventEconomyTransaction grant = new PlayerEventEconomyTransaction(
                CurrencyType.Coins, CurrencyFlow.Source, amount: 500, balanceBefore: 3000, balanceAfter: 3500,
                EconomyReason.WheelPrize, EconomyFeature.SpinWheel, contentId: null, correlation);

            Assert.That(spend.Keywords, Is.EquivalentTo(new[] { AnalyticsKeywords.Economy, AnalyticsKeywords.Sink }));
            Assert.That(grant.Keywords, Is.EquivalentTo(new[] { AnalyticsKeywords.Economy, AnalyticsKeywords.Source }));

            Assert.That(KeywordsOf(typeof(PlayerEventEconomyTransaction)),
                Is.EqualTo(new[] { AnalyticsKeywords.Economy }),
                "the direction belongs on the instance; an attribute would put both directions on every row");
        }

        [Test]
        public void TheVocabularyIsAClosedSetOfPascalCaseTokens()
        {
            Assert.That(Vocabulary, Is.Unique, "a keyword is either a domain or a qualifier, never both");

            foreach (string keyword in Vocabulary)
                Assert.That(Regex.IsMatch(keyword, "^[A-Z][A-Za-z]*$"), Is.True, $"'{keyword}' is not an exact PascalCase token");
        }

        /// <summary>
        /// Checks that the keywords this game adds to SDK events come from the same vocabulary as its own events.
        /// </summary>
        [Test]
        public void TheSdkEventCustomizationsUseTheSameVocabulary()
        {
            foreach ((Type type, string[] keywords) in GameAnalyticsEventCustomizations.KeywordsBySdkEvent)
            {
                Assert.That(typeof(AnalyticsEventBase).IsAssignableFrom(type), Is.True, $"{type.Name} is not an analytics event");
                Assert.That(IsGameType(type), Is.False, $"{type.Name} is the game's own event; keyword it with an attribute instead");
                Assert.That(keywords, Is.Not.Empty);
                Assert.That(keywords.Length, Is.LessThanOrEqualTo(AnalyticsKeywords.MaxPerEvent));

                foreach (string keyword in keywords)
                    Assert.That(Vocabulary, Contains.Item(keyword), $"{type.Name} is filed under '{keyword}', which is not in the vocabulary");
            }
        }

        /// <summary>
        /// Checks that no game event has the alias of an SDK event. Two events for one fact disagree when the fact
        /// happens on a path the game does not handle (<c>docs/analytics.md</c>, "SDK event customizations").
        /// </summary>
        [Test]
        public void NoCustomEventDuplicatesAnSdkFact()
        {
            // Derive the SDK's aliases from its event types, so the list follows SDK upgrades.
            Dictionary<string, string> sdkOwned = new Dictionary<string, string>();
            foreach (Type type in SdkEvents)
                sdkOwned[AnalyticsEventCodes.AliasFor(type.Name)] = type.Name;

            Assert.That(sdkOwned, Is.Not.Empty, "no SDK analytics events were found, so this test proves nothing");

            foreach (AnalyticsEventCodes.Entry entry in AnalyticsEventCodes.Registry)
                Assert.That(sdkOwned.ContainsKey(entry.Alias), Is.False,
                    $"'{entry.Alias}' is the fact the SDK's {(sdkOwned.TryGetValue(entry.Alias, out string sdk) ? sdk : "?")} already emits; add a keyword to the SDK event instead");
        }

        #endregion

        #region Payloads and privacy

        /// <summary>
        /// Checks that no declared event's payload can reach a <see cref="string"/>. A payload with no string
        /// cannot carry a player's name, a chat line, a URL or an exception message.
        /// </summary>
        [Test]
        public void NoEventPayloadCanReachFreeText()
        {
            foreach (Type type in DeclaredEvents)
            foreach (MemberInfo member in PayloadOf(type))
            {
                List<Type> reachableTypes = ReachableFrom(MemberType(member), new HashSet<Type>()).ToList();

                Assert.That(reachableTypes, Has.None.EqualTo(typeof(string)),
                    $"{type.Name}.{member.Name} can reach a string. Use a closed enum or a typed ID; the envelope already says who this was. "
                    + "A StringId counts: it is exported as a string. If it genuinely carries a config id, vet it and add it to VettedConfigIdTypes.");
            }
        }

        /// <summary>
        /// Applies the free-text rule of <see cref="NoEventPayloadCanReachFreeText"/> to every event type this
        /// game constructs, including SDK event types.
        /// <para>
        /// An SDK event that game code constructs and emits is written on the game's path. For example, the SDK's
        /// name-change event carries the old and new names as strings. The declared-event check does not see SDK
        /// types, so this test finds constructed types by scanning the IL of every game assembly. SDK events that
        /// only the SDK constructs are not covered.
        /// </para>
        /// </summary>
        [Test]
        public void NoEventThisGameConstructsCanReachFreeText()
        {
            IReadOnlyList<Type> constructed = GameAssemblies.SelectMany(AnalyticsEventsConstructedBy).Distinct().ToList();

            Assert.That(constructed, Is.Not.Empty, "the IL scan found no analytics event construction at all, so this test proves nothing");

            foreach (Type type in constructed)
            foreach (MemberInfo member in PayloadOf(type))
            {
                List<Type> reachableTypes = ReachableFrom(MemberType(member), new HashSet<Type>()).ToList();

                Assert.That(reachableTypes, Has.None.EqualTo(typeof(string)),
                    $"{type.Name}.{member.Name} can reach a string, and this game constructs {type.Name} itself. "
                    + (DeclaredEvents.Contains(type)
                        ? "Use a closed enum or a typed ID."
                        : "It is an SDK event, so its payload cannot be changed: emit one of this game's own instead, or stop emitting it here."));
            }
        }

        /// <summary>
        /// Checks that the IL scan finds a known construction site in game code, so that a scan that finds
        /// nothing cannot pass <see cref="NoEventThisGameConstructsCanReachFreeText"/>.
        /// </summary>
        [Test]
        public void TheConstructionScanReachesRealGameCode()
        {
            // PlayerModel.EmitWalletRows constructs this event, and shared code is loaded in every run of this fixture.
            Assert.That(AnalyticsEventsConstructedBy(typeof(PlayerModel).Assembly),
                Contains.Item(typeof(PlayerEventEconomyTransaction)));
        }

        /// <summary>
        /// Returns every analytics event type constructed in <paramref name="assembly"/>, found by scanning each
        /// method's IL for a <c>newobj</c> opcode whose constructor belongs to an analytics event.
        /// <para>
        /// The scan checks every byte offset, not only instruction boundaries, so it cannot miss a real
        /// <c>newobj</c>. A false match either fails to resolve and is dropped, or adds a real event type, which
        /// only makes the check stricter.
        /// </para>
        /// </summary>
        static IReadOnlyList<Type> AnalyticsEventsConstructedBy(Assembly assembly)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            const byte         NewObj = 0x73;

            HashSet<Type> constructed = new HashSet<Type>();

            foreach (Type type in TypesOf(assembly))
            foreach (MethodBase method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                byte[] il = BodyOf(method);
                if (il == null)
                    continue;

                for (int ilOffset = 0; ilOffset + 5 <= il.Length; ilOffset++)
                {
                    if (il[ilOffset] != NewObj)
                        continue;

                    Type constructedType = ConstructedTypeAt(method, BitConverter.ToInt32(il, ilOffset + 1));
                    if (constructedType != null && typeof(AnalyticsEventBase).IsAssignableFrom(constructedType))
                        constructed.Add(constructedType);
                }
            }

            return constructed.OrderBy(type => type.Name, StringComparer.Ordinal).ToList();
        }

        static byte[] BodyOf(MethodBase method)
        {
            try
            {
                return method.GetMethodBody()?.GetILAsByteArray();
            }
            catch
            {
                // Some methods, such as abstract and extern ones, have no readable body and construct nothing.
                return null;
            }
        }

        static Type ConstructedTypeAt(MethodBase method, int methodToken)
        {
            try
            {
                Type[] typeArguments   = method.DeclaringType != null && method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
                Type[] methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

                return method.Module.ResolveMethod(methodToken, typeArguments, methodArguments) is ConstructorInfo constructor
                    ? constructor.DeclaringType
                    : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Checks that every entry in <see cref="VettedConfigIdTypes"/> is a <see cref="IStringId"/> declared by
        /// this game, so the list cannot exempt an SDK id type.
        /// </summary>
        [Test]
        public void TheVettedIdListHoldsOnlyThisGamesConfigIdTypes()
        {
            Assert.That(VettedConfigIdTypes, Is.Unique);

            foreach (Type vetted in VettedConfigIdTypes)
            {
                Assert.That(typeof(IStringId).IsAssignableFrom(vetted), Is.True, $"{vetted.Name} is not a StringId, so it has no business on this list");
                Assert.That(IsGameType(vetted), Is.True, $"{vetted.Name} is not one of this game's own types");
            }
        }

        /// <summary>
        /// Every concrete, closed <see cref="IStringId"/> type this game declares, vetted or not.
        /// </summary>
        static IReadOnlyList<Type> GameStringIdTypes =>
            GameAssemblies
                .SelectMany(TypesOf)
                .Where(type => typeof(IStringId).IsAssignableFrom(type))
                .Where(type => !type.IsAbstract && !type.IsInterface && !type.ContainsGenericParameters)
                .Distinct()
                .ToList();

        /// <summary>
        /// Checks that <see cref="ReachableFrom"/> reports a string for every unvetted <see cref="IStringId"/>
        /// this game declares. The test covers the whole unvetted set rather than one named id, because a named id
        /// may later be vetted.
        /// </summary>
        [Test]
        public void AnUnvettedStringIdCountsAsFreeText()
        {
            IReadOnlyList<Type> unvetted = GameStringIdTypes.Where(type => !VettedConfigIdTypes.Contains(type)).ToList();

            Assert.That(unvetted, Is.Not.Empty,
                "every id this game declares is now vetted, so this test proves nothing; state the rule against a type declared here instead");

            foreach (Type type in unvetted)
            {
                Assert.That(ReachableFrom(type, new HashSet<Type>()), Contains.Item(typeof(string)),
                    $"{type.Name} has not been vetted, so the walk has to report the string it wraps rather than assume it is a config id");
            }

            Assert.That(ReachableFrom(typeof(EconomyContentId), new HashSet<Type>()), Has.None.EqualTo(typeof(string)));
        }

        [Test]
        public void NoEventPayloadNamesSomethingPrivate()
        {
            string[] exact    = { "player", "playerid", "name", "text", "message", "url", "email", "ip", "address" };
            string[] anywhere = { "email", "stacktrace", "playername", "displayname", "username", "advertising", "deviceid", "ipaddress", "password", "freetext" };

            foreach (Type type in DeclaredEvents)
            foreach (MemberInfo member in PayloadOf(type))
            {
                string name = member.Name.ToLowerInvariant();

                foreach (string word in exact)
                    Assert.That(name, Is.Not.EqualTo(word), $"{type.Name}.{member.Name} looks like something the payload must not carry");

                foreach (string word in anywhere)
                    Assert.That(name, Does.Not.Contain(word), $"{type.Name}.{member.Name} looks like something the payload must not carry");
            }
        }

        /// <summary>
        /// Checks that every event goes to both the player's event log and the analytics sink
        /// (<c>docs/analytics.md</c>).
        /// </summary>
        [Test]
        public void EveryEventGoesToThePlayerLogAndToAnalytics()
        {
            foreach (Type type in DeclaredEvents)
            {
                AnalyticsEventAttribute attribute = AttributeOf(type);

                Assert.That(attribute.IncludeInEventLog, Is.True, $"{type.Name} is excluded from the player log, and nothing else reads it yet");
                Assert.That(attribute.SendToAnalytics, Is.True, $"{type.Name} is not sent to analytics");
            }
        }

        [Test]
        public void EveryPayloadFieldIdIsUniqueWithinItsEvent()
        {
            foreach (Type type in DeclaredEvents)
            {
                IEnumerable<int> ids = PayloadOf(type).Select(member => member.GetCustomAttribute<MetaMemberAttribute>(inherit: false).TagId);
                Assert.That(ids, Is.Unique, $"{type.Name} reuses a [MetaMember] id; ids are append-only and never recycled");
            }
        }

        #endregion

        #region Descriptions

        /// <summary>
        /// Checks that every declared event has an instance in <see cref="RepresentativeEvents"/>, because the
        /// keyword, description, culture and round-trip tests only cover events listed there.
        /// </summary>
        [Test]
        public void EveryDeclaredEventHasARepresentativeInstance()
        {
            IEnumerable<Type> represented = RepresentativeEvents().Select(e => e.GetType()).Distinct();

            foreach (Type type in DeclaredEvents)
                Assert.That(represented, Contains.Item(type),
                    $"{type.Name} has no representative instance, so nothing checks its description or its round trip");
        }

        [Test]
        public void EveryEventDescribesItselfInWords()
        {
            foreach (PlayerEventBase analyticsEvent in RepresentativeEvents())
            {
                string description = analyticsEvent.EventDescription;

                Assert.That(description, Is.Not.Null.And.Not.Empty, $"{analyticsEvent.GetType().Name} describes itself as nothing");
                Assert.That(description.Length, Is.GreaterThan(10));
                Assert.That(description, Does.EndWith("."), $"{analyticsEvent.GetType().Name}'s description is not a sentence");
            }
        }

        /// <summary>
        /// Checks that every description is the same under the invariant and German cultures, because the server's
        /// culture is not set by the game.
        /// </summary>
        [Test]
        public void EveryDescriptionIsCultureInvariant()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;

            try
            {
                IReadOnlyList<PlayerEventBase> events = RepresentativeEvents();

                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                List<string> invariant = events.Select(e => e.EventDescription).ToList();

                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                List<string> german = events.Select(e => e.EventDescription).ToList();

                Assert.That(german, Is.EqualTo(invariant), "a description changed with the culture; format it invariantly");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        #endregion

        #region Serialization

        [Test]
        public void EveryEventSurvivesARoundTrip()
        {
            foreach (PlayerEventBase analyticsEvent in RepresentativeEvents())
            {
                byte[] serialized = MetaSerialization.SerializeTagged<PlayerEventBase>(analyticsEvent, MetaSerializationFlags.IncludeAll, logicVersion: null);
                PlayerEventBase restored = MetaSerialization.DeserializeTagged<PlayerEventBase>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

                Assert.That(restored, Is.TypeOf(analyticsEvent.GetType()));
                Assert.That(restored.EventDescription, Is.EqualTo(analyticsEvent.EventDescription));
            }
        }

        #endregion
    }
}
