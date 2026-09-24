using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary> What is behind a seat. Every case is drawn on the plaque (<c>Docs/bots.md</c>). </summary>
    [MetaSerializable]
    public enum SeatOccupancy
    {
        /// <summary> A person, connected or not. </summary>
        Human = 0,
        /// <summary> A bot seated at formation. Shown as a computer player, with a name from the bot roster. </summary>
        Bot = 1,
        /// <summary>
        /// A person's seat played by a bot while they are away. The human's identity is retained so they can
        /// reclaim it — from the next turn boundary after they come back — and the plaque keeps their name and adds the computer-player mark: a bot playing
        /// anonymously under a human's name would break the table's honesty in the most common bot case there
        /// is.
        /// </summary>
        HumanCoveredByBot = 2,
    }

    /// <summary>
    /// One seat's public roster entry: who owns it, what is behind it, and whether they are here. A player
    /// identity appears in exactly this one place — the engine, the action history and the result all speak in
    /// seat indices — which is what makes changing who occupies a seat a one-field change
    /// (<c>Docs/match.md</c>, "Seats").
    /// </summary>
    [MetaSerializable]
    public class MatchSeat
    {
        /// <summary> The owning account, or <see cref="EntityId.None"/> for a seat that never had one. </summary>
        [MetaMember(1)] public EntityId      PlayerId    { get; set; }
        [MetaMember(2)] public string         DisplayName { get; set; }
        [MetaMember(3)] public SeatOccupancy  Occupancy   { get; set; }
        [MetaMember(4)] public bool           IsConnected { get; set; }
        /// <summary>
        /// Which profile plays this seat. Only meaningful while <see cref="Occupancy"/> is
        /// <see cref="SeatOccupancy.Bot"/> or <see cref="SeatOccupancy.HumanCoveredByBot"/>; a seat played on
        /// behalf of an absent human is always the strongest one, whatever this says.
        /// </summary>
        [MetaMember(5)] public BotProfileId   BotProfile  { get; set; }
        /// <summary>
        /// Consecutive turn deadlines this seat let lapse while its owner was connected. Any action from the
        /// seat puts it back to zero, and at the host's configured count a bot covers the seat. Public, and
        /// deliberately not drawn as a running total: the board says a lapse cost something and what the next
        /// one costs, rather than keeping a scoreboard of somebody's failures.
        /// </summary>
        [MetaMember(6)] public int            Strikes     { get; set; }
        /// <summary>
        /// A covered seat's owner is back and takes the seat at the next turn boundary; until then the bot keeps
        /// playing it and the owner's intents are refused. Public, because the board says so. Only meaningful
        /// while <see cref="Occupancy"/> is <see cref="SeatOccupancy.HumanCoveredByBot"/>.
        /// </summary>
        [MetaMember(7)] public bool           ReclaimPending { get; set; }

        public MatchSeat() { }

        public MatchSeat(EntityId playerId, string displayName, SeatOccupancy occupancy, BotProfileId botProfile)
        {
            PlayerId    = playerId;
            DisplayName = displayName;
            Occupancy   = occupancy;
            BotProfile  = botProfile;
        }

        /// <summary> Whether a policy plays this seat rather than a person. </summary>
        public bool IsBotDriven => Occupancy == SeatOccupancy.Bot || Occupancy == SeatOccupancy.HumanCoveredByBot;

        /// <summary> Whether this seat is a person who is here right now. </summary>
        public bool IsConnectedHuman => Occupancy == SeatOccupancy.Human && IsConnected;

        /// <summary> Whether the plaque carries the computer-player mark. </summary>
        public bool ShowsComputerMark => IsBotDriven;
    }
}
