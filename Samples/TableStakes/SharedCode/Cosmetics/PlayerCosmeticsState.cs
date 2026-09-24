using Metaplay.Core.Model;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// One player's cosmetics (<c>docs/cosmetics.md</c>): what they own, what they have equipped, and which
    /// granted items they have not seen yet.
    /// <para>
    /// Every ownership check reads this state. <c>PlayerTournamentState.EarnedCosmetics</c> is only a record of
    /// awards, because a placement claim also grants the item here. Only this class's internal methods write its
    /// state, so an ID appears in <see cref="Owned"/> at most once and only owned items are equipped.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class PlayerCosmeticsState
    {
        /// <summary>Everything this player owns, in the order it was acquired. Ownership is permanent.</summary>
        [MetaMember(1)] List<CosmeticId> _owned = new List<CosmeticId>();

        /// <summary>
        /// The item equipped in each slot, or null for an empty slot. One member per slot instead of a
        /// dictionary, because a dictionary keyed on the enum could deserialize a slot value that does not exist.
        /// </summary>
        [MetaMember(2)] CosmeticId _equippedAvatar;
        [MetaMember(3)] CosmeticId _equippedFrame;
        [MetaMember(4)] CosmeticId _equippedNameEffect;

        /// <summary>
        /// Items granted to the player that they have not seen yet. A non-empty list shows the Profile badge.
        /// Only grants add to this list. Purchases do not, because the player sees the item while buying it.
        /// </summary>
        [MetaMember(5)] List<CosmeticId> _unacknowledged = new List<CosmeticId>();

        static readonly List<CosmeticId> Nothing = new List<CosmeticId>();

        public PlayerCosmeticsState() { }

        public IReadOnlyList<CosmeticId> Owned          => _owned ?? Nothing;
        public IReadOnlyList<CosmeticId> Unacknowledged => _unacknowledged ?? Nothing;

        /// <summary>Whether this player owns <paramref name="id"/>.</summary>
        public bool Owns(CosmeticId id) => id != null && Owned.Contains(id);

        /// <summary>Whether any granted item is unseen. The Profile badge is shown exactly when this is true.</summary>
        public bool HasUnacknowledged => Unacknowledged.Count > 0;

        /// <summary>What is worn in <paramref name="slot"/>, or null.</summary>
        public CosmeticId EquippedIn(CosmeticKind slot) => slot switch
        {
            CosmeticKind.Avatar     => _equippedAvatar,
            CosmeticKind.Frame      => _equippedFrame,
            CosmeticKind.NameEffect => _equippedNameEffect,
            _                       => null,
        };

        /// <summary>Whether <paramref name="id"/> is the item currently worn in <paramref name="slot"/>.</summary>
        public bool IsEquipped(CosmeticKind slot, CosmeticId id) => id != null && Equals(EquippedIn(slot), id);

        /// <summary>
        /// Adds <paramref name="id"/> to the owned items. Returns false and changes nothing if it was already
        /// owned, for example when a second tournament season awards the same frame.
        /// </summary>
        internal bool Acquire(CosmeticId id)
        {
            if (id == null || Owns(id))
                return false;

            _owned ??= new List<CosmeticId>();
            _owned.Add(id);
            return true;
        }

        /// <summary>
        /// Adds <paramref name="id"/> to <see cref="Unacknowledged"/> so the Profile badge shows it. Called only
        /// for grants, never for purchases.
        /// </summary>
        internal void MarkUnacknowledged(CosmeticId id)
        {
            if (id == null)
                return;

            Lists.AddOnce(ref _unacknowledged, id);
        }

        /// <summary>
        /// Equips <paramref name="id"/> in <paramref name="slot"/> without validation. The caller must check that
        /// the player owns the item and that the slot matches the item's kind, as
        /// <see cref="PlayerModel.EquipCosmetic"/> does using the catalogue.
        /// </summary>
        internal void Equip(CosmeticKind slot, CosmeticId id)
        {
            switch (slot)
            {
                case CosmeticKind.Avatar:     _equippedAvatar     = id; break;
                case CosmeticKind.Frame:      _equippedFrame      = id; break;
                case CosmeticKind.NameEffect: _equippedNameEffect = id; break;
            }
        }

        /// <summary>Clears <see cref="Unacknowledged"/>. Changes nothing else (see <see cref="PlayerAcknowledgeCosmetics"/>).</summary>
        internal void Acknowledge()
        {
            _unacknowledged ??= new List<CosmeticId>();
            _unacknowledged.Clear();
        }

        public override string ToString() =>
            $"{Owned.Count} owned, wearing {_equippedFrame?.Value ?? "no frame"} and {_equippedNameEffect?.Value ?? "no name effect"}";
    }
}
