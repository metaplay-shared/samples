// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import { test, expect, clickMButton } from '@metaplay/playwright-config'

test('Unlocks a producer', async ({ page, freshTestPlayer }) => {
  // Navigate to the player page.
  await page.goto(`/players/${freshTestPlayer}`)

  // Open elixir unlocking modal.
  await page.getByTestId('unlock-button-elixir').click()

  // Ok the modal.
  await clickMButton(page.getByTestId('unlock-producer-ok-button'))

  // Check that the button is not visible anymore, meaning the producer is unlocked.
  await expect(page.getByTestId('unlock-button-elixir')).not.toBeVisible()
})
