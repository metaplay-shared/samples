import { test, expect, clickMButton } from '@metaplay/playwright-config'

test('Unlocks a producer', async ({ page, freshTestPlayer }) => {
  // Navigate to the player page.
  await page.goto(`/players/${freshTestPlayer}`)

  // Open elixir unlocking modal.
  await page.getByTestId('unlock-button-elixir').click()

  // Ok the modal.
  await clickMButton(page.getByTestId('unlock-producer-ok-button'))

  // Check that the button is disabled, meaning the producer is unlocked.
  await expect(page.getByTestId('unlock-button-elixir')).toBeDisabled()
})
