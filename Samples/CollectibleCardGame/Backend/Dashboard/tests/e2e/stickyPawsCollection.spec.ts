import { expect, test } from '@metaplay/playwright-config'

/**
 * The collection and decks cards, against a fresh account. `PlayerModel.GameInitializeNewPlayerModel` grants
 * the whole starter collection at `Global.RankMin`, no saved decks and no remembered deck — so this pins the
 * empty and floor-rank branches, which are the ones a fixture can reach without playing a match.
 *
 * The branches that need a played account — a rank above the floor, a starter deck as the last played one —
 * are covered by `tests/unit/powerScore.test.ts` and `tests/unit/lastPlayedDeck.test.ts` instead, because no
 * dashboard fixture can arrange either state.
 */
test.describe('Collection card', () => {
  test('lists the starter grant, every card at the rank floor', async ({ page, freshTestPlayer }) => {
    await page.goto(`/players/${freshTestPlayer}`)

    const card = page.getByTestId('sticky-paws-collection-card')
    await expect(card).toBeVisible()

    // The starter grant is every collectible card, so owned equals collectible and nothing is above the
    // floor: the Heist is the only thing that moves a rank after creation.
    await expect(card.getByTestId('collection-summary-badge')).toHaveText(/^(\d+)\/\1 owned · 0 above rank 1$/)
    await expect(card.getByTestId('collection-config-unavailable')).toBeHidden()

    const rows = card.locator('[data-testid^="collection-row-"]')
    await expect(rows.first()).toBeVisible()
    expect(await rows.count()).toBeGreaterThan(0)

    // Every row a fresh account shows is owned and at the floor, so no row reads as unowned.
    const ranks = await card.locator('[data-testid^="collection-rank-"]').allInnerTexts()
    expect(ranks.length).toEqual(await rows.count())
    for (const rank of ranks) expect(rank).toMatch(/^Rank 1 \/ \d+$/)
  })
})

test.describe('Decks card', () => {
  test('says the account has never played and has no saved decks', async ({ page, freshTestPlayer }) => {
    await page.goto(`/players/${freshTestPlayer}`)

    const card = page.getByTestId('sticky-paws-decks-card')
    await expect(card).toBeVisible()
    await expect(card.getByTestId('decks-summary-badge')).toHaveText('0 saved')

    // The one read of `Global.DeckSize` a fresh account renders. It is here because the card no longer falls
    // back to a number: a casing regression on GlobalConfig's fields — the wave's own headline spec error,
    // and the likelier direction of drift since CardInfo and PlayerRecord are already properties — would
    // otherwise show a plausible card and pass every other test.
    await expect(card).toContainText('not 25')
    await expect(card.getByTestId('decks-config-unavailable')).toBeHidden()

    // `LastPlayedDeck` is null on a fresh account — the branch that would otherwise never be exercised.
    await expect(card.getByTestId('decks-last-played-value')).toHaveText('Never played')
    await expect(card.getByTestId('decks-empty-callout')).toBeVisible()
    await expect(card.locator('[data-testid^="deck-row-"]')).toHaveCount(0)
  })
})
