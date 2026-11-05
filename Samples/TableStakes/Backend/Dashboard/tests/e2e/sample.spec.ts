// Please note to import from '@metaplay/playwright-config' instead of '@playwright/test'
import { test, expect } from '@metaplay/playwright-config'

test('trivial example', async ({ page, freshTestPlayer }) => {
  await page.goto(`/players/${freshTestPlayer}`)
  await expect(page.getByTestId('player-overview-card')).toBeVisible()
})
