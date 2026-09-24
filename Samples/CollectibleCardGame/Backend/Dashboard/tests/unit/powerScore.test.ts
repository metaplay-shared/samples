import { describe, expect, test } from 'vitest'

import { computePowerScore } from '../../src/powerScore'

describe('computePowerScore', () => {
  test('sums the ranks the account holds the deck at', () => {
    const collection = { EmberKit: 1, Foxfire: 3, FlameDancer: 5 }
    expect(computePowerScore(['EmberKit', 'Foxfire', 'FlameDancer'], collection)).toEqual(9)
  })

  test('a card the collection does not hold contributes nothing', () => {
    // DeckValidator.cs's own documented behaviour: a partially built or stale list scores what it actually
    // has rather than throwing. A legal deck can never contain one.
    const collection = { EmberKit: 2 }
    expect(computePowerScore(['EmberKit', 'NotOwned'], collection)).toEqual(2)
    expect(computePowerScore(['NotOwned'], collection)).toEqual(0)
  })

  test('an empty deck scores zero', () => {
    expect(computePowerScore([], { EmberKit: 5 })).toEqual(0)
  })

  test('a missing deck or collection scores zero rather than throwing', () => {
    // Both are reachable from a real page: `decks` is empty on a fresh account, and a subscription's data is
    // undefined for the first render.
    expect(computePowerScore(undefined, { EmberKit: 5 })).toEqual(0)
    expect(computePowerScore(['EmberKit'], undefined)).toEqual(0)
    expect(computePowerScore(null, null)).toEqual(0)
  })

  test('a duplicate card is counted once per entry, as the sum implies', () => {
    // The singleton rule is deck legality's business, not the score's: the C# definition walks the list and
    // adds, so a list that broke the rule scores the higher number rather than being silently corrected.
    expect(computePowerScore(['EmberKit', 'EmberKit'], { EmberKit: 4 })).toEqual(8)
  })

  test('a whole starter deck at the rank floor scores its card count', () => {
    const cards = Array.from({ length: 25 }, (_, index) => `Card${String(index)}`)
    const collection: Record<string, number> = {}
    for (const cardId of cards) collection[cardId] = 1

    expect(computePowerScore(cards, collection)).toEqual(25)
  })
})
