using Game.Logic;

namespace WebClient.Gameplay;

/// <summary>
/// What the player carries, as the medallion and the customize view both show it, read off the model; a
/// player not yet loaded carries nothing.
/// </summary>
public static class PlayerLoadout
{
    /// <summary>
    /// What is in the one Skill slot, or null for an empty slot. This reads the Skill a run opened now would
    /// be played with, so a Skill that has left the published config reads as an empty slot here exactly as
    /// it would on the ticket.
    /// </summary>
    public static SkillInfo? EquippedSkill(PlayerModel? player)
        => player != null ? player.GetSkill(player.ActiveSkillId) : null;

    /// <summary>Whether the player owns the Name Tag, which is what puts the name plaque on the medallion and opens the name field.</summary>
    public static bool HasNameTag(PlayerModel? player) => player?.HasSkill(TimeTrialSkills.NameTag) == true;
}
