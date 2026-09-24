using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// What the five engine-flag keywords mean, as queries. The engine asks "does this critter have Guard" and
    /// never asks where it got it — printed, granted in play, or from the Weather aura are the same fact
    /// (<c>Docs/rules.md</c>).
    /// </summary>
    public static class KeywordRules
    {
        /// <summary>
        /// Whether the seat's Den is closed to attacks. The gate counts only Guards the attacker could legally
        /// have attacked instead: otherwise a critter with both Guard and Sneaky — which a Smoke Bomb
        /// aimed at your own Guard makes — would be an unanswerable lock on the enemy's only way forward.
        /// <para>
        /// The gate is binary. Two Guards are one gate and two bodies, Guard never redirects an attack
        /// onto itself, and a sleepy Guard gates like any other because Guard is a property of being in play.
        /// </para>
        /// </summary>
        public static bool GuardGate(SeatState defender)
        {
            IReadOnlyList<BoardCritter> board = defender.Board;
            for (int ndx = 0; ndx < board.Count; ndx++)
            {
                if (GatesTheDen(board[ndx].Keywords))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether one critter's keywords close its owner's Den. Stated over the flags rather than over a
        /// board critter because two callers ask it of something that is not one: the Guard scan above walks
        /// a board it is already holding, and the bot scorer asks it of a candidate's keywords before there
        /// is a critter. Two implementations of "does this gate" would disagree eventually
        /// (<c>Docs/hidden-information.md</c>, "The legality promise, stated honestly").
        /// </summary>
        public static bool GatesTheDen(KeywordFlags keywords)
            => (keywords & KeywordFlags.Guard) != 0 && (keywords & KeywordFlags.Sneaky) == 0;

        /// <summary>
        /// Whether the enemy may pick this critter out as a target at all. Sneaky blocks being <em>targeted</em>
        /// by the enemy; it does not protect against an untargeted sweep, and it never hides a critter
        /// from its own side.
        /// </summary>
        public static bool IsTargetableByEnemy(BoardCritter critter) => IsTargetableByEnemy(critter.Keywords);

        /// <inheritdoc cref="IsTargetableByEnemy(BoardCritter)"/>
        public static bool IsTargetableByEnemy(KeywordFlags keywords) => (keywords & KeywordFlags.Sneaky) == 0;

        /// <summary>
        /// Whether a damage instance of this size consumes a Bubble. A Bubble ignores the whole first damage
        /// instance rather than one point of it, and zero damage is not a damage instance.
        /// </summary>
        public static bool BubbleAbsorbs(BoardCritter critter, int amount) => amount > 0 && critter.BubbleWouldAbsorb;

        /// <summary>
        /// Whether dealing this much damage reveals a Sneaky critter. The keyword lasts until it deals damage,
        /// and a swing that a Bubble happened to absorb was still a swing the whole board saw.
        /// </summary>
        public static bool RevealsSneaky(BoardCritter dealer, int amount) => amount > 0 && dealer.HasSneaky;

        /// <summary>
        /// How much a critter's own damage heals its owner's Den. Snacktime applies to any damage the critter
        /// sources — its attack, its counterattack, or its own triggered effect — and a dying attacker's
        /// damage still landed, so it still heals.
        /// </summary>
        public static int SnacktimeHeal(BoardCritter dealer, int amount)
            => amount > 0 && dealer.HasSnacktime ? amount : 0;
    }
}
