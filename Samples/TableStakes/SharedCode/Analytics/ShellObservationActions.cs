using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;

namespace Game.Logic
{
    public static partial class ActionCodes
    {
        // Client observation actions. The events they emit are in ShellAnalyticsEvents.cs.
        public const int PlayerObserveScreenViewed          = 5005;
        public const int PlayerObservePromotedEntrySelected = 5006;
    }

    public static partial class ActionResults
    {
        /// <summary>An observation named a screen or placement that is Unknown or not a defined enum value.</summary>
        public static readonly MetaActionResult UnknownObservation = new MetaActionResult(nameof(UnknownObservation));
    }

    /// <summary>
    /// Reports that a top-level route became the visible screen, and emits <see cref="PlayerEventScreenViewed"/>.
    /// <para>
    /// It changes no model state. Every event the client may submit is trusted client input, so the set is kept
    /// small (<c>docs/analytics.md</c>). The client debounces screen views before it enqueues this action
    /// (<c>WebClient/Meta/ShellObservationPolicy.cs</c>), because the server cannot tell a re-render from a
    /// real repeat visit.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerObserveScreenViewed)]
    public class PlayerObserveScreenViewed : PlayerActionCore<PlayerModel>
    {
        public ShellScreen Screen { get; private set; }

        public PlayerObserveScreenViewed() { }

        public PlayerObserveScreenViewed(ShellScreen screen)
        {
            Screen = screen;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (Screen == ShellScreen.Unknown || !Enum.IsDefined(typeof(ShellScreen), Screen))
                return ActionResults.UnknownObservation;

            if (commit)
                player.EventStream.Event(new PlayerEventScreenViewed(Screen));

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Reports that the player selected a promoted entry, and emits <see cref="PlayerEventPromotedEntrySelected"/>.
    /// Like <see cref="PlayerObserveScreenViewed"/>, it changes no model state.
    /// </summary>
    [ModelAction(ActionCodes.PlayerObservePromotedEntrySelected)]
    public class PlayerObservePromotedEntrySelected : PlayerActionCore<PlayerModel>
    {
        public PromotedEntryPlacement Placement   { get; private set; }
        public ShellScreen            From        { get; private set; }
        public ShellScreen            Destination { get; private set; }

        public PlayerObservePromotedEntrySelected() { }

        public PlayerObservePromotedEntrySelected(PromotedEntryPlacement placement, ShellScreen from, ShellScreen destination)
        {
            Placement   = placement;
            From        = from;
            Destination = destination;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            bool allValuesKnown = Placement != PromotedEntryPlacement.Unknown &&
                           Enum.IsDefined(typeof(PromotedEntryPlacement), Placement) &&
                           From != ShellScreen.Unknown && Enum.IsDefined(typeof(ShellScreen), From) &&
                           Destination != ShellScreen.Unknown && Enum.IsDefined(typeof(ShellScreen), Destination);

            if (!allValuesKnown)
                return ActionResults.UnknownObservation;

            if (commit)
                player.EventStream.Event(new PlayerEventPromotedEntrySelected(Placement, From, Destination));

            return MetaActionResult.Success;
        }
    }
}
