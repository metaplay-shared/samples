using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// A part carried by value rather than by id: the part editor's draft, which is in no published config and
    /// so has no id <see cref="CourseResolver"/> answers for. It rides on the practice ticket
    /// (<see cref="TimeTrialTicket.DraftPart"/>) so the run screen and the server compose the same course out
    /// of it: the server recomputes a recording's result against the geometry the client played, which for a
    /// published part it looks up by id and for a draft it reads off the ticket.
    /// <para>
    /// A type of its own rather than a <see cref="CoursePartInfo"/> member, because the serializer writes a game
    /// config item as its key and looks the key up on the way back in, and a draft's key names nothing. The
    /// fields are the part's, and <see cref="ToPart"/> turns them back into one for the composer.
    /// </para>
    /// </summary>
    [MetaSerializable]
    [MetaBlockedMembers(6)]
    public sealed class DraftCoursePart
    {
        [MetaMember(1)] public CoursePartId             Id            { get; private set; }
        [MetaMember(2)] public List<GroundSegment>      Ground        { get; private set; }
        [MetaMember(3)] public List<HazardSpan>         Hazards       { get; private set; }
        [MetaMember(4)] public List<CoursePartPosition> Positions     { get; private set; }
        [MetaMember(5)] public bool                     IsSetPiece    { get; private set; }
        [MetaMember(7)] public List<CoursePartBridge>   Bridges       { get; private set; }
        [MetaMember(8)] public List<CoursePartDecoration> Decorations { get; private set; }
        [MetaMember(9)] public List<CoursePartVine>     Vines         { get; private set; }

        /// <summary>
        /// How many rows of one kind a draft may carry, which bounds the course a developer-only action can make
        /// the server compose and run.
        /// </summary>
        public const int MaxRowsPerList = 64;

        DraftCoursePart() { }

        /// <summary><paramref name="part"/>'s fields, carried by value.</summary>
        public DraftCoursePart(CoursePartInfo part)
        {
            if (part == null)
                throw new ArgumentNullException(nameof(part));

            Id            = part.Id;
            Ground        = new List<GroundSegment>(part.Ground);
            Hazards       = new List<HazardSpan>(part.Hazards);
            Positions     = new List<CoursePartPosition>(part.Positions);
            IsSetPiece    = part.IsSetPiece;
            Bridges       = new List<CoursePartBridge>(part.Bridges);
            Decorations   = new List<CoursePartDecoration>(part.Decorations);
            Vines         = new List<CoursePartVine>(part.Vines);
        }

        /// <summary>
        /// The part these fields describe, for <see cref="CoursePartInfo.Validate"/> and the composer. A list the
        /// draft arrived without is an empty one.
        /// </summary>
        public CoursePartInfo ToPart()
            => new CoursePartInfo(Id, Ground, Hazards, Positions, IsSetPiece, Bridges, Decorations, Vines);

        /// <summary>
        /// Whether no list holds an empty row. A draft arrives from a client, and a part's rules and the composer
        /// read every row of a part they are handed, so a hole in one is refused here rather than met there.
        /// </summary>
        public bool HasNoEmptyRows()
            => HasNoEmptyRow(Ground)
            && HasNoEmptyRow(Hazards)
            && HasNoEmptyRow(Bridges)
            && HasNoEmptyRow(Decorations)
            && HasNoEmptyRow(Vines);

        static bool HasNoEmptyRow<T>(List<T> rows) where T : class
            => rows == null || !rows.Contains(null);

        /// <summary>Whether every list is within <see cref="MaxRowsPerList"/>. A null list counts as empty.</summary>
        public bool IsWithinBounds()
            => (Ground?.Count ?? 0) <= MaxRowsPerList
            && (Hazards?.Count ?? 0) <= MaxRowsPerList
            && (Bridges?.Count ?? 0) <= MaxRowsPerList
            && (Decorations?.Count ?? 0) <= MaxRowsPerList
            && (Vines?.Count ?? 0) <= MaxRowsPerList
            && (Positions?.Count ?? 0) <= MaxRowsPerList;

        /// <summary>
        /// The draft composed alone through <see cref="CourseInspection.PartAlone"/>: the course a run on the
        /// draft is played and its recording recomputed on. Null for a draft that cannot be composed.
        /// </summary>
        public CourseDefinition TryComposeAlone(GlobalConfig global)
        {
            if (global == null || !IsWithinBounds() || !HasNoEmptyRows())
                return null;

            try
            {
                return CourseInspection.PartAlone(ToPart(), global);
            }
            catch (ArgumentException)
            {
                // The composer and the course constructor throw for a composition they refuse.
                return null;
            }
        }
    }
}
