using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// Put a card in a lock slot, or empty the slot. Locking, unlocking and swapping are all this one action:
    /// the commit step vacates whatever other slot the card already occupies, so "move this lock" and "lock a
    /// different card here" are the same call and there is no two-step dance for the UI to get out of sync.
    /// <para>
    /// A locked card can neither lose a rank nor gain one, and a card below <c>Global.MinLockRank</c> cannot be
    /// locked at all: at the rank floor there is no growth to freeze and the floor already protects the card,
    /// so the lock would buy nothing but denial of a winner's Heist pick (<c>Docs/game-design.md</c>, "Locks").
    /// </para>
    /// <para>
    /// Nothing here is tied to matchmaking state: a match's stakes are fixed from a snapshot taken at enqueue,
    /// so a lock changed mid-search cannot move a wager that has already been agreed, and there is nothing for
    /// this action to protect against by refusing to run.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerSetLockSlot)]
    public class PlayerSetLockSlot : PlayerAction
    {
        public int    SlotIndex { get; private set; }
        /// <summary> The card to lock, or null to empty the slot. </summary>
        public CardId CardId    { get; private set; }

        public PlayerSetLockSlot() { }

        public PlayerSetLockSlot(int slotIndex, CardId cardId)
        {
            SlotIndex = slotIndex;
            CardId    = cardId;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (SlotIndex < 0 || SlotIndex >= player.LockSlotCount)
                return ActionResults.InvalidLockSlot;

            // Clearing a slot asks nothing about a card, by construction: there is none to weigh, so a lock
            // set before the threshold moved can always be undone.
            if (CardId != null)
            {
                // A card the player does not own cannot be locked. Where it currently sits is deliberately not
                // checked: the commit step moves it, so the UI can offer "lock this card" as one press.
                if (!player.Collection.TryGetValue(CardId, out int rank))
                    return ActionResults.CardNotOwned;

                // The rank threshold, asked of the rank this account actually holds.
                if (rank < player.GameConfig.Global.MinLockRank)
                    return ActionResults.CardRankTooLowToLock;
            }

            if (commit)
            {
                if (CardId != null)
                {
                    int occupiedSlot = -1;
                    foreach ((int slot, CardId locked) in player.LockSlots)
                    {
                        if (slot != SlotIndex && locked == CardId)
                        {
                            occupiedSlot = slot;
                            break;
                        }
                    }

                    if (occupiedSlot >= 0)
                        player.LockSlots.Remove(occupiedSlot);

                    player.LockSlots[SlotIndex] = CardId;
                }
                else
                {
                    // Clearing an already-empty slot is not an error, so that a double press cannot fail.
                    player.LockSlots.Remove(SlotIndex);
                }

                player.ClientListener.Collection?.GenericPropertyChanged(nameof(PlayerModel.LockSlots));
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Set the rank of a card this account owns.
    /// <para>
    /// <b>Development-only</b>, and it exists because nothing else in the game can raise a rank yet: ranks
    /// move through the Heist and practice stakes move nothing, so a fresh account is all rank 1 for
    /// the whole of the current build. Every rule that turns on rank — <c>Global.MinLockRank</c> today, the
    /// rank tracks and the Heist payouts later — would otherwise be unreachable from the client and untestable
    /// end to end. The affordance that sends it is query-string gated, like the board's own test knobs.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerDevSetCardRank)]
    [DevelopmentOnlyAction]
    public class PlayerDevSetCardRank : PlayerAction
    {
        public CardId CardId { get; private set; }
        public int    Rank   { get; private set; }

        public PlayerDevSetCardRank() { }

        public PlayerDevSetCardRank(CardId cardId, int rank)
        {
            CardId = cardId;
            Rank   = rank;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (CardId == null || !player.Collection.ContainsKey(CardId))
                return ActionResults.CardNotOwned;

            GlobalConfig global = player.GameConfig.Global;
            if (Rank < global.RankMin || Rank > global.RankMax)
                return ActionResults.InvalidCardRank;

            // A locked card can neither lose a rank nor gain one — the invariant this file's own action
            // exists to serve. A cheat that walked over it would be the reference the Heist is written
            // against, so it refuses instead: unlock the card first. The same question the Heist's transfer
            // asks, through the same reader.
            if (player.IsLocked(CardId))
                return ActionResults.CardIsLocked;

            if (commit)
            {
                player.Collection[CardId] = Rank;
                player.ClientListener.Collection?.GenericPropertyChanged(nameof(PlayerModel.Collection));
            }

            return MetaActionResult.Success;
        }
    }
}
