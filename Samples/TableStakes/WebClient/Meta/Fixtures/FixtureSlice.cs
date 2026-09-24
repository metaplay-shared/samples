namespace WebClient.Meta.Fixtures;

/// <summary>
/// A part of the meta snapshot that <c>MetaStateService</c> fills from real player state over the fixture. A
/// scenario keeps its fixture data for a slice only by listing that slice in <see cref="ScenarioInfo.Pinned"/>.
/// </summary>
public enum FixtureSlice
{
    /// <summary>
    /// The name, avatar, worn cosmetics and competitive standing. The standing is tournament data, so a pin on
    /// either this slice or <see cref="Tournament"/> keeps it.
    /// </summary>
    Identity,

    /// <summary>The three balances on the HUD.</summary>
    Wallet,

    /// <summary>The Profile's statistics: games played, games won and tricks won.</summary>
    Record,

    /// <summary>The daily login reward: the streak, the cycle and whether today is claimable.</summary>
    DailyReward,

    /// <summary>The first-week event: the current day, the schedule, and every day's progress and claims.</summary>
    FirstWeek,

    /// <summary>The daily and weekly missions.</summary>
    Missions,

    /// <summary>The spin wheel: its sectors, its odds, the spins in hand and any unseen result.</summary>
    SpinWheel,

    /// <summary>
    /// The seasonal tournament: the player's run, the group standings and the reward table. Also the competitive
    /// standing on <see cref="Identity"/>, which a pin on either slice keeps.
    /// </summary>
    Tournament,

    /// <summary>The Shop's featured slot, filled by the published demo product.</summary>
    Shop,

    /// <summary>
    /// The weekly themed event: the player's week, their points in it, and whether its reward is earned and
    /// claimed.
    /// </summary>
    WeeklyEvent,

    /// <summary>
    /// The cosmetics catalogue: every published item, what the player owns and wears, and unacknowledged
    /// acquisitions. The worn items also appear on <see cref="Identity"/>. A pin on this slice keeps the
    /// catalogue data, and a pin on <see cref="Identity"/> keeps the worn items.
    /// </summary>
    Cosmetics,
}

/// <summary>
/// A fixture scenario: its description and the slices it pins.
/// <para>
/// Once a session exists, real player state replaces every slice not listed in <see cref="Pinned"/>. A scenario
/// that shows a specific rung of the next-action ladder must also pin the slices that feed higher rungs, or
/// real state, such as a claimable daily reward, could take over the Home card.
/// </para>
/// </summary>
public sealed record ScenarioInfo(string Description, IReadOnlySet<FixtureSlice> Pinned)
{
    /// <summary>Pinned no slices, so real player state fills every slice.</summary>
    public static ScenarioInfo PinningNothing(string description) =>
        new ScenarioInfo(description, new HashSet<FixtureSlice>());

    /// <summary>Pinned the named slices. Real player state fills the others.</summary>
    public static ScenarioInfo Pinning(string description, params FixtureSlice[] slices) =>
        new ScenarioInfo(description, new HashSet<FixtureSlice>(slices));

    /// <summary>
    /// Pinned every feature slice, leaving <see cref="FixtureSlice.Identity"/>, <see cref="FixtureSlice.Wallet"/> and
    /// <see cref="FixtureSlice.Record"/> real. The loading, offline and error scenarios use this to put every feature
    /// surface in one state. Browser tests use the real generated player name to detect a live session.
    /// </summary>
    public static ScenarioInfo PinningEveryFeature(string description) => Pinning(description,
        FixtureSlice.DailyReward, FixtureSlice.FirstWeek, FixtureSlice.Missions,
        FixtureSlice.SpinWheel, FixtureSlice.Tournament, FixtureSlice.Shop, FixtureSlice.WeeklyEvent, FixtureSlice.Cosmetics);

    /// <summary>Pinned every slice, including the HUD wallet and the record.</summary>
    public static ScenarioInfo PinningEverything(string description) =>
        new ScenarioInfo(description, new HashSet<FixtureSlice>(Enum.GetValues<FixtureSlice>()));
}
