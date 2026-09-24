using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// A new player was given a generated name and a starting avatar. Records the vocabulary version and whether
    /// the fallback name was used, which cannot be derived from the name. The event carries the name's length,
    /// not the name (<c>docs/analytics.md</c>, "Payload rules").
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.IdentityInitialized, displayName: "Identity initialized", docString: "A new player was given a generated display name, and which vocabulary produced it.")]
    [AnalyticsAlias("player_identity_initialized")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Identity)]
    public class PlayerEventIdentityInitialized : PlayerEventBase
    {
        /// <summary>The vocabulary version the name came from. Zero when no vocabulary was usable.</summary>
        [MetaMember(1)] public int GeneratorVersion { get; private set; }

        /// <summary>The name's length, as counted by <see cref="DisplayNamePolicy.CountCharacters"/>.</summary>
        [MetaMember(2)] public int NameLength { get; private set; }

        /// <summary>Whether the fallback name was used because the vocabulary could not produce a valid name.</summary>
        [MetaMember(3)] public bool UsedFallback { get; private set; }

        /// <summary>The avatar the player starts with, or null if none is equipped.</summary>
        [MetaMember(4)] public CosmeticId AvatarId { get; private set; }

        public override string EventDescription =>
            UsedFallback
                ? $"Named by the fallback, {NameLength} characters."
                : $"Named from vocabulary v{GeneratorVersion}, {NameLength} characters.";

        public PlayerEventIdentityInitialized() { }

        public PlayerEventIdentityInitialized(int generatorVersion, int nameLength, bool usedFallback, CosmeticId avatarId)
        {
            GeneratorVersion = generatorVersion;
            NameLength       = nameLength;
            UsedFallback     = usedFallback;
            AvatarId         = avatarId;
        }
    }

    /// <summary>
    /// The server refused a rename, and the rule that refused it. Only server refusals are recorded, not the
    /// client's validation while the player types. The event carries no name text. It and
    /// <see cref="PlayerEventNameChangeAccepted"/> both carry <c>NameChangeCount</c>, so a rejection rate needs no join.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.NameChangeRejected, displayName: "Name change rejected", docString: "The server refused a rename, and which rule refused it. Carries no name text.")]
    [AnalyticsAlias("player_name_change_rejected")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Identity, AnalyticsKeywords.Rejected)]
    public class PlayerEventNameChangeRejected : PlayerEventBase
    {
        /// <summary>The rule that refused the rename. The player is shown the same reason.</summary>
        [MetaMember(1)] public DisplayNameRefusal Reason { get; private set; }

        /// <summary>The submitted name's length, as counted by <see cref="DisplayNamePolicy.CountCharacters"/>.</summary>
        [MetaMember(2)] public int SubmittedLength { get; private set; }

        /// <summary>The number of this player's accepted renames. This refusal does not change it.</summary>
        [MetaMember(3)] public int NameChangeCount { get; private set; }

        public override string EventDescription => $"Rename refused: {Reason}, on {SubmittedLength} characters.";

        public PlayerEventNameChangeRejected() { }

        public PlayerEventNameChangeRejected(DisplayNameRefusal reason, int submittedLength, int nameChangeCount)
        {
            Reason          = reason;
            SubmittedLength = submittedLength;
            NameChangeCount = nameChangeCount;
        }
    }

    /// <summary>Where a player's name came from.</summary>
    [MetaSerializable]
    public enum DisplayNameOrigin
    {
        /// <summary>The generated name. The player has never renamed.</summary>
        Generated = 0,

        /// <summary>A name the player chose.</summary>
        Custom = 1,
    }

    /// <summary>
    /// The server accepted a player's rename. The event carries lengths and the previous name's origin, but no
    /// name text. The SDK's <c>PlayerEventNameChanged</c>, which carries both names as text, is emitted only by
    /// the LiveOps Dashboard rename. A player rename goes through the game's own handler and emits only this
    /// event (<c>docs/analytics.md</c>, "SDK event customizations").
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.NameChanged, displayName: "Name changed", docString: "The server accepted a rename. Carries lengths and an origin, never name text.")]
    [AnalyticsAlias("player_name_changed")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Identity)]
    public class PlayerEventNameChangeAccepted : PlayerEventBase
    {
        /// <summary>Whether the replaced name was generated or chosen by the player.</summary>
        [MetaMember(1)] public DisplayNameOrigin PreviousOrigin { get; private set; }

        /// <summary>The replaced name's length, as counted by <see cref="DisplayNamePolicy.CountCharacters"/>.</summary>
        [MetaMember(2)] public int PreviousLength { get; private set; }

        /// <summary>The new name's length, as counted by <see cref="DisplayNamePolicy.CountCharacters"/>.</summary>
        [MetaMember(3)] public int NewLength { get; private set; }

        /// <summary>The number of this player's accepted renames, including this one.</summary>
        [MetaMember(4)] public int NameChangeCount { get; private set; }

        public override string EventDescription =>
            $"Renamed from a {PreviousOrigin} name of {PreviousLength} characters to one of {NewLength}; change {NameChangeCount}.";

        public PlayerEventNameChangeAccepted() { }

        public PlayerEventNameChangeAccepted(DisplayNameOrigin previousOrigin, int previousLength, int newLength, int nameChangeCount)
        {
            PreviousOrigin  = previousOrigin;
            PreviousLength  = previousLength;
            NewLength       = newLength;
            NameChangeCount = nameChangeCount;
        }
    }
}
