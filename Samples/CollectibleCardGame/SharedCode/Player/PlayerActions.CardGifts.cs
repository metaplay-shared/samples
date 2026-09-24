using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary> One free, uniformly selected unowned collectible after each configured number of matches. </summary>
    public static class CardGiftPolicy
    {
        public static List<CardId> Candidates(PlayerModel player)
        {
            List<CardId> cards = new List<CardId>();
            foreach ((CardId id, CardInfo card) in player.GameConfig.Cards)
                if (card.Collectible && !player.Collection.ContainsKey(id))
                    cards.Add(id);
            cards.Sort((a, b) => StringComparer.Ordinal.Compare(a.Value, b.Value));
            return cards;
        }

        public static bool IsReady(PlayerModel player)
            => player.GameConfig.Global.MatchesPerCardGift > 0
               && player.CardGiftProgress >= player.GameConfig.Global.MatchesPerCardGift
               && player.UnseenCardGift == null && Candidates(player).Count > 0;
    }

    /// <summary>
    /// The metagame claims without choosing a card. Public state and a stable seed make prediction and replay
    /// identical; the ordinal advances atomically with the grant, so retries cannot reroll or duplicate it.
    /// </summary>
    [ModelAction(ActionCodes.PlayerClaimCardGift)]
    public class PlayerClaimCardGift : PlayerAction
    {
        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (!CardGiftPolicy.IsReady(player))
                return ActionResults.CardGiftNotReady;
            if (commit)
            {
                List<CardId> candidates = CardGiftPolicy.Candidates(player);
                RandomPCG random = RandomPCG.CreateFromSeed(player.PlayerId.Value ^ ((ulong)player.CardGiftsClaimed << 32) ^ 0x4341524447494654UL);
                CardId card = candidates[random.NextInt(candidates.Count)];
                player.Collection[card] = player.GameConfig.Global.RankMin;
                player.CardGiftProgress -= player.GameConfig.Global.MatchesPerCardGift;
                player.CardGiftsClaimed++;
                player.UnseenCardGift = card;
                player.ClientListener.Collection?.GenericPropertyChanged(nameof(PlayerModel.Collection));
            }
            return MetaActionResult.Success;
        }
    }

    [ModelAction(ActionCodes.PlayerDismissCardGift)]
    public class PlayerDismissCardGift : PlayerAction
    {
        public CardId Card { get; private set; }
        public PlayerDismissCardGift() { }
        public PlayerDismissCardGift(CardId card) { Card = card; }
        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (Card == null || player.UnseenCardGift != Card)
                return ActionResults.CardGiftNotReady;
            if (commit)
            {
                player.UnseenCardGift = null;
                player.ClientListener.Collection?.GenericPropertyChanged(nameof(PlayerModel.UnseenCardGift));
            }
            return MetaActionResult.Success;
        }
    }
}
