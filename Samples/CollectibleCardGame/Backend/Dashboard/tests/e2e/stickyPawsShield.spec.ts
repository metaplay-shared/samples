import { clickMButton, expect, test } from '@metaplay/playwright-config'

/**
 * The newcomer shield card, against the account state `PlayerModel.GameInitializeNewPlayerModel` guarantees:
 * the starter collection at `Global.RankMin`, `Record.RankedMatchesPlayed` at zero, no waiver, no saved decks
 * and no remembered deck. Every assertion here is against that known start, so no match is needed.
 *
 * This suite runs against the **built** dashboard served by the game server on 5550 (+
 * `STICKYPAWS_PORT_OFFSET`), not against vite — vite is pinned to 5551 `strictPort` in a file the dashboard
 * sync tool owns. See `AGENTS.md`, "Tests".
 */
test.describe('Newcomer shield card', () => {
  test('renders the state a fresh account is in', async ({ page, freshTestPlayer }) => {
    await page.goto(`/players/${freshTestPlayer}`)

    const card = page.getByTestId('sticky-paws-shield-card')
    await expect(card).toBeVisible()
    await expect(card.getByTestId('shield-state-badge')).toHaveText('Shielded')
    await expect(card.getByTestId('shield-waiver')).toHaveText('Off')
    await expect(card.getByTestId('shield-current-match')).toHaveText('No')

    // The count against the configured threshold, and the sentence the SDK's generated modal cannot say.
    await expect(card.getByTestId('shield-ranked-matches')).toContainText('0 of ')
    await expect(card.getByTestId('shield-both-seats-callout')).toBeVisible()
  })

  test('waives and restores the shield, in both directions', async ({ page, freshTestPlayer, apiURL, request }) => {
    /** The waiver as the admin API reads it back off the model, which is the fact under test. */
    async function waivedOnTheModel(): Promise<boolean> {
      const response = await request.get(`${apiURL}/players/${freshTestPlayer}`)
      expect(response.status()).toEqual(200)
      const details = await response.json()
      return details.model.newcomerShieldWaived === true
    }

    await page.goto(`/players/${freshTestPlayer}`)
    const card = page.getByTestId('sticky-paws-shield-card')
    await expect(card.getByTestId('shield-state-badge')).toHaveText('Shielded')
    expect(await waivedOnTheModel()).toEqual(false)

    // Waive it. The button posts the opposite of the account's current value, so its label says which way.
    await clickMButton(card.getByTestId('shield-toggle-button'))
    await clickMButton(page.getByTestId('shield-toggle-modal-ok-button'))
    await expect(card.getByTestId('shield-state-badge')).toHaveText('Waived')
    await expect.poll(async () => await waivedOnTheModel()).toEqual(true)

    // The callout is about a shield that still decides something, so it goes when the waiver lands.
    await expect(card.getByTestId('shield-both-seats-callout')).toBeHidden()

    // And back. This is the direction the SDK's generated modal could not do before this wave: its form
    // opened on `false` for every account, so `false` was refused as "No changes to Save."
    await clickMButton(card.getByTestId('shield-toggle-button'))
    await clickMButton(page.getByTestId('shield-toggle-modal-ok-button'))
    await expect(card.getByTestId('shield-state-badge')).toHaveText('Shielded')
    await expect.poll(async () => await waivedOnTheModel()).toEqual(false)
  })
})
