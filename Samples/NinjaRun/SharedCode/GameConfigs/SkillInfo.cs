using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Identity of a Skill. Stored in player state, on tickets and on stored ghosts, so ids are permanent.
    /// </summary>
    [MetaSerializable]
    public class SkillId : StringId<SkillId> { }

    /// <summary>
    /// What the client draws for a Skill wherever it is shown as a medallion. The config only names the icon;
    /// the drawings themselves belong to the client.
    /// </summary>
    [MetaSerializable]
    public enum SkillIcon
    {
        Tag,
        Sandal,
        Shield,
        Bolt,
        Dash,
        Jump,
        Mountain,
    }

    /// <summary>
    /// One node of the Skill tree: what it costs in Orbs, what it needs bought before it, and how it moves the
    /// movement values a run is simulated with. Built from <c>GameConfigSource/Skills.csv</c>.
    /// <para>
    /// Each modifier is a permille of one movement stat relative to the base rules. <see cref="UnchangedPermille"/>
    /// leaves the stat alone, above it is a benefit and below it a penalty.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class SkillInfo : IGameConfigData<SkillId>
    {
        /// <summary>A modifier that leaves its member exactly as the base rules have it.</summary>
        public const int UnchangedPermille = 1000;

        /// <summary>The lowest tier a Skill can be equipped from: tier 1 is the Name Tag, which changes nothing.</summary>
        public const int LowestEquippableTier = 2;

        [MetaMember(1)] public SkillId Id          { get; private set; }
        [MetaMember(2)] public string  DisplayName { get; private set; }

        /// <summary>Depth in the tree, 1 at the root. A prerequisite always sits exactly one tier lower.</summary>
        [MetaMember(3)] public int Tier { get; private set; }

        /// <summary>The Skill that has to be owned before this one can be bought. Null on the root alone.</summary>
        [MetaMember(4)] public SkillId Prerequisite { get; private set; }

        /// <summary>Orbs this Skill costs to unlock. Paid once; owning a Skill is permanent.</summary>
        [MetaMember(5)] public int OrbCost { get; private set; }

        // Member ids 9, 10, 11 and 13 are retired and may not be reused.

        [MetaMember(6)]  public int RunSpeedPermille        { get; private set; } = UnchangedPermille;
        [MetaMember(7)]  public int RunAccelerationPermille { get; private set; } = UnchangedPermille;
        [MetaMember(8)]  public int MaxLaunchSpeedPermille  { get; private set; } = UnchangedPermille;

        /// <summary>
        /// Footing: the stumble and the two fixed phases of a mantle, the hang and the top transition. Each of
        /// those step counts is divided by it, so better footing is a shorter stumble and a quicker mantle.
        /// </summary>
        [MetaMember(14)] public int FootingPermille         { get; private set; } = UnchangedPermille;

        /// <summary>The icon the client draws this Skill with.</summary>
        [MetaMember(12)] public SkillIcon Icon { get; private set; }

        public SkillId ConfigKey => Id;

        /// <summary>
        /// Whether this Skill can be put in the player's one slot. The Name Tag, which moves nothing, cannot.
        /// </summary>
        public bool IsEquippable => Tier >= LowestEquippableTier;

        /// <summary>
        /// Every stat this Skill moves and the permille it moves it by: benefits first and penalties after, each
        /// group in <see cref="SkillStat"/> order. A stat left at <see cref="UnchangedPermille"/> is not listed.
        /// Built from the members above on every call into a new list.
        /// </summary>
        public List<SkillEffect> GetAllEffects()
        {
            List<SkillEffect> effects = new List<SkillEffect>(AllStats.Count);

            foreach (SkillStat stat in AllStats)
            {
                int permille = PermilleOf(stat);
                if (permille > UnchangedPermille)
                    effects.Add(new SkillEffect(stat, permille));
            }

            foreach (SkillStat stat in AllStats)
            {
                int permille = PermilleOf(stat);
                if (permille < UnchangedPermille)
                    effects.Add(new SkillEffect(stat, permille));
            }

            return effects;
        }

        /// <summary>Every <see cref="SkillStat"/>, in declaration order.</summary>
        public static readonly IReadOnlyList<SkillStat> AllStats = new SkillStat[]
        {
            SkillStat.Speed, SkillStat.Acceleration, SkillStat.Jump, SkillStat.Footing,
        };

        /// <summary>The permille this Skill moves <paramref name="stat"/> by.</summary>
        public int PermilleOf(SkillStat stat)
        {
            switch (stat)
            {
                case SkillStat.Speed:        return RunSpeedPermille;
                case SkillStat.Acceleration: return RunAccelerationPermille;
                case SkillStat.Jump:         return MaxLaunchSpeedPermille;
                case SkillStat.Footing:      return FootingPermille;
                default:                     return UnchangedPermille;
            }
        }

        /// <summary>The sheet column that holds <paramref name="stat"/>'s permille.</summary>
        public static string ColumnOf(SkillStat stat)
        {
            switch (stat)
            {
                case SkillStat.Speed:        return nameof(RunSpeedPermille);
                case SkillStat.Acceleration: return nameof(RunAccelerationPermille);
                case SkillStat.Jump:         return nameof(MaxLaunchSpeedPermille);
                case SkillStat.Footing:      return nameof(FootingPermille);
                default:                     return stat.ToString();
            }
        }
    }

    /// <summary>The movement stats a Skill can move, one sheet column each.</summary>
    public enum SkillStat { Speed, Acceleration, Jump, Footing }

    /// <summary>
    /// One stat a Skill moves and the permille it moves it by. Above <see cref="SkillInfo.UnchangedPermille"/> is
    /// a benefit for every stat.
    /// </summary>
    public readonly record struct SkillEffect(SkillStat Stat, int Permille)
    {
        public bool IsBenefit => Permille > SkillInfo.UnchangedPermille;
    }

    /// <summary>
    /// Where a player stands in the Name Tag tutorial: the wordless walk from their first Orbs to a name on the
    /// board. Read off <see cref="PlayerModel.NameTagTutorialStep"/> whenever it is needed and never stored or
    /// sent, so it cannot drift from the model it describes.
    /// </summary>
    public enum NameTagTutorialStep
    {
        /// <summary>No tutorial: the player is named, or cannot yet afford the Name Tag.</summary>
        None,

        /// <summary>The player can afford the Name Tag and does not own it yet.</summary>
        BuyNameTag,

        /// <summary>The player owns the Name Tag and has not set a name.</summary>
        SetName,
    }

    /// <summary>
    /// Turns a Skill into the movement values a run is simulated with, and holds the Name Tag's id and the name
    /// a player without it goes by.
    /// </summary>
    public static class TimeTrialSkills
    {
        /// <summary>The root of the tree: what it buys is the right to set a name, not movement.</summary>
        public static readonly SkillId NameTag = SkillId.FromString("name-tag");

        /// <summary>What a player without the Name Tag is called wherever their ghost is named.</summary>
        public const string MaskedNinjaName = "Masked Ninja";

        /// <summary>
        /// A new set of movement values: the published ones with <paramref name="skill"/>'s modifiers applied.
        /// A pure function, so client and server derive the same values. The rules handed in are never written
        /// to, because they are the game config every run shares.
        /// </summary>
        public static TimeTrialRules EffectiveRules(TimeTrialRules baseRules, SkillInfo skill)
        {
            TimeTrialRules rules = baseRules.Copy();
            if (skill == null)
                return rules;

            rules.RunSpeed        = Scale(rules.RunSpeed,        skill.RunSpeedPermille);
            rules.RunAcceleration = Scale(rules.RunAcceleration, skill.RunAccelerationPermille);
            rules.MaxLaunchSpeed  = Scale(rules.MaxLaunchSpeed,  skill.MaxLaunchSpeedPermille);

            // Footing is a rate the stumble's step count is divided by, so better footing is a shorter stumble.
            rules.StumbleSteps = ScaleInverse(rules.StumbleSteps, skill.FootingPermille, minimum: 0);

            // The two fixed phases of a mantle, divided by footing too. Each is a divisor the phase's progress is
            // read against, so neither may be scaled away to nothing; the rise between them is a speed and is
            // left alone.
            rules.LedgeGrabHangSteps          = ScaleInverse(rules.LedgeGrabHangSteps,          skill.FootingPermille, minimum: 1);
            rules.LedgeGrabTopTransitionSteps = ScaleInverse(rules.LedgeGrabTopTransitionSteps, skill.FootingPermille, minimum: 1);

            return rules;
        }

        /// <summary>
        /// The movement values a run with <paramref name="skillId"/> equipped is simulated with. A null id, or one
        /// the published config no longer holds, gets the base rules, so a Skill retired from the sheet costs a
        /// player their bonus and never their run.
        /// </summary>
        public static TimeTrialRules EffectiveRules(SharedGameConfig gameConfig, SkillId skillId)
            => EffectiveRules(gameConfig.TimeTrialRules, TryGetSkill(gameConfig, skillId));

        /// <summary>The Skill <paramref name="skillId"/> names, or null for no Skill and for an unknown id.</summary>
        public static SkillInfo TryGetSkill(SharedGameConfig gameConfig, SkillId skillId)
        {
            if (gameConfig == null || skillId == null)
                return null;

            return gameConfig.Skills.GetValueOrDefault(skillId);
        }

        /// <summary>
        /// A fixed-point value scaled by a permille, computed on the raw value so it truncates once.
        /// </summary>
        static F32 Scale(F32 value, int permille)
            => permille == SkillInfo.UnchangedPermille ? value : F32.FromRaw((int)((long)value.Raw * permille / 1000));

        /// <summary>
        /// A step count divided by a rate permille, so a rate above <see cref="SkillInfo.UnchangedPermille"/> is
        /// fewer steps and one below is more. The minimum is applied after the division.
        /// </summary>
        static int ScaleInverse(int value, int permille, int minimum)
        {
            if (permille == SkillInfo.UnchangedPermille)
                return value;

            int scaled = (int)((long)value * 1000 / permille);
            return scaled < minimum ? minimum : scaled;
        }
    }
}
