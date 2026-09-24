using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Game.Logic
{
    /// <summary>
    /// Identifies one published bot strength. A table records this id for each bot seat, so a finished game can
    /// report which profile played each seat.
    /// </summary>
    [MetaSerializable]
    public class BotProfileId : StringId<BotProfileId> { }

    /// <summary>
    /// One published bot strength: how often the bot makes a mistake and how often it takes the long think delay
    /// (<c>docs/bots.md</c>, "Profiles").
    /// <para>
    /// A bot seat filled at table formation gets a profile drawn uniformly from this library. The library has no
    /// <see cref="BotDecisionMode"/> column: a published profile always uses <see cref="BotDecisionMode.Heuristic"/>.
    /// The other modes exist for reproducible tests, and a bot that plays random legal cards looks like a broken
    /// game to a player, so a config publish cannot select them.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class BotProfileInfo : IGameConfigData<BotProfileId>, IValidatedConfigItem
    {
        /// <summary>
        /// The highest allowed <see cref="MistakeChancePercent"/>. A mistake should look like an occasional lapse.
        /// A bot that plays its second-ranked card most of the time no longer plays the heuristic.
        /// </summary>
        public const int MaxMistakeChancePercent = 40;

        [MetaMember(1)] public BotProfileId Id { get; private set; }

        /// <summary>The name operators see for this profile. Players never see it.</summary>
        [MetaMember(2)] public string DisplayName { get; private set; }

        /// <summary>How often, in percent, this opponent takes its second-ranked card instead of its first.</summary>
        [MetaMember(3)] public int MistakeChancePercent { get; private set; }

        /// <summary>
        /// How often, in percent, a move uses the longer of the host's two think-delay bands. The host supplies the
        /// band durations, so a test host with zero timings plays instantly regardless of this value.
        /// </summary>
        [MetaMember(4)] public int LongThinkChancePercent { get; private set; }

        public BotProfileId ConfigKey => Id;

        public BotProfileInfo() { }

        public BotProfileInfo(BotProfileId id, string displayName, int mistakeChancePercent, int longThinkChancePercent)
        {
            Id                     = id;
            DisplayName            = displayName;
            MistakeChancePercent   = mistakeChancePercent;
            LongThinkChancePercent = longThinkChancePercent;
        }

        /// <summary>
        /// The profile a table copies onto a bot seat as it forms, always with <see cref="BotDecisionMode.Heuristic"/>.
        /// </summary>
        public BotProfile ToProfile() =>
            new BotProfile(Id, BotDecisionMode.Heuristic, MistakeChancePercent, LongThinkChancePercent);

        public void Validate(ConfigItemValidation validation)
        {
            validation.Require(!string.IsNullOrWhiteSpace(DisplayName), "has no display name for an operator to tell it by", nameof(DisplayName));

            validation.RequireNonNegative(MistakeChancePercent, nameof(MistakeChancePercent));
            validation.RequireAtMost(MistakeChancePercent, MaxMistakeChancePercent, nameof(MistakeChancePercent));

            validation.RequireNonNegative(LongThinkChancePercent, nameof(LongThinkChancePercent));
            validation.RequireAtMost(LongThinkChancePercent, 100, nameof(LongThinkChancePercent));
        }

        public override string ToString() => Id?.Value ?? "(no profile)";
    }

    /// <summary>
    /// Identifies one reserved bot name. The id is separate from <see cref="BotNameInfo.Name"/> so that fixing
    /// a spelling does not change the row's identity.
    /// </summary>
    [MetaSerializable]
    public class BotNameId : StringId<BotNameId> { }

    /// <summary>
    /// One name a bot may be given. The same name is reserved, so no player can take it (<c>docs/bots.md</c>,
    /// "Names").
    /// <para>
    /// Treat the library as append-only. A removed name becomes available to players while existing tables may
    /// still show it on a bot seat. Remove a name only when no table can still be showing it.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class BotNameInfo : IGameConfigData<BotNameId>, IValidatedConfigItem
    {
        [MetaMember(1)] public BotNameId Id { get; private set; }

        /// <summary>The name shown on the bot's seat plaque.</summary>
        [MetaMember(2)] public string Name { get; private set; }

        public BotNameId ConfigKey => Id;

        public BotNameInfo() { }

        public BotNameInfo(BotNameId id, string name)
        {
            Id   = id;
            Name = name;
        }

        /// <summary>
        /// Requires <see cref="Name"/> to pass <see cref="DisplayNamePolicy.Validate"/>, the check a player rename
        /// goes through, with an empty reserved-name roster (every row here would fail against the full roster).
        /// <para>
        /// The seat plaque layout is sized for names the policy accepts, so a longer name breaks the table screen
        /// on a phone. And reserving a name that no player could choose anyway protects nothing.
        /// </para>
        /// </summary>
        public void Validate(ConfigItemValidation validation)
        {
            DisplayNameRefusal refusal = DisplayNamePolicy.Validate(Name, BotNameRoster.Empty);
            validation.Require(
                refusal == DisplayNameRefusal.None,
                $"is named '{Name}', which the server would refuse from a player ({refusal})",
                nameof(Name));
        }

        public override string ToString() => Name ?? Id?.Value ?? "(no name)";
    }

    /// <summary>
    /// Reads the bot profiles and bot names from the published config.
    /// <para>
    /// Both libraries are read whole rather than through an active-row pointer, because a table draws from all
    /// rows (<c>docs/game-config.md</c>).
    /// </para>
    /// </summary>
    public static class BotConfig
    {
        /// <summary>
        /// The profiles a bot seat may be given at table formation, in config order. Empty when the config has
        /// no profile library. The config build refuses an empty library, and
        /// <see cref="BotProfiles.DrawForSeat"/> returns the strongest profile for an empty list.
        /// </summary>
        public static List<BotProfile> DrawableProfiles(SharedGameConfig config)
        {
            List<BotProfile> profiles = new List<BotProfile>();
            if (config?.BotProfiles == null)
                return profiles;

            foreach (BotProfileInfo info in config.BotProfiles.Values)
            {
                if (info != null)
                    profiles.Add(info.ToProfile());
            }
            return profiles;
        }

        /// <summary>
        /// The reserved bot names, used both to name bot seats and to refuse player names. Empty when the config
        /// has no name library, which the config build refuses.
        /// </summary>
        public static BotNameRoster ReservedNames(SharedGameConfig config)
        {
            if (config?.BotNames == null)
                return BotNameRoster.Empty;

            return Rosters.GetValue(config.BotNames, BuildRoster);
        }

        /// <summary>
        /// The roster of each <c>BotNames</c> library, built on first use. A loaded library does not change and a
        /// roster is immutable, so every caller can share one. The key is the library rather than the config, so a
        /// config whose library is replaced gets a new roster.
        /// </summary>
        static readonly ConditionalWeakTable<GameConfigLibrary<BotNameId, BotNameInfo>, BotNameRoster> Rosters =
            new ConditionalWeakTable<GameConfigLibrary<BotNameId, BotNameInfo>, BotNameRoster>();

        static BotNameRoster BuildRoster(GameConfigLibrary<BotNameId, BotNameInfo> library)
        {
            List<string> names = new List<string>(library.Count);
            foreach (BotNameInfo info in library.Values)
            {
                if (info != null)
                    names.Add(info.Name);
            }
            return BotNameRoster.FromNames(names);
        }
    }
}
