using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// A <see cref="BotProfile"/> by name, in a form that can ride the wire with a match's seat roster. The
    /// profile itself is a plain object holding two tuning numbers and is deliberately not serializable: a
    /// seat names a profile rather than carrying a copy of its numbers.
    /// </summary>
    [MetaSerializable]
    public enum BotProfileId
    {
        Strongest             = 0,
        StrictlyDeterministic = 1,
        Practiced             = 2,
        Casual                = 3,
        Sloppy                = 4,
    }

    /// <summary> Resolving a <see cref="BotProfileId"/> to the profile it names. </summary>
    public static class BotProfiles
    {
        /// <summary>
        /// The profile this id names. An id with no profile behind it resolves to
        /// <see cref="BotProfile.Strongest"/>: a seat played by an unknown profile is played well rather than
        /// not at all, which is the safe direction when somebody's collection is on the table.
        /// </summary>
        public static BotProfile Resolve(BotProfileId id)
        {
            string name = id.ToString();
            foreach (BotProfile profile in BotProfile.All)
            {
                if (profile.Name == name)
                    return profile;
            }

            return BotProfile.Strongest;
        }
    }
}
