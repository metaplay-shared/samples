/**
 * The newcomer shield's state, as an operator needs to read it.
 *
 * The shield covers an account's first `Global.NewcomerShieldMatches` ranked matches, and a per-account waiver
 * lifts it early. Once the count has passed the threshold the waiver decides nothing at all, which is the one
 * judgement this module makes and the reason it is a function rather than two ternaries in a template.
 *
 * The authoritative rule lives in `SharedCode/` — the tier is decided at formation from both seats' counts and
 * both seats' waivers (`Docs/matchmaking.md`, "The queue service"). Nothing here is used to decide
 * anything; it only names what the model already says.
 */
export type ShieldState = 'shielded' | 'waived' | 'playedOut'

/**
 * Which of the three states the account is in.
 * @param rankedMatchesPlayed The account's lifetime ranked matches, `PlayerRecord.RankedMatchesPlayed`.
 * @param shieldMatches How many ranked matches the shield covers, `Global.NewcomerShieldMatches`.
 * @param waived Whether this account's own waiver is set, `PlayerModel.NewcomerShieldWaived`.
 * @returns `playedOut` once the count has reached the threshold — that beats `waived`, because at that point
 * the waiver changes nothing and a card reading "Waived" would imply a lever that is no longer connected.
 */
export function shieldState(rankedMatchesPlayed: number, shieldMatches: number, waived: boolean): ShieldState {
  if (rankedMatchesPlayed >= shieldMatches) return 'playedOut'
  if (waived) return 'waived'
  return 'shielded'
}

/**
 * The label for a shield state, as it appears on the card's badge.
 * @param state The state to label.
 * @returns The operator-facing label.
 */
export function shieldStateLabel(state: ShieldState): string {
  if (state === 'playedOut') return 'Played out'
  if (state === 'waived') return 'Waived'
  return 'Shielded'
}
