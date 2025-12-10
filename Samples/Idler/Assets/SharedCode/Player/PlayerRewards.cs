// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Rewards;

namespace Game.Logic
{
    public abstract class PlayerReward : MetaPlayerReward<PlayerModel>
    {
    }

    /// <summary>
    /// A reward with gems that gets added to the player.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class RewardGems : PlayerReward
    {
        [MetaMember(1)] public int Amount { get; private set; } = 10;

        public RewardGems() { }
        public RewardGems(int amount) { Amount = amount; }

        public override void Consume(PlayerModel playerModel, IRewardSource source)
        {
            playerModel.Wallet.NumGems += Amount;
        }
    }

    /// <summary>
    /// A reward with gold that gets added to the player.
    /// </summary>
    [MetaSerializableDerived(2)]
    public class RewardGold : PlayerReward
    {
        [MetaMember(1)] public int Amount { get; private set; } = 1000;

        public RewardGold() { }
        public RewardGold(int amount) { Amount = amount; }

        public override void Consume(PlayerModel playerModel, IRewardSource source)
        {
            playerModel.Wallet.NumGold += Amount;
        }
    }

    /// <summary>
    /// A reward with producers that unlocks and/or level up the player's producers.
    /// </summary>
    [MetaSerializableDerived(3)]
    public class RewardProducer : PlayerReward
    {
        [MetaMember(1)] public ProducerTypeId   ProducerId  { get; private set; }
        [MetaMember(2)] public int              Amount      { get; private set; } = 1;

        public RewardProducer() { }
        public RewardProducer(ProducerTypeId producerId, int amount)
        {
            ProducerId = producerId;
            Amount = amount;
        }

        public override void Consume(PlayerModel playerModel, IRewardSource source)
        {
            for (int ndx = 0; ndx < Amount; ndx++)
            {
                // Unlock new one or level up existing one
                if (playerModel.Producers.TryGetValue(ProducerId, out ProducerModel producer))
                    producer.Level += 1;
                else
                    playerModel.UnlockProducer(ProducerId);
            }
        }
    }

    /*
    [MetaSerializableDerived(4)]
    public class RewardComplexExample : PlayerReward
    {
        [MetaMember(1)] public ProducerTypeId   ProducerId  { get; private set; }
        [MetaMember(2)] public int              Level       { get; private set; }
        [MetaMember(3)] public List<string>     Tags        { get; private set; } // makes no sense, just an example

        public override void Consume(PlayerModel playerModel, IRewardSource source)
        {
            throw new System.NotImplementedException();
        }
    }
    */
}
