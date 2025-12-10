// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic.TypeCodes;
using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Game.Logic
{
    /// <summary>
    /// State for a single special producer event for a player.
    ///
    /// Unlike the simpler <see cref="HappyHourModel"/> type of event,
    /// a <c>SpecialProducerEventModel</c> extends <see cref="MetaActivableState"/>
    /// in a non-trivial manner. It contains state for storing the
    /// latest result of the event for the player (<see cref="LastResult"/>),
    /// and implements hooks for <see cref="MetaActivableState"/>
    /// to manage that state:
    /// <see cref="OnStartedActivation"/> unlocks the special producer
    /// for the player when the event is activated, and
    /// <see cref="Finalize"/> removes the producer from the player
    /// when the event ends. These methods also update the <see cref="LastResult"/> state.
    ///
    /// Both methods are overrides of virtual methods that are called from
    /// <see cref="MetaActivableState"/> when the activable is activated
    /// and finalized, respectively. The top-level controlling of the activation
    /// and finalization of activables is done by game-specific code.
    /// For Idler's special producer events and happy hour events, this
    /// is done in <see cref="PlayerModel.TickEvents"/>.
    /// </summary>
    [MetaSerializableDerived(3)]
    public class SpecialProducerEventModel : MetaActivableState<SpecialProducerEventId, SpecialProducerEventInfo>
    {
        /// <summary>
        /// Result from a finished event.
        /// Reward (if any) is claimed by player from the game UI.
        /// </summary>
        [MetaSerializable]
        public class EventResult
        {
            [MetaMember(1)] public int                  LevelReached    { get; private set; } = 0;
            [MetaMember(2)] public List<PlayerReward>   Rewards         { get; private set; }
            [MetaMember(3)] public bool                 ClaimPending    { get; set; }

            EventResult(){ }
            public EventResult(int levelReached, List<PlayerReward> rewards, bool claimPending)
            {
                LevelReached = levelReached;
                Rewards = new List<PlayerReward>(rewards); // copy the list for safety
                ClaimPending = claimPending;
            }
        }

        [MetaMember(1)] public sealed override SpecialProducerEventId   ActivableId { get; protected set; }
        [MetaMember(2)] public EventResult                              LastResult  { get; private set; } = null;

        /// <summary> Just a shorthand/convenience property. </summary>
        [IgnoreDataMember] public SpecialProducerEventInfo Info => ActivableInfo;

        SpecialProducerEventModel(){ }
        public SpecialProducerEventModel(SpecialProducerEventInfo info)
            : base(info)
        {
        }

        protected override bool CustomCanStartActivation(IPlayerModelBase playerBase, MetaTime time)
        {
            // Previous reward (if any) must be claimed before starting new event.
            return LastResult == null || !LastResult.ClaimPending;
        }

        protected override void OnStartedActivation(IPlayerModelBase playerBase)
        {
            PlayerModel player = (PlayerModel)playerBase;

            // Activating the event unlocks the special producer.
            player.UnlockProducer(Info.Producer.Ref.Id);

            // Forget last result
            LastResult = null;
        }

        protected override void Finalize(IPlayerModelBase playerBase)
        {
            PlayerModel player = (PlayerModel)playerBase;

            if (!player.Producers.TryGetValue(Info.Producer.Ref.Id, out ProducerModel producer))
            {
                player.Log.Warning("Tried to finalize {EventId}, but player does not have producer {ProducerId}", Info.EventId, Info.Producer.Ref.Id);
                return;
            }

            // Store the result, and remove the producer.

            bool                reachedTarget   = producer.Level >= Info.ProducerTargetLevel;
            List<PlayerReward>  rewards         = reachedTarget ? Info.Rewards : new List<PlayerReward>();
            LastResult = new EventResult(producer.Level, rewards, claimPending: reachedTarget);

            player.Producers.Remove(Info.Producer.Ref.Id);
        }
    }

    /// <summary>
    /// A player's state concerning all special producer events.
    ///
    /// As a minor extension to <see cref="MetaActivableSet{TId, TInfo, TActivableState}"/>,
    /// this implements a custom <see cref="CustomCanStartActivation"/> check in order
    /// to ensure that multiple producers of the same type aren't added to the player.
    /// </summary>
    [MetaSerializableDerived(3)]
    [MetaActivableSet("SpecialProducerEvent")]
    public class PlayerSpecialProducerEventsModel : MetaActivableSet<SpecialProducerEventId, SpecialProducerEventInfo, SpecialProducerEventModel>
    {
        protected override SpecialProducerEventModel CreateActivableState(SpecialProducerEventInfo info, IPlayerModelBase player)
        {
            return new SpecialProducerEventModel(info);
        }

        protected override bool CustomCanStartActivation(SpecialProducerEventInfo info, IPlayerModelBase playerBase)
        {
            PlayerModel player = (PlayerModel)playerBase;

            // Make sure to not activate the event if the player already has the target producer.
            return !player.Producers.ContainsKey(info.Producer.Ref.Id);
        }
    }

    /// <summary>
    /// An action to claim the rewards from a special producer event.
    /// This is invoked from the client's UI code when the player clicks
    /// a claim button.
    /// </summary>
    [ModelAction(ActionCodes.PlayerClaimSpecialProducerEventRewards)]
    public class PlayerClaimSpecialProducerEventRewards : PlayerAction
    {
        public SpecialProducerEventInfo EventInfo { get; private set; }

        PlayerClaimSpecialProducerEventRewards(){ }
        public PlayerClaimSpecialProducerEventRewards(SpecialProducerEventInfo eventInfo)
        {
            EventInfo = eventInfo;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            // Get the player's state for the specified event, and validate that
            // it is in a state where it can be legitimately claimed.
            SpecialProducerEventModel eventState = player.SpecialProducerEvents.TryGetState(EventInfo);
            if (eventState == null)
                return ActionResult.NoEventState;
            if (eventState.LastResult == null)
                return ActionResult.NoEventResult;
            if (!eventState.LastResult.ClaimPending)
                return ActionResult.NoEventClaimPending;

            if (commit)
            {
                // Claim: mark the result as claimed, and grant the rewards to the player.
                player.Log.Info("Claiming result for event {EventId}", EventInfo.EventId);
                eventState.LastResult.ClaimPending = false;
                foreach (PlayerReward reward in eventState.LastResult.Rewards)
                    reward.Consume(player, source: null);
            }

            return MetaActionResult.Success;
        }
    }
}
