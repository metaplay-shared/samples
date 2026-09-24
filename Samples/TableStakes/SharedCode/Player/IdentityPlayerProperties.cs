using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// The number of games this player has finished, as a targetable player property (<c>docs/offers.md</c>,
    /// "Player properties"). There is no win-rate property: a segment can combine games played and games won.
    /// The display name is not a property, because the name policy forbids player-chosen text as a targeting
    /// dimension.
    /// <para>
    /// This property, <see cref="PlayerPropertyGamesWon"/> and <see cref="PlayerPropertyHasCustomizedName"/>
    /// read <see cref="PlayerModel.TargetingFacts"/>. See <see cref="PlayerTargetingFacts"/> for why.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(1003)]
    public class PlayerPropertyGamesPlayed : TypedPlayerPropertyId<int>
    {
        public override string DisplayName => "Games played";

        public override int GetTypedValueForPlayer(IPlayerModelBase player) =>
            player is PlayerModel model ? model.TargetingFacts.GamesPlayed : 0;
    }

    /// <summary>The number of games this player has won. See <see cref="PlayerPropertyGamesPlayed"/>.</summary>
    [MetaSerializableDerived(1004)]
    public class PlayerPropertyGamesWon : TypedPlayerPropertyId<int>
    {
        public override string DisplayName => "Games won";

        public override int GetTypedValueForPlayer(IPlayerModelBase player) =>
            player is PlayerModel model ? model.TargetingFacts.GamesWon : 0;
    }

    /// <summary>
    /// Whether this player has ever renamed themselves. A chosen name is a free signal that the player
    /// intends to keep playing.
    /// </summary>
    [MetaSerializableDerived(1005)]
    public class PlayerPropertyHasCustomizedName : TypedPlayerPropertyId<bool>
    {
        public override string DisplayName => "Has customized name";

        public override bool GetTypedValueForPlayer(IPlayerModelBase player) =>
            player is PlayerModel model && model.TargetingFacts.HasCustomizedName;
    }

    /// <summary>
    /// The number of whole days since the account was created, as an integer so a segment bound reads <c>6</c>
    /// rather than <c>6.00:00:00</c>. It counts elapsed 24-hour periods, not local calendar days, because the
    /// player's UTC offset changes with each login and a calendar-day age would change with it.
    /// <para>
    /// It is clamped at zero. The creation time can be later than the model's clock, for example for an
    /// imported account or after a server clock change, and a negative age would fall outside every range.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(1006)]
    public class PlayerPropertyAccountAgeDays : TypedPlayerPropertyId<int>
    {
        public override string DisplayName => "Account age (days)";

        public override int GetTypedValueForPlayer(IPlayerModelBase player)
        {
            if (player?.Stats == null)
                return 0;

            MetaDuration age = player.CurrentTime - player.Stats.CreatedAt;
            if (age <= MetaDuration.Zero)
                return 0;

            return (int)(age.Milliseconds / MetaDuration.Day.Milliseconds);
        }
    }
}
