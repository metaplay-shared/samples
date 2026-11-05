import { expect, test } from '@metaplay/playwright-config'

/**
 * The collection and decks cards, against a fresh account. `PlayerModel.GameInitializeNewPlayerModel` grants
 * the starter collection at `Global.RankMin`, no saved decks and no remembered deck — so this pins the
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

    // The starter grant is the cards flagged InStarterCollection, a subset of the pool, and nothing is above the
    // floor: the Heist is the only thing that moves a rank after creation.
    const badge = card.getByTestId('collection-summary-badge')
    await expect(badge).toHaveText(/^\d+\/\d+ owned · 0 above rank 1$/)
    const counts = /^(\d+)\/(\d+)/.exec(await badge.innerText())
    const owned = Number(counts?.[1])
    const total = Number(counts?.[2])
    expect(owned).toBeGreaterThan(0)
    expect(owned).toBeLessThanOrEqual(total)
    await expect(card.getByTestId('collection-config-unavailable')).toBeHidden()

    const rows = card.locator('[data-testid^="collection-row-"]')
    await expect(rows.first()).toBeVisible()
    expect(await rows.count()).toBeGreaterThan(0)

    // One row per collectible card: the owned ones at the floor, the rest marked as not owned.
    const ranks = await card.locator('[data-testid^="collection-rank-"]').allInnerTexts()
    expect(ranks.length).toEqual(await rows.count())
    expect(ranks.length).toEqual(total)
    expect(ranks.filter((rank) => /^Rank 1 \/ \d+$/.test(rank)).length).toEqual(owned)
    expect(ranks.filter((rank) => rank === '—').length).toEqual(total - owned)
  })
})

test.describe('Decks card', () => {
  test('says the account has never played and has no saved decks', async ({ page, freshTestPlayer }) => {
    await page.goto(`/players/${freshTestPlayer}`)

    const card = page.getByTestId('sticky-paws-decks-card')
    await expect(card).toBeVisible()
    await expect(card.getByTestId('decks-summary-badge')).toHaveText('0 saved')

    // The one `Global.DeckSize` read a fresh account renders; a casing mismatch on GlobalConfig's fields
    // would show a plausible card.
    await expect(card).toContainText('not 25')
    await expect(card.getByTestId('decks-config-unavailable')).toBeHidden()

    // `LastPlayedDeck` is null on a fresh account — the branch that would otherwise never be exercised.
    await expect(card.getByTestId('decks-last-played-value')).toHaveText('Never played')
    await expect(card.getByTestId('decks-empty-callout')).toBeVisible()
    await expect(card.locator('[data-testid^="deck-row-"]')).toHaveCount(0)
  })
})
