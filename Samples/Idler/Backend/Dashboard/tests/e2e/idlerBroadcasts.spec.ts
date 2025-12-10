// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import { test, expect, clickMButton } from '@metaplay/playwright-config'

test.describe('Idler Specific Broadcasts', () => {
  test.beforeEach('Navigate to broadcasts list page', async ({ page }) => {
    await page.goto(`/broadcasts`)
  })

  test('Creates a new broadcast, checks contents and deletes it', async ({ page, testToken }) => {
    await test.step('Create a new broadcast', async () => {
      await page.getByTestId('create-new-broadcast-button').click()

      // Uncheck Finnish localization.
      await page.getByTestId('localizations-selection-input-checkbox-Finnish').uncheck()

      // Fill in mandatory fields.
      await page.getByTestId('broadcast-name').fill(`Test Broadcast ${testToken}`)
      await page.getByTestId('contents-title-localizations-en-input').fill(`Test Mail ${testToken}`)

      // Enable targeting.
      await page.getByTestId('enable-targeting-switch-control').click()

      // Select two segments.
      await page.getByTestId('add-segment-button').click()

      await page.getByTestId('segment-selector-0').click()
      await page.getByTestId('segment-selector-0').fill('First Unlock')
      await page.getByTestId('segment-selector-0').press('ArrowDown')
      await page.getByTestId('segment-selector-0').press('Enter')

      await page.getByTestId('add-segment-button').click()

      await page.getByTestId('segment-selector-1').click()
      await page.getByTestId('segment-selector-1').fill('JQuery Unlocked')
      await page.getByTestId('segment-selector-1').press('ArrowDown')
      await page.getByTestId('segment-selector-1').press('Enter')

      // Make sure validation is ok.
      await expect(page.getByTestId('targeting-hint-message')).not.toBeVisible()

      // Save the broadcast.
      await clickMButton(page.getByTestId('create-new-broadcast-modal-ok-button'))
    })

    await test.step('Open the broadcast', async () => {
      // Open the broadcast details page.
      await page.getByTestId(`view-broadcast-Test Broadcast ${testToken}`).click()

      // Check the header matches the broadcast name.
      await expect(page.getByTestId('broadcast-details-overview-card')).toContainText(`Test Broadcast ${testToken}`)

      // Check that the two segments are listed in the targeting card.
      await expect(page.getByTestId('tab-1')).toBeVisible()
      await page.getByTestId('tab-1').click()

      await expect(page.getByTestId('targeting-card')).toContainText('First Unlock')
      await expect(page.getByTestId('targeting-card')).toContainText('JQuery Unlocked')

      // TODO: could test this a lot more. Edits, duplication, more complex settings, etc.))
    })

    await test.step('Delete the broadcast', async () => {
      // Delete the broadcast.
      await page.getByTestId('action-delete-broadcast-button').click()

      // Confirm the delete.
      await clickMButton(page.getByTestId('action-delete-broadcast-modal-ok-button'))
    })
  })
})
