import { describe, expect, test } from 'vitest'

import { lastPlayedDeckLabel } from '../../src/lastPlayedDeck'

const savedDecks = { '1': 'Load', '4': 'Kitsune burn' }
const starterDecks = { FireAndFoam: 'Fire and Foam', AlleySparks: 'Alley Sparks' }

describe('lastPlayedDeckLabel', () => {
  test('a fresh account has never played', () => {
    expect(lastPlayedDeckLabel(null, savedDecks, starterDecks)).toEqual('Never played')
    expect(lastPlayedDeckLabel(undefined, savedDecks, starterDecks)).toEqual('Never played')
  })

  test('a saved deck reads as its name', () => {
    const choice = { savedDeckId: 4, starterDeckId: null, isSaved: true, isStarter: false }
    expect(lastPlayedDeckLabel(choice, savedDecks, starterDecks)).toEqual('Kitsune burn')
  })

  test('a starter deck reads as the config entry name', () => {
    // The branch most accounts are actually in, and the one no dashboard fixture can arrange: a fresh
    // account's first match is a starter deck.
    const choice = { savedDeckId: 0, starterDeckId: 'FireAndFoam', isSaved: false, isStarter: true }
    expect(lastPlayedDeckLabel(choice, savedDecks, starterDecks)).toEqual('Fire and Foam (starter)')
  })

  test('a deleted saved deck falls back to the choice key rather than blank', () => {
    const choice = { savedDeckId: 9, starterDeckId: null, isSaved: true, isStarter: false }
    expect(lastPlayedDeckLabel(choice, savedDecks, starterDecks)).toEqual('saved:9')
  })

  test('a starter deck dropped from the config falls back the same way', () => {
    const choice = { savedDeckId: 0, starterDeckId: 'RetiredDeck', isSaved: false, isStarter: true }
    expect(lastPlayedDeckLabel(choice, savedDecks, starterDecks)).toEqual('starter:RetiredDeck')
  })

  test('a malformed choice says so — neither half set', () => {
    // No picker produces one, but a persisted row can carry anything and this is the one row an operator is
    // reading precisely because something is off.
    expect(lastPlayedDeckLabel({ isSaved: false, isStarter: false }, savedDecks, starterDecks)).toEqual(
      'Malformed deck choice'
    )
  })

  test('a malformed choice says so — both halves set', () => {
    // The case the fallback's comment claims: checking `isStarter` alone would give this row a confident
    // starter-deck label rather than saying the row itself is wrong.
    const choice = { savedDeckId: 4, starterDeckId: 'FireAndFoam', isSaved: true, isStarter: true }
    expect(lastPlayedDeckLabel(choice, savedDecks, starterDecks)).toEqual('Malformed deck choice')
  })
})
