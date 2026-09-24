/* global console, process */
// Smoke check that the custom cards render with live data on a player's details page.
import { chromium } from '@playwright/test'

const url = process.argv[2] ?? 'http://localhost:5551/players/Player:Ba88C6SfZz'

const browser = await chromium.launch()
const page = await browser.newPage({ viewport: { width: 1600, height: 1200 } })
const errors = []
page.on('pageerror', (err) => errors.push(String(err)))
page.on('console', (msg) => {
  if (msg.type() === 'error') errors.push(msg.text())
})

await page.goto(url, { waitUntil: 'domcontentloaded' })

// Wait for the app to boot and the player's data to arrive.
await page.waitForSelector('[data-testid="player-systems-card"]', { timeout: 60000 })

const systems = await page.textContent('[data-testid="player-systems-card"]')
const history = await page.textContent('[data-testid="player-match-history-card"]')
const cosmetics = await page.textContent('[data-testid="player-cosmetics-card"]')
const overview = await page.textContent('[data-testid="player-overview-card"]')

console.log('=== overview (resources) ===')
console.log(overview.replace(/\n+/g, ' ').slice(0, 300))
console.log('=== meta systems ===')
console.log(systems.replace(/\n+/g, ' ').slice(0, 600))
console.log('=== match history ===')
console.log(history.replace(/\n+/g, ' ').slice(0, 300))
console.log('=== cosmetics ===')
console.log(cosmetics.replace(/\n+/g, ' ').slice(0, 300))

await page.screenshot({ path: '/tmp/ts-player-details.png', fullPage: false })

const ok =
  overview.includes('Coins') &&
  overview.includes('Gems') &&
  overview.includes('Spin Tokens') &&
  overview.includes('Games Played') &&
  overview.includes('Games Won') &&
  overview.includes('Tricks Won') &&
  overview.includes('At Table') &&
  systems.includes('Meta Systems') &&
  systems.includes('Daily Reward') &&
  systems.includes('Weekly Event') &&
  !systems.includes('Record') &&
  !systems.includes('Tournament') &&
  history.includes('Match History')
console.log(errors.length > 0 ? `CONSOLE/PAGE ERRORS:\n${errors.join('\n')}` : 'NO PAGE ERRORS')
console.log(ok ? 'SMOKE OK' : 'SMOKE FAILED')

await browser.close()
process.exit(ok ? 0 : 1)
