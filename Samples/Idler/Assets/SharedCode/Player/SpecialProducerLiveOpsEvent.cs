// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic.TypeCodes;
using Metaplay.Core;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Collections.Generic;
using System.Linq;
using Metaplay.Core.Config;

namespace Game.Logic
{
    [MetaSerializableDerived(3)]
    public class SpecialProducerEvent : LiveOpsEventContent
    {
        [MetaMember(1)]             public string                 DisplayName         { get; private set; }
        [MetaMember(2)]             public ProducerTypeId         ProducerId          { get; private set; }
        [MetaMember(3)]             public int                    ProducerTargetLevel { get; private set; }
        [MetaMember(4)]             public List<PlayerReward>     Rewards             { get; private set; }

        public static SpecialProducerEvent FromTemplate(SpecialProducerEventInfo template)
        {
            return new SpecialProducerEvent()
            {
                DisplayName = template.DisplayName,
                ProducerId = (ProducerTypeId)template.Producer.KeyObject,
                ProducerTargetLevel = template.ProducerTargetLevel,
                Rewards = template.Rewards.ToList(), // take a copy of the list for safety
            };
        }

        public override bool ShouldWarnAboutOverlapWith(LiveOpsEventContent otherContent)
        {
            return otherContent is SpecialProducerEvent otherProducerEvent
                && otherProducerEvent.ProducerId == ProducerId;
        }

        public override void Validate(ILiveOpsEventValidationLog log, FullGameConfig activeGameConfig)
        {
            SharedGameConfig sharedConfig = (SharedGameConfig)activeGameConfig.SharedConfig;

            if (!sharedConfig.Producers.TryGetValue(ProducerId, out ProducerInfo producer))
                log.Error($"Producer {ProducerId} doesn't exist in game config", nameof(ProducerId));
            else if (producer.Category != ProducerCategory.Event)
                log.Error($"Producer {ProducerId} is not an event producer", nameof(ProducerId));
        }
    }

    [MetaSerializableDerived(3)]
    public class SpecialProducerLiveOpsEventModel : PlayerLiveOpsEventModel<SpecialProducerEvent, PlayerModel>
    {
        [MetaMember(1)] public bool PreviewPopupSeen { get; private set; } = false;
        [MetaMember(2)] public bool EventStartPopupSeen { get; private set; } = false;
        [MetaMember(3)] public bool RewardsClaimed { get; private set; } = false;
        [MetaMember(4)] public int LevelReached { get; private set; } = -1;

        public override bool AllowRemove => RewardsClaimed;

        protected override void OnPhaseChanged(PlayerModel player, LiveOpsEventPhase oldPhase, LiveOpsEventPhase[] fastForwardedPhases, LiveOpsEventPhase newPhase)
        {
            // For logic purposes we are only interested in the active/non-active transitions
            bool wasActive = oldPhase.IsActivePhase();
            bool isActive = newPhase.IsActivePhase();

            if (!wasActive && isActive)
            {
                // Activating the event unlocks the special producer.
                if (!player.Producers.ContainsKey(Content.ProducerId))
                    player.UnlockProducer(Content.ProducerId, new ProducerSourceLiveOpsEvent(Id));
            }
            else if (wasActive && !isActive)
            {
                if (!player.Producers.TryGetValue(Content.ProducerId, out ProducerModel producer))
                {
                    player.Log.Info("Tried to finalize {EventId}, but player does not have producer {ProducerId}", Id, Content.ProducerId);
                    return;
                }

                if (!(producer.StartedBy is ProducerSourceLiveOpsEvent eventSource && eventSource.EventId == Id))
                {
                    player.Log.Info("Tried to finalize {EventId}, but {ProducerId} was not started by this event", Id, Content.ProducerId);
                    return;
                }

                // Store the result, and remove the producer. If start popup was never seen then player did not participate.
                if (EventStartPopupSeen)
                    LevelReached = producer.Level;
                player.Producers.Remove(Content.ProducerId);
            }
        }

        protected override void OnLatestUpdateAcknowledged(PlayerModel player)
        {
            // Acknowledge popups, to prevent them from being shown again
            if (!PreviewPopupSeen && Phase == LiveOpsEventPhase.Preview)
                PreviewPopupSeen = true;
            if (!EventStartPopupSeen && Phase.IsActivePhase())
                EventStartPopupSeen = true;

            // Claim rewards and allow event to be deleted
            if (!RewardsClaimed && Phase.IsEndedPhase())
            {
                player.Log.Info("Claiming result for event {EventId}, target level reached: {LevelReached}", Id, LevelReached);

                if (LevelReached >= Content.ProducerTargetLevel)
                {
                    foreach (PlayerReward reward in Content.Rewards)
                        reward.Consume(player, source: null);
                }
                RewardsClaimed = true;
            }
        }
    }
}
