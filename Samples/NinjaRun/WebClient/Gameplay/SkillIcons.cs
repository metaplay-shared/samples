using Game.Logic;

namespace WebClient.Gameplay;

/// <summary>
/// The drawings Skills and their effects are shown with, as references into the portrait area's
/// <c>skill-icon-set</c> symbols.
/// </summary>
public static class SkillIcons
{
    /// <summary>The drawing a Skill's icon names.</summary>
    public static string Symbol(SkillIcon icon) => icon switch
    {
        SkillIcon.Tag      => "#skill-icon-tag",
        SkillIcon.Sandal   => "#skill-icon-sandal",
        SkillIcon.Shield   => "#skill-icon-shield",
        SkillIcon.Bolt     => "#skill-icon-bolt",
        SkillIcon.Dash     => "#skill-icon-dash",
        SkillIcon.Jump     => "#skill-icon-jump",
        SkillIcon.Mountain => "#skill-icon-mountain",
        _                  => "#skill-icon-tag",
    };

    /// <summary>The drawing an effect line on <paramref name="stat"/> is drawn with.</summary>
    public static string StatSymbol(SkillStat stat) => stat switch
    {
        SkillStat.Speed        => "#skill-icon-bolt",
        SkillStat.Acceleration => "#skill-icon-dash",
        SkillStat.Jump         => "#skill-icon-jump",
        SkillStat.Footing      => "#skill-icon-shield",
        _                      => "#skill-icon-bolt",
    };
}
