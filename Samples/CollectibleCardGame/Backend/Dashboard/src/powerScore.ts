/**
 * A deck's Power Score, ported to TypeScript.
 *
 * **`SharedCode/Decks/DeckValidator.cs`'s `ComputePowerScore` is the authoritative definition** — matchmaking
 * bands on this number and the stakes tier is set by the gap between two of them, so the game has exactly one
 * of them and it is the C# one. This is a read-only re-derivation for an operator's screen, and it is the only
 * shared rule this dashboard re-implements. Keep the two in step: if the C# definition changes, so does this,
 * and `tests/unit/powerScore.test.ts` is where the shape is pinned.
 *
 * The rule: the sum of the deck's cards' ranks, read from **this account's own collection**. A card the
 * collection does not hold contributes nothing, so a partially built or stale list scores what it actually
 * has rather than throwing.
 */

/**
 * The Power Score of `cards` against `collection`.
 * @param cards The deck's card ids, in any order. A null or missing list scores zero.
 * @param collection The account's `PlayerModel.Collection`, a card id to rank map as the admin API serializes
 * it. A card absent from it contributes nothing.
 * @returns The sum of the ranks the account holds the deck's cards at.
 */
export function computePowerScore(
  cards: readonly string[] | undefined | null,
  collection: Record<string, number> | undefined | null
): number {
  if (!cards || !collection) return 0

  let total = 0
  for (const cardId of cards) {
    const rank = collection[cardId]
    if (typeof rank === 'number') total += rank
  }

  return total
}
