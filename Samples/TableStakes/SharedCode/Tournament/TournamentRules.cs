using Metaplay.Core;
using Metaplay.Core.League;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Seasonal tournament rules that the client and the server must agree on
    /// (<c>docs/seasonal-tournament.md</c>). They are code constants, not game config, because the league manager
    /// that sizes a division has no game config to read, and a config update could make the scored-match cap
    /// differ between human progress and <see cref="TournamentBots"/>. Values a designer tunes without changing
    /// the competition's structure are in <see cref="TournamentRewardTableInfo"/>.
    /// </summary>
    public static class TournamentRules
    {
        /// <summary>Seats in one tournament group, chosen so the whole standings list is readable on a phone.</summary>
        public const int GroupSize = 20;

        /// <summary>
        /// How many completed matches per player count towards the season. Later matches do not count, which
        /// limits the advantage of playing more.
        /// </summary>
        public const int ScoredMatchCap = 10;

        /// <summary>
        /// Points per win. A completed loss or draw uses a scored match and gives no points.
        /// <para>
        /// Only <c>TournamentDivisionModel.ComputeScore</c> applies it, to turn a participant's wins into the score
        /// the SDK ranks by. The player state, the bots and <see cref="TournamentStandings"/> all count wins, which
        /// gives the same order as points for any positive value. Applying it anywhere else would make the SDK's
        /// order and the player's standings disagree once this value is not 1.
        /// </para>
        /// </summary>
        public const int PointsPerWin = 1;

        /// <summary>
        /// The most wins a bot can reach. It is below <see cref="ScoredMatchCap"/>, so a player who wins every
        /// scored match finishes ahead of every bot.
        /// </summary>
        public const int BotMaxWins = 8;

        /// <summary>The league's only rank. There are no player-facing tiers and no promotion.</summary>
        public const int OnlyRank = 0;

        /// <summary>The id of the game's only league manager.</summary>
        public const int LeagueId = 0;

    }

    /// <summary>
    /// One unranked row of a tournament group: a human participant or a bot, in the same form so both can be
    /// sorted together.
    /// </summary>
    public readonly struct TournamentEntrant
    {
        /// <summary>
        /// The division participant index. The league manager gives a joining human the lowest free index, and
        /// bots hold the other indices.
        /// </summary>
        public int ParticipantIndex { get; }

        /// <summary>Whether this entrant is a bot.</summary>
        public bool IsBot { get; }

        public PlayerPublicIdentity Identity { get; }

        /// <summary>The number of wins. The standings rank by wins (see <see cref="TournamentRules.PointsPerWin"/>).</summary>
        public int Wins { get; }

        /// <summary>Completed scored matches, at most <see cref="TournamentRules.ScoredMatchCap"/>.</summary>
        public int ScoredMatches { get; }

        /// <summary>When this entrant reached its current win total. Used as the third tie-break.</summary>
        public MetaTime LastWinAt { get; }

        public int Losses => ScoredMatches - Wins;

        public TournamentEntrant(int participantIndex, bool isBot, PlayerPublicIdentity identity, int wins, int scoredMatches, MetaTime lastWinAt)
        {
            ParticipantIndex = participantIndex;
            IsBot            = isBot;
            Identity         = identity;
            Wins             = wins;
            ScoredMatches    = scoredMatches;
            LastWinAt        = lastWinAt;
        }
    }

    /// <summary>
    /// The ranking of a tournament group.
    /// <para>
    /// The client's standings, the division actor's placement at season end, and the tests all use these pure
    /// functions, so they cannot disagree. The order is: more wins, then fewer losses, then the earlier time the
    /// win total was reached, then the lower seat index. The seat index makes the order total, so no two seats
    /// ever tie.
    /// </para>
    /// </summary>
    public static class TournamentStandings
    {
        /// <summary>
        /// Compares two seats. A negative result means <paramref name="lhs"/> places ahead of
        /// <paramref name="rhs"/>. It orders by wins, which matches the SDK's order by points (see
        /// <see cref="TournamentRules.PointsPerWin"/>).
        /// </summary>
        public static int Compare(in TournamentEntrant lhs, in TournamentEntrant rhs)
        {
            if (lhs.Wins != rhs.Wins)
                return rhs.Wins.CompareTo(lhs.Wins);
            if (lhs.Losses != rhs.Losses)
                return lhs.Losses.CompareTo(rhs.Losses);
            if (lhs.LastWinAt != rhs.LastWinAt)
                return lhs.LastWinAt.CompareTo(rhs.LastWinAt);
            return lhs.ParticipantIndex.CompareTo(rhs.ParticipantIndex);
        }

        /// <summary>
        /// The seats in placement order, best first. A seat's placement is its list index plus one, with no shared
        /// placements.
        /// </summary>
        public static List<TournamentEntrant> Rank(IEnumerable<TournamentEntrant> seats)
        {
            List<TournamentEntrant> ordered = new List<TournamentEntrant>(seats);
            ordered.Sort((lhs, rhs) => Compare(lhs, rhs));
            return ordered;
        }

        /// <summary>
        /// The one-based placement of <paramref name="seat"/> in <paramref name="ranked"/>, or 0 if it is not there.
        /// </summary>
        public static int PlacementOf(IReadOnlyList<TournamentEntrant> ranked, int seat)
        {
            for (int index = 0; index < ranked.Count; index++)
            {
                if (ranked[index].ParticipantIndex == seat)
                    return index + 1;
            }
            return 0;
        }
    }

    /// <summary>
    /// Bots for the empty seats of a tournament group, so a joining player never sees an empty leaderboard.
    /// Bots are not stored: identity, target and progress are computed with integer arithmetic from the
    /// division, the seat and the time, so the client and the division actor agree and a reconnect cannot change
    /// a bot.
    /// <para>
    /// Lower seats hold weaker bots, and the league manager gives a joining human the lowest free seat. Each human
    /// therefore replaces the weakest remaining bot and the standings do not reshuffle.
    /// </para>
    /// </summary>
    public static class TournamentBots
    {
        /// <summary>The slowest and fastest bot pace relative to the season clock, in percent.</summary>
        const int MinPacePercent = 70;
        const int MaxPacePercent = 130;

        /// <summary>
        /// The <see cref="SeedStreams"/> key of each per-seat draw. A new per-seat draw needs a new odd constant.
        /// </summary>
        const ulong PaceStream       = 0x9E3779B97F4A7C15UL;
        const ulong IdentityStream   = 0xD6E8FEB86659FD93UL;
        const ulong NameEffectStream = 0x2545F4914F6CDD1DUL;
        const ulong AvatarStream     = 0xA24BAED4963EE407UL;

        /// <summary>
        /// The bots of one group: one for every seat from 1 to <paramref name="groupSize"/> (at most
        /// <see cref="TournamentRules.GroupSize"/>) that is not in <paramref name="occupiedSeats"/>.
        /// </summary>
        public static List<TournamentEntrant> Fill(
            SharedGameConfig     config,
            DivisionIndex        division,
            int                  groupSize,
            HashSet<int>         occupiedSeats,
            MetaTime             startsAt,
            MetaTime             endsAt,
            MetaTime             now)
        {
            List<TournamentEntrant> bots = new List<TournamentEntrant>();

            // WinTargets always has TournamentRules.GroupSize entries, whatever groupSize the caller passes. The
            // targets are sorted before they are assigned to seats, so drawing a different number of them would
            // change every seat's target, not only the last. groupSize comes from replicated division state and
            // decides only how many seats are filled.
            int[] targets = WinTargets(division);

            for (int seat = 1; seat <= groupSize && seat <= targets.Length; seat++)
            {
                if (occupiedSeats != null && occupiedSeats.Contains(seat))
                    continue;

                bots.Add(Seat(config, division, seat, targets[seat - 1], startsAt, endsAt, now));
            }

            return bots;
        }

        /// <summary>
        /// The final win totals for a group's seats, in ascending order. The seed depends only on the division, so
        /// a group always gets the same targets on the client and the server at any time in the season.
        /// </summary>
        public static int[] WinTargets(DivisionIndex division)
        {
            RandomPCG random  = RandomPCG.CreateFromSeed(Seed(division));
            int[]     targets = new int[TournamentRules.GroupSize];

            for (int index = 0; index < targets.Length; index++)
                targets[index] = random.NextInt(TournamentRules.BotMaxWins + 1);

            Array.Sort(targets);
            return targets;
        }

        /// <summary>One bot's progress at <paramref name="now"/>.</summary>
        public static TournamentEntrant Seat(
            SharedGameConfig     config,
            DivisionIndex        division,
            int                  seat,
            int                  winTarget,
            MetaTime             startsAt,
            MetaTime             endsAt,
            MetaTime             now)
        {
            int pacePercent = Pace(division, seat);
            int permille    = PacedPermille(startsAt, endsAt, now, pacePercent);

            int wins          = (int)((long)winTarget * permille / 1000);
            int scoredMatches = (int)((long)TournamentRules.ScoredMatchCap * permille / 1000);
            if (scoredMatches < wins)
                scoredMatches = wins;

            // The time of this bot's latest win: the point in the season proportional to wins / target. It breaks
            // ties between seats with equal wins and losses.
            MetaTime lastWinAt = startsAt;
            if (wins > 0 && winTarget > 0)
            {
                long seasonLengthMs = (endsAt - startsAt).Milliseconds;
                lastWinAt = startsAt + MetaDuration.FromMilliseconds(seasonLengthMs * wins / winTarget);
            }

            return new TournamentEntrant(seat, isBot: true, Identity(config, division, seat), wins, scoredMatches, lastWinAt);
        }

        /// <summary>
        /// How far a bot is towards its target, in permille, after applying its pace. It is 0 before the season
        /// starts and 1000 once the season has ended. A bot with a pace above 100 percent reaches 1000 before the end.
        /// </summary>
        static int PacedPermille(MetaTime startsAt, MetaTime endsAt, MetaTime now, int pacePercent)
        {
            long seasonLengthMs = (endsAt - startsAt).Milliseconds;
            if (seasonLengthMs <= 0)
                return 1000;

            long elapsedMs = (now - startsAt).Milliseconds;
            if (elapsedMs <= 0)
                return 0;
            if (elapsedMs >= seasonLengthMs)
                return 1000;

            long permille = elapsedMs * 1000 / seasonLengthMs * pacePercent / 100;
            return permille >= 1000 ? 1000 : (int)permille;
        }

        /// <summary>A bot's pace in percent, drawn per seat so the bots in a group do not all progress at the same rate.</summary>
        static int Pace(DivisionIndex division, int seat)
        {
            RandomPCG random = SeedStreams.Stream(Seed(division), seat, PaceStream);
            return random.NextIntMinMax(MinPacePercent, MaxPacePercent + 1);
        }

        /// <summary>
        /// A bot's public identity: a name from the player name generator and cosmetics drawn by
        /// <see cref="CosmeticDraws"/>. The synthesized entity id is only the name generator's input and addresses
        /// no entity.
        /// <para>
        /// Each per-seat draw uses its own <see cref="SeedStreams"/> key. Each key is multiplied by the seat number,
        /// so seats must start at one (see <see cref="Fill"/>): seat 0 would make every draw use the division seed
        /// alone. A caller that numbers seats from zero must add one, as <see cref="BotSeatIdentity"/> does.
        /// </para>
        /// </summary>
        public static PlayerPublicIdentity Identity(SharedGameConfig config, DivisionIndex division, int seat)
        {
            ulong   value = SeedStreams.Seed(Seed(division), seat, IdentityStream) & ((1UL << EntityId.NumValueBits) - 1);
            EntityId id   = EntityId.Create(EntityKindCore.Player, value);

            string displayName = DisplayNameGenerator.Generate(config, id, DisplayNameGenerator.FallbackName(id)).Name;

            return PlayerPublicIdentity.ForBot(
                id,
                displayName,
                avatarId:     AvatarId(config, division, seat),
                nameEffectId: CosmeticDraws.RolledNameEffect(config, NameEffectRandom(division, seat)));
        }

        /// <summary>
        /// The random stream for the name effect draw, with its own key (see <see cref="Identity"/>).
        /// </summary>
        static RandomPCG NameEffectRandom(DivisionIndex division, int seat) =>
            SeedStreams.Stream(Seed(division), seat, NameEffectStream);

        /// <summary>
        /// The bot's avatar, drawn with <see cref="CosmeticDraws.PurchasableAvatar"/> from a stream keyed to this
        /// seat. It depends only on the division and the seat, so every viewer sees the same avatar on every
        /// reload.
        /// </summary>
        static CosmeticId AvatarId(SharedGameConfig config, DivisionIndex division, int seat) =>
            CosmeticDraws.PurchasableAvatar(config, SeedStreams.Stream(Seed(division), seat, AvatarStream));

        /// <summary>The group's seed, computed from the league, season, rank and division.</summary>
        static ulong Seed(DivisionIndex division)
        {
            ulong seed = (ulong)(uint)division.League;
            seed = (seed * 0x9E3779B97F4A7C15UL) ^ (ulong)(uint)division.Season;
            seed = (seed * 0xBF58476D1CE4E5B9UL) ^ (ulong)(uint)division.Rank;
            seed = (seed * 0x94D049BB133111EBUL) ^ (ulong)(uint)division.Division;
            return seed;
        }
    }
}
