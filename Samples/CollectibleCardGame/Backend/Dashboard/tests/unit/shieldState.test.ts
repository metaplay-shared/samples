import { describe, expect, test } from 'vitest'

import { shieldState } from '../../src/shieldState'

describe('shieldState', () => {
  // Global.NewcomerShieldMatches is config, so a threshold of zero is an authorable value: it turns the shield
  // off for every account rather than shielding everyone forever.
  test.each([
    { played: 0, threshold: 10, waived: false, expected: 'shielded' },
    { played: 9, threshold: 10, waived: false, expected: 'shielded' },
    { played: 3, threshold: 10, waived: true, expected: 'waived' },
    { played: 10, threshold: 10, waived: false, expected: 'playedOut' },
    { played: 0, threshold: 0, waived: false, expected: 'playedOut' },
  ])('$played of $threshold played, waived $waived, is $expected', ({ played, threshold, waived, expected }) => {
    expect(shieldState(played, threshold, waived)).toEqual(expected)
  })

  test('an account past the threshold has played out, waiver or not', () => {
    // The one judgement in the card: at the threshold the waiver decides nothing, so reporting "Waived"
    // would imply a lever that is no longer connected to anything.
    expect(shieldState(10, 10, true)).toEqual('playedOut')
    expect(shieldState(47, 10, true)).toEqual('playedOut')
  })
})
