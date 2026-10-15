using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System;

namespace Game.Logic
{
    /// <summary>
    /// Which side of the field a decoration is drawn on. <see cref="Behind"/> props stand in the course's own
    /// plane, drawn with the torii's far layer (before the terrain, so the terrain covers their base row and
    /// they read as planted) and so behind every runner. <see cref="Front"/> props stand nearer the camera and
    /// are drawn last, over the runners, the ghosts and the course.
    /// </summary>
    [MetaSerializable]
    public enum CourseDecorationLayer
    {
        Behind = 0,
        Front  = 1,
    }

    /// <summary>
    /// A purely cosmetic prop a part places by hand, such as a stone lantern, in part-local metres. It stands on
    /// the part's ground at <see cref="X"/>, the way a spike bed does: its height comes from the ground under it
    /// rather than being authored, so a prop moved along the part stays planted.
    /// <para>
    /// A decoration is art and nothing else. The simulation, the recording and the server's recomputation of
    /// a run never read it, so placing, moving or removing one changes nothing a run does. What a kind looks
    /// like (its sprite, its size, whether it plays a pass-by animation) is the renderer's catalogue
    /// (<c>WebClient/wwwroot/js/courseDecorations.js</c>), keyed by <see cref="Kind"/>.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public sealed class CoursePartDecoration
    {
        /// <summary>The kind of prop: a key into the renderer's decoration catalogue, for example <c>stone-lantern</c>.</summary>
        [MetaMember(1)] public string                Kind  { get; private set; }

        /// <summary>Where along the part it stands, part-local metres.</summary>
        [MetaMember(2)] public F32                   X     { get; private set; }

        [MetaMember(3)] public CourseDecorationLayer Layer { get; private set; }

        /// <summary>Whether the sprite is drawn mirrored left to right.</summary>
        [MetaMember(4)] public bool                  Flip  { get; private set; }

        CoursePartDecoration() { }

        public CoursePartDecoration(string kind, F32 x, CourseDecorationLayer layer, bool flip = false)
        {
            Kind  = kind;
            X     = x;
            Layer = layer;
            Flip  = flip;
        }

        /// <summary>
        /// Whether <paramref name="kind"/> is written the way a kind id is: lower-case letters and digits in
        /// words joined by single hyphens.
        /// </summary>
        public static bool IsWellFormedKind(string kind)
        {
            if (string.IsNullOrEmpty(kind) || kind[0] == '-' || kind[kind.Length - 1] == '-')
                return false;

            for (int ndx = 0; ndx < kind.Length; ndx++)
            {
                char c = kind[ndx];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || (c == '-' && kind[ndx - 1] != '-');
                if (!ok)
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// A decoration as a composed course carries it: in course metres, standing on the ground at
    /// (<see cref="X"/>, <see cref="Y"/>), which the composer reads off the ground under the part-local X. Like
    /// the rest of a course's presentation, nothing that moves the runner reads it.
    /// </summary>
    public sealed class CourseDecoration
    {
        /// <summary>A name unique within the course: the part's place in the map and the decoration's in the part.</summary>
        public string                Id    { get; }
        public string                Kind  { get; }
        public F32                   X     { get; }

        /// <summary>The height of the ground the decoration stands on.</summary>
        public F32                   Y     { get; }
        public CourseDecorationLayer Layer { get; }
        public bool                  Flip  { get; }

        public CourseDecoration(string id, string kind, F32 x, F32 y, CourseDecorationLayer layer, bool flip)
        {
            Id    = id ?? throw new ArgumentNullException(nameof(id));
            Kind  = kind ?? throw new ArgumentNullException(nameof(kind));
            X     = x;
            Y     = y;
            Layer = layer;
            Flip  = flip;
        }
    }
}
