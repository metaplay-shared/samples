using Metaplay.Core.Math;
using System;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Courses the tests run on. The small ones are built here, each around one mechanic, so a rule is visible on
    /// its own; the maps are real compositions of the parts in the committed game config, for whatever is about
    /// maps rather than about a mechanic.
    /// </summary>
    public static class TestCourses
    {
        static readonly Lazy<SharedGameConfig> _gameConfig = new Lazy<SharedGameConfig>(
            () => TestGameConfig.LoadSharedAsync().GetAwaiter().GetResult());

        /// <summary>The committed game config, read once for the tests that need a map without a set-up of their own.</summary>
        static SharedGameConfig GameConfig => _gameConfig.Value;

        /// <summary>The map offline practice plays, composed from the committed parts.</summary>
        public static CourseDefinition PracticeMap => CourseComposer.Compose(GameConfig.Global.OfflinePracticeMapId, GameConfig);

        /// <summary>A legal map other than <see cref="PracticeMap"/>, composed from the committed parts.</summary>
        public static CourseDefinition OtherMap
        {
            get
            {
                foreach (CourseId mapId in CourseComposer.LegalMapIds(GameConfig))
                {
                    if (mapId != GameConfig.Global.OfflinePracticeMapId)
                        return CourseComposer.Compose(mapId, GameConfig);
                }
                throw new InvalidOperationException("The committed parts compose into no map besides the practice map");
            }
        }

        /// <summary>Where <see cref="StepLedge"/>'s ground steps up, and so where its catchable face stands.</summary>
        static readonly F32 StepLedgeFaceX = F32.FromInt(30);

        /// <summary>
        /// Level ground that steps up at <see cref="StepLedgeFaceX"/> higher than a full-charge jump rises, so
        /// every runner meets the face the course derives there (a jump high up it, a runner on foot at its foot
        /// ) and is pulled up onto the ground above.
        /// </summary>
        public static CourseDefinition StepLedge => new CourseDefinition(
            CourseId.FromString("test-step-ledge"),
            "Step Ledge",
            startX:  F32.Zero,
            finishX: F32.FromInt(100),
            ground: new List<GroundSegment>
            {
                new GroundSegment(F32.FromInt(-12), StepLedgeFaceX, F32.Zero),
                new GroundSegment(StepLedgeFaceX, F32.FromInt(120), F32.Ratio10(32)),
            },
            hazards: new List<HazardSpan>());

        /// <summary>
        /// Take-off ground dropping at 20 m into a basin of spike beds, with a vine hung over the basin whose
        /// branch stands beyond the spikes. Missing the rope walks the basin, which is slower but still finishes.
        /// </summary>
        public static CourseDefinition Vine => VineOver(VineBasinHazards());

        /// <summary><see cref="Vine"/> with a spike bed lying under the branch too, for runners carried over it.</summary>
        public static CourseDefinition VineOverSpikes
        {
            get
            {
                List<HazardSpan> hazards = VineBasinHazards();
                hazards.Add(new HazardSpan(F32.FromInt(36), F32.FromInt(38)));
                return VineOver(hazards);
            }
        }

        static List<HazardSpan> VineBasinHazards()
            => new List<HazardSpan>
            {
                new HazardSpan(F32.FromInt(21), F32.FromInt(23)),
                new HazardSpan(F32.FromInt(24), F32.FromInt(26)),
                new HazardSpan(F32.FromInt(27), F32.FromInt(29)),
                new HazardSpan(F32.FromInt(30), F32.FromInt(31)),
            };

        static CourseDefinition VineOver(List<HazardSpan> hazards)
            => new CourseDefinition(
                CourseId.FromString("test-vine"),
                "Vine",
                startX:  F32.Zero,
                finishX: F32.FromInt(100),
                ground: new List<GroundSegment>
                {
                    new GroundSegment(F32.FromInt(-12), F32.FromInt(20), F32.Zero),
                    new GroundSegment(F32.FromInt(20), F32.FromInt(112), F32.FromInt(-3)),
                },
                hazards: hazards,
                encounters: new List<CourseEncounterDefinition>
                {
                    new VineEncounterDefinition(
                        "test-vine",
                        anchorX:        F32.FromInt(27),
                        anchorY:        F32.FromInt(8),
                        platformStartX: F32.FromInt(31),
                        platformEndX:   F32.FromInt(46),
                        platformTopY:   F32.FromInt(3)),
                });
    }
}
