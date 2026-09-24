import { describe, expect, test } from 'vitest'

import { shieldState, shieldStateLabel } from '../../src/shieldState'

describe('shieldState', () => {
  test('a fresh account is shielded', () => {
    expect(shieldState(0, 10, false)).toEqual('shielded')
  })

  test('an account inside the window with the waiver set is waived', () => {
    expect(shieldState(3, 10, true)).toEqual('waived')
  })

  test('an account past the threshold has played out, waiver or not', () => {
    // The one judgement in the card: at the threshold the waiver decides nothing, so reporting "Waived"
    // would imply a lever that is no longer connected to anything.
    expect(shieldState(10, 10, false)).toEqual('playedOut')
    expect(shieldState(10, 10, true)).toEqual('playedOut')
    expect(shieldState(47, 10, true)).toEqual('playedOut')
  })

  test('the last shielded match is still shielded', () => {
    expect(shieldState(9, 10, false)).toEqual('shielded')
  })

  test('a threshold of zero shields nobody', () => {
    // Global.NewcomerShieldMatches is config, so zero is an authorable value: it turns the shield off for
    // every account rather than shielding everyone forever.
    expect(shieldState(0, 0, false)).toEqual('playedOut')
  })
})

describe('shieldStateLabel', () => {
  test('names each state the way the badge reads', () => {
    expect(shieldStateLabel('shielded')).toEqual('Shielded')
    expect(shieldStateLabel('waived')).toEqual('Waived')
    expect(shieldStateLabel('playedOut')).toEqual('Played out')
  })
})
