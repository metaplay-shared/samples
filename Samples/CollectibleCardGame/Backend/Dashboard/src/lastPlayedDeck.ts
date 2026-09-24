/**
 * What `PlayerModel.LastPlayedDeck` should read as on the decks card.
 *
 * `DeckChoice` (`SharedCode/Decks/DeckChoice.cs`) has exactly one of its two halves set, and the whole member
 * is null on an account that has never played a match. **Both halves have to be handled or the row shows
 * nothing for most accounts**: a fresh account's first match is a starter deck.
 *
 * It is also a remembered choice rather than a guarantee. A saved deck may have been deleted since, and a
 * starter deck may have been dropped from the config — the model's own doc comment says every reader falls
 * back rather than trusting the id. So an unresolvable choice falls back to the `DeckChoice.ToKey()`-shaped
 * string (`saved:3`, `starter:FireAndFoam`), which is a normal state and not an error.
 *
 * A pure module so a unit test can reach every branch: the starter-deck one is otherwise only exercised by an
 * account that played a starter deck, which no dashboard fixture can arrange.
 */

/** `DeckChoice` as the admin API serializes it. The computed `isSaved` / `isStarter` come over the wire too. */
export interface DeckChoiceJson {
  savedDeckId?: number
  starterDeckId?: string | null
  isSaved?: boolean
  isStarter?: boolean
}

/**
 * The label for a last-played deck choice.
 * @param choice The account's `LastPlayedDeck`, or null/undefined when it has never played.
 * @param savedDeckNames The account's saved decks, deck id as a string to deck name.
 * @param starterDeckNames The `StarterDecks` config library's display names, starter deck id to name.
 * @returns The text the card's "Last played" row shows.
 */
export function lastPlayedDeckLabel(
  choice: DeckChoiceJson | null | undefined,
  savedDeckNames: Record<string, string>,
  starterDeckNames: Record<string, string>
): string {
  if (!choice) return 'Never played'

  // Each branch excludes the other flag, so a row with **both** set — the case the fallback at the end claims
  // to cover — falls through to it rather than being given a confident label from whichever branch is tested
  // first. This is the one row an operator is reading precisely because something is off.
  if (choice.isStarter === true && choice.isSaved !== true) {
    const starterDeckId = String(choice.starterDeckId)
    const displayName = starterDeckNames[starterDeckId]
    return displayName === undefined ? `starter:${starterDeckId}` : `${displayName} (starter)`
  }

  if (choice.isSaved === true && choice.isStarter !== true) {
    const savedDeckId = String(choice.savedDeckId)
    return savedDeckNames[savedDeckId] ?? `saved:${savedDeckId}`
  }

  // Neither half set, or both. No picker produces one, but a persisted row can carry anything and the shared
  // code checks it at the consumers rather than in a factory — so the card says so instead of rendering blank.
  return 'Malformed deck choice'
}
