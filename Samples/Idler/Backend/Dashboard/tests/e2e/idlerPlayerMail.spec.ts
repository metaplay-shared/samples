// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import { test, expect, clickMButton } from '@metaplay/playwright-config'

test.describe('Idler Specific Player Mail', () => {
  test.beforeEach('Navigate to player page', async ({ page, freshTestPlayer }) => {
    await page.goto(`/players/${freshTestPlayer}`)

    // Open player create player mail modal.
    await page.getByTestId('action-mail-button').click()
  })

  test('Sends a mail to a player, checks contents and deletes it', async ({ page, testToken }) => {
    // The information of attachment that we save in the filling form step and use in the checking that information is correct step.
    const attachmentData: { type: string; amount: number } = {
      type: '',
      amount: 0,
    }

    await test.step('Fill the title, body and add an attachment', async () => {
      // Fill the title and description of the form.
      await page.getByTestId('title-localizations-en-input').fill(`Test Mail ${testToken}`)
      await page.getByTestId('body-localizations-en-input').fill(`Lorem ipsum dolor sit ${testToken}.`)

      // Add an attachment and use the default values.
      await page.getByTestId('add-attachment-button').click()

      await test.step('Save attachment data for checking step', async () => {
        // Get first attachment form that includes the input elements.
        const attachmentForm = page.getByTestId('attachment-form')

        // Find the input element for `Type` field within the form and get its default value.
        const attachmentType = await attachmentForm
          .getByTestId('attachments-0-type-input-selected-option')
          .textContent()
        if (attachmentType === null) {
          throw new Error('Attachment type should be selected with a default value, but none found!')
        } else {
          attachmentData.type = attachmentType
        }

        // Find the input element for `Amount` field within the form and get its default value.
        const attachmentAmount = await attachmentForm.getByTestId('attachments-0-amount-input').inputValue()
        if (attachmentAmount === null) {
          throw new Error('There should be a default attachment amount, but none found!')
        } else {
          attachmentData.amount = parseInt(attachmentAmount, 10)
        }
      })

      // Press the `Send Mail` button to ok the modal.
      await clickMButton(page.getByTestId('action-mail-modal-ok-button'))
    })

    // Get the inbox mail item which will used in both check and delete steps.
    const inboxMailItem = page.getByTestId('player-inbox-mail-item').first()

    await test.step('Check that the inbox mail item contains the correct information', async () => {
      // TODO: Check that we got a success toast.

      // Check that the inbox list card contains the mail.
      await expect(inboxMailItem).toContainText(`Test Mail ${testToken}`)

      // Click the inboxMailItem row header to expand and expose content inside of it.
      await inboxMailItem.click()

      // Check that the expanded content contains the description text.
      await expect(page.getByTestId('player-inbox-mail-content')).toContainText(`Lorem ipsum dolor sit ${testToken}.`, {
        useInnerText: true,
      })

      // Get the reward badge.
      // Note: since there can be duplicate similar type rewards we cannot use that as unique identifier
      // we are using index that produces `generated-ui-reward-badge-0`, `generated-ui-reward-badge-1`, etc.
      const rewardBadge = page.getByTestId('generated-ui-reward-badge-0')

      // Get the reward badge text and check it contains the correct type that we saved in the filling form step.
      const rewardBadgeText = await rewardBadge.textContent()
      if (rewardBadgeText === null) {
        throw new Error('Reward badge text should be present, but none found!')
      }
      expect(rewardBadgeText).toContain(attachmentData.type.replace('Reward', ''))

      // Get the reward amount by parsing the reward badge text and check it is equal to the default amount saved in the filling form step.
      // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
      const rewardBadgeAmount = parseInt(rewardBadgeText.split('x')[1]!, 10)
      expect(rewardBadgeAmount).toEqual(attachmentData.amount)
    })

    await test.step('Delete the mail', async () => {
      // Open delete modal.
      await inboxMailItem.getByTestId('confirm-mail-delete').click()

      // Ok the modal.
      await clickMButton(page.getByTestId('confirm-mail-delete-ok-button'))

      // TODO: Check that we got a success toast.

      // Check that the mail was deleted.
      await expect(page.getByTestId('player-inbox-list-card')).not.toContainText(`Test Mail ${testToken}`)
    })

    await test.step('Check for audit logs of sending and deleting the mail', async () => {
      // Navigate to tab 1.
      await page.getByTestId('tab-1').click()

      // Get the audit log card.
      const auditLogCard = page.getByTestId('audit-log-card')

      // Check that the audit log card contains the mail deletion.
      const deleteEvent = auditLogCard.getByTestId('event-PlayerMailController-PlayerEventMailDeleted')
      await deleteEvent.scrollIntoViewIfNeeded()
      await expect(deleteEvent).toContainText('Mail deleted')

      // Check that the audit log card contains the mail sending.
      const sendEvent = auditLogCard.getByTestId('event-PlayerMailController-PlayerEventMailSent')
      await sendEvent.scrollIntoViewIfNeeded()
      await expect(sendEvent).toContainText('Mail sent')
      await expect(sendEvent).toContainText(`Test Mail ${testToken}`)
    })
  })
})
