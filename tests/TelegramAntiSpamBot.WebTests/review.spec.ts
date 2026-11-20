import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { createServer } from 'node:http'
import type { AddressInfo } from 'node:net'
import { parseHistoryPage, parseReviewMessagePage } from '../../src/web/src/api/contracts'

const chatId = '-1001234567890'
const userId = '9007199254740993'
const ticks = '638712864000000001'
const initData = 'query_id=mock%2Bquery&user=%7B%22id%22%3A42%7D&auth_date=1795183171&hash=mock%2Bsignature'
const authorization = `tma ${initData}`
const reviewBase = 'https://review.example.test/api/review/'
const accessRoute = `${reviewBase}access`
const apiRoute = (url: URL) => url.href.startsWith(reviewBase) && url.href !== accessRoute
const telegramScript = 'https://telegram.org/js/telegram-web-app.js*'
const messageJSON = `{"ChatId":${chatId},"MessageId":7,"UserId":${userId},"From":{"Id":${userId},"Name":"Alice Smith","Username":null},"ReplyTo":null,"Message":"Hello team!\\n<em>Not HTML</em>","ImageText":"Image text: special offer","ContentHash":"HASH","MessageDateTicks":${ticks}}`
const firstPage = `{"Messages":[${messageJSON}],"ContinuationToken":"next+/= page"}`
const secondMessageJSON = `{"ChatId":${chatId},"MessageId":8,"UserId":43,"From":{"Id":43,"Name":"Bob Jones","Username":null},"ReplyTo":null,"Message":"Another pending message","ImageText":"","ContentHash":"HASH","MessageDateTicks":${ticks}}`
const lastPage = `{"Messages":[${secondMessageJSON}],"ContinuationToken":null}`
const emptyPage = '{"Messages":[],"ContinuationToken":null}'
const chatPage = `{"Chats":[{"ChatId":${chatId},"Name":"Test group","LastSeenUtc":"2026-10-25T00:00:00+00:00"}],"ContinuationToken":null}`

test.beforeEach(async ({ page }, testInfo) => {
  if (testInfo.tags.includes('@real-network')) return

  await page.route(telegramScript, route => route.fulfill({
    contentType: 'application/javascript',
    body: `window.Telegram = { WebApp: {
      initData: ${JSON.stringify(initData)},
      ready() {}, expand() {}, close() { document.documentElement.dataset.telegramClosed = 'true' }
    } };`,
  }))
  await page.route(accessRoute, route => {
    const request = route.request()
    expect(request.method()).toBe('GET')
    expect(request.headers().authorization).toBe(authorization)
    expect(request.postData()).toBeNull()
    return route.fulfill({ status: 204 })
  })
})

test('review and history share authors with lossless IDs and usernames', () => {
  const review = parseReviewMessagePage(firstPage).messages[0]
  expect(review.from).toEqual({ id: userId, name: 'Alice Smith', username: null })

  const history = parseHistoryPage(`{"messages":[{"chatId":${chatId},"messageId":7,"dateTimeTicks":${ticks},
    "content":{"from":{"id":${userId},"name":"Alice Smith","username":null},"replyTo":null,"payload":{}}}],
    "continuationToken":null}`).rows[0]
  expect(history.content.from).toEqual(review.from)

  const reply = `{"MessageId":6,"From":{"Id":${userId},"Name":"Bob","Username":"bob"},"Text":"Question"}`
  const withReply = parseReviewMessagePage(firstPage.replace('"ReplyTo":null', `"ReplyTo":${reply}`)).messages[0]
  expect(withReply.replyTo?.from).toEqual({ id: userId, name: 'Bob', username: 'bob' })
})

test('DANGER clears only the selected chat history after explicit confirmation', async ({ page }) => {
  let deletions = 0
  let release!: () => void
  const gate = new Promise<void>(resolve => { release = resolve })
  await page.route(apiRoute, async route => {
    if (route.request().method() === 'DELETE') {
      deletions++
      expect(new URL(route.request().url()).pathname).toBe(`/api/review/history/${chatId}`)
      expect(route.request().headers().authorization).toBe(authorization)
      await gate
      return route.fulfill({ status: 204 })
    }
    return route.fulfill({ contentType: 'application/json', body: '{"messages":[],"continuationToken":null}' })
  })
  await openWorkspace(page, { loadQueue: false })
  await openSection(page, 'Message history')
  const danger = page.getByRole('button', { name: 'DANGER: Clear chat history', exact: true })
  await expect(danger).toBeDisabled()
  await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
  await danger.click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText(chatId)
  await expect(dialog).toContainText('This cannot be undone')
  await expect(dialog.getByRole('button', { name: 'Delete chat history', exact: true })).toBeDisabled()
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
  expect(deletions).toBe(0)
  await danger.click()
  await dialog.getByLabel('Type DANGER to confirm', { exact: true }).fill('DANGER')
  await dialog.getByRole('button', { name: 'Delete chat history', exact: true }).click()
  try {
    await expect.poll(() => deletions).toBe(1)
    await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeDisabled()
    await expect(dialog.getByRole('button', { name: 'Delete chat history', exact: true })).toBeDisabled()
  } finally { release() }
  await expect(dialog).toHaveCount(0)
  await expect(page.getByRole('status')).toContainText(`Message history cleared for chat ${chatId}`)
  await expect(page.getByRole('region', { name: 'Message history records' })).toContainText('No records loaded.')
  expect(deletions).toBe(1)
})

test('failed history deletion warns about partial results and is never retried automatically', async ({ page }) => {
  let deletions = 0
  await page.route(apiRoute, route => {
    if (route.request().method() === 'DELETE') {
      deletions++
      return route.fulfill({ status: 503 })
    }
    return route.fulfill({ contentType: 'application/json', body: '{"messages":[],"continuationToken":null}' })
  })
  await openWorkspace(page, { loadQueue: false })
  await openSection(page, 'Message history')
  await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
  await page.getByRole('button', { name: 'DANGER: Clear chat history', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Type DANGER to confirm', { exact: true }).fill('DANGER')
  await dialog.getByRole('button', { name: 'Delete chat history', exact: true }).click()
  await expect(dialog.getByRole('alert')).toContainText('Deletion may be partial')
  await expect(dialog.getByRole('button', { name: 'Delete chat history', exact: true })).toBeDisabled()
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
  expect(deletions).toBe(1)
})

for (const sdkLoaded of [false, true]) {
  test(`shows launch instructions outside Telegram (SDK loaded: ${sdkLoaded})`, async ({ page }) => {
    let apiCalls = 0
    await page.route('**/api/review/**', route => { apiCalls++; return route.abort() })
    await page.route(telegramScript, route => sdkLoaded ? route.fulfill({
      contentType: 'application/javascript',
      body: 'window.Telegram = { WebApp: { initData: "", initDataUnsafe: { user: { id: 42 } }, ready() {} } };',
    }) : route.abort())
    await page.goto('/')
    await expect(page.getByRole('heading', { name: 'Open in Telegram' })).toBeVisible()
    await expect(page.getByText('Open the private chat with the bot', { exact: false })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Load records' })).toHaveCount(0)
    expect(apiCalls).toBe(0)
  })
}

for (const status of [401, 403, 200]) {
  test(`keeps the workspace closed after an unsuccessful access check (HTTP ${status})`, async ({ page }) => {
    let checks = 0
    let apiCalls = 0
    await page.route(apiRoute, route => { apiCalls++; return route.abort() })
    await page.route(accessRoute, route => {
      checks++
      return route.fulfill({ status })
    })
    await page.goto('/')
    await expect(page.getByRole('heading', { name: status === 401 ? 'Reopen in Telegram'
      : status === 403 ? 'Access denied' : 'Review unavailable' })).toBeVisible()
    await expect(page.getByRole('alert')).toContainText(status === 401 ? 'Your Telegram launch is invalid or expired'
      : status === 403 ? 'You do not have access' : 'unexpected access response')
    await expect(page.getByRole('button', { name: 'Load records' })).toHaveCount(0)
    expect(checks).toBe(1)
    expect(apiCalls).toBe(0)
  })
}

test('does not forward raw Telegram launch data on an access redirect', async ({ page }) => {
  const target = 'https://redirect.example.test/access'
  let forwarded = 0
  page.on('request', request => { if (request.url() === target) forwarded++ })
  await page.route(target, route => route.abort())
  await page.route(accessRoute, route => route.fulfill({ status: 307, headers: { location: target } }))
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Review unavailable' })).toBeVisible()
  expect(forwarded).toBe(0)
})

async function openWorkspace(page: Page, { mockChats = true, loadQueue = true }: {
  mockChats?: boolean; loadQueue?: boolean
} = {}) {
  if (mockChats) await page.route('**/api/review/chats?*', route => {
    expect(route.request().headers().authorization).toBe(authorization)
    expect(route.request().headers()['x-functions-key']).toBeUndefined()
    return route.fulfill({ contentType: 'application/json', body: chatPage })
  })
  await page.goto('/')
  await expect(page.getByRole('button', { name: 'Connect Function', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Connection', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Disconnect and clear key', exact: true })).toHaveCount(0)
  await expect(page.getByLabel('Function key', { exact: false })).toHaveCount(0)
  if (loadQueue) {
    await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
    await page.getByRole('button', { name: 'Load records' }).click()
  }
}

async function openSection(page: Page, name: string) {
  await page.getByRole('button', { name: 'Menu', exact: true }).click()
  await page.getByRole('navigation', { name: 'Workspace sections' }).getByRole('button', { name, exact: true }).click()
}

test('uses one shared picker and loads only the selected group snapshot', async ({ page }) => {
  const channelId = '-1009876543210'
  const groupLookups: string[] = []
  const queries: string[] = []
  const resets: string[] = []
  await page.route('**/api/review/search-snapshots?*', route => {
    queries.push(new URL(route.request().url()).searchParams.get('channelId')!)
    return route.fulfill({ json: queries.length === 1 ? [] : [
      { name: channelId, sizeBytes: 1048576, lastModified: '2026-10-31T12:00:00Z' },
      { name: '-1002222222222', sizeBytes: 2097152, lastModified: '2026-10-31T12:00:00Z' },
    ] })
  })
  await page.route(`**/api/review/search-snapshots/groups/${chatId}/channel`, route => {
    groupLookups.push(new URL(route.request().url()).pathname)
    return route.fulfill({ json: { channelId } })
  })
  await page.route('**/api/review/search-snapshots/*/reset', route => {
    expect(route.request().method()).toBe('POST')
    resets.push(new URL(route.request().url()).pathname)
    return route.fulfill({ status: 202 })
  })
  await openWorkspace(page, { loadQueue: false })
  await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
  await openSection(page, 'Snapshots')
  await expect(page.getByRole('combobox')).toHaveCount(1)
  await expect(page.getByLabel('Discussion group')).toHaveValue(chatId)
  await expect(page.getByRole('textbox')).toHaveCount(0)
  expect(groupLookups).toEqual([])
  expect(queries).toEqual([])
  const load = page.getByRole('button', { name: 'Load snapshot' })
  await load.click()
  await expect(page.getByText('No snapshot published yet.')).toBeVisible()
  await page.getByRole('button', { name: 'Queue rebuild' }).click()
  await expect(page.getByRole('status')).toContainText('Rebuild queued.')
  await load.click()
  await expect(page.getByText('1.00 MiB', { exact: false })).toBeVisible()
  await expect(page.getByText('2.00 MiB', { exact: false })).toHaveCount(0)
  expect(queries).toEqual([channelId, channelId])
  expect(resets).toEqual([`/api/review/search-snapshots/${channelId}/reset`])
})

test('requires a chat and clears stale cards and continuation when switching chats', async ({ page }) => {
  const otherChat = '-1005555555555'
  const queries: URL[] = []
  await page.route(apiRoute, route => {
    const url = new URL(route.request().url())
    if (url.pathname.endsWith('/chats')) {
      const other = { ChatId: Number(otherChat), Name: 'Other group', LastSeenUtc: '2026-10-25T00:00:00Z' }
      return route.fulfill({ json: { Chats: [...JSON.parse(chatPage).Chats, other], ContinuationToken: null } })
    }
    queries.push(url)
    const body = url.searchParams.get('chatId') === chatId ? firstPage : lastPage.replace(chatId, otherChat)
    return route.fulfill({ contentType: 'application/json', body })
  })
  await openWorkspace(page, { mockChats: false, loadQueue: false })
  const chat = page.getByLabel('Chat', { exact: true })
  const load = page.getByRole('button', { name: 'Load records' })
  await expect(chat).toBeEnabled()
  await expect(load).toBeDisabled()
  await expect(page.getByLabel('Page size')).toHaveCount(0)
  await chat.selectOption(chatId)
  expect(queries).toHaveLength(0)
  await load.click()
  await expect(page.getByRole('heading', { name: 'Alice Smith' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Load more' })).toBeVisible()
  await chat.selectOption(otherChat)
  await expect(page.getByRole('heading', { name: 'Alice Smith' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Load more' })).toHaveCount(0)
  expect(queries).toHaveLength(1)
  await load.click()
  await expect(page.getByRole('heading', { name: 'Bob Jones' })).toBeVisible()
  expect(queries.map(url => [...url.searchParams])).toEqual([
    [['chatId', chatId], ['pageSize', '100']], [['chatId', otherChat], ['pageSize', '100']],
  ])
})

test('connects after authentication, paginates, and does not persist initData in workspace storage', async ({ page }) => {
  const queries: string[] = []
  await page.route(apiRoute, async route => {
    expect(route.request().headers().authorization).toBe(authorization)
    expect(route.request().headers()['x-functions-key']).toBeUndefined()
    expect(route.request().headers()['x-review-endpoint']).toBeUndefined()
    queries.push(route.request().url())
    await route.fulfill({ contentType: 'application/json', body: queries.length === 1 ? firstPage : lastPage })
  })
  await openWorkspace(page)
  await expect(page.getByRole('heading', { name: 'Alice Smith' })).toBeVisible()
  await expect(page.getByRole('listitem').first()).toContainText(userId)
  await expect(page.locator(`[title="${ticks}"]`)).toContainText('1 Jan 2025, 00:00')
  await expect(page.locator('.message-text').first()).toHaveText('Hello team!\n<em>Not HTML</em>')
  await expect(page.locator('.message-text em')).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'Image transcription OCR' })).toBeVisible()
  await page.getByRole('button', { name: 'Load more', exact: true }).click()
  await expect(page.locator('.message-row')).toHaveCount(2)
  expect(new URL(queries[1]).searchParams.get('continuationToken')).toBe('next+/= page')
  expect(queries.map(url => new URL(url).searchParams.get('pageSize'))).toEqual(['100', '100'])
  expect(new URL(queries[0]).origin).toBe('https://review.example.test')
  expect(new URL(queries[0]).pathname).toBe('/api/review/messages')
  const storage = await page.evaluate(() => ({ local: { ...localStorage }, session: { ...sessionStorage } }))
  expect(JSON.stringify(storage)).not.toContain(initData)
  expect(JSON.stringify(storage)).toContain(chatId)
  await page.reload()
  await expect(page.getByLabel('Chat', { exact: true })).toHaveValue(chatId)
  await expect(page.getByLabel('Chat', { exact: true })).toBeEnabled()
  await page.getByRole('button', { name: 'Load records' }).click()
  await expect(page.locator('.message-row')).toHaveCount(1)
})

test('parses agreed chat and message contracts without losing integer precision', async ({ page }) => {
  const chatTokens: (string | null)[] = []
  const messageTokens: (string | null)[] = []
  await page.route(apiRoute, route => {
    const url = new URL(route.request().url())
    if (url.pathname.endsWith('/chats')) {
      chatTokens.push(url.searchParams.get('continuationToken'))
      const body = chatTokens.length === 1 ? '{"Chats":[],"ContinuationToken":"chat+/="}' : chatPage
      return route.fulfill({ contentType: 'application/json', body })
    }
    messageTokens.push(url.searchParams.get('continuationToken'))
    return route.fulfill({ contentType: 'application/json',
      body: messageTokens.length === 1 ? firstPage : lastPage })
  })
  await openWorkspace(page, { mockChats: false })
  await expect(page.getByLabel('Chat', { exact: true })).toHaveValue(chatId)
  await expect(page.getByRole('heading', { name: 'Alice Smith' })).toBeVisible()
  await expect(page.getByRole('listitem').first()).toContainText(userId)
  await expect(page.locator(`[title="${ticks}"]`)).toContainText('1 Jan 2025, 00:00')
  await page.getByRole('button', { name: 'Load more', exact: true }).click()
  await expect(page.locator('.message-row')).toHaveCount(2)
  await expect(page.getByRole('button', { name: 'Load more', exact: true })).toHaveCount(0)
  expect(chatTokens).toEqual([null, 'chat+/='])
  expect(messageTokens).toEqual([null, 'next+/= page'])
})

test('not spam submits one decision and removes only the reviewed card', async ({ page }) => {
  const mutations: { path: string; body: string | null }[] = []
  await page.route(apiRoute, async route => {
    if (route.request().method() === 'GET') {
      const body = `{"Messages":[${messageJSON},${secondMessageJSON}],"ContinuationToken":null}`
      await route.fulfill({ contentType: 'application/json', body })
    } else {
      mutations.push({ path: new URL(route.request().url()).pathname, body: route.request().postData() })
      await route.fulfill({ status: 204 })
    }
  })
  await openWorkspace(page)
  const cards = page.getByRole('list', { name: 'Pending messages' }).getByRole('listitem')
  await expect(cards).toHaveCount(2)
  await cards.filter({ hasText: 'Alice Smith' }).getByRole('button', { name: 'Not spam', exact: true }).click()
  expect(mutations).toHaveLength(0)
  await page.getByRole('button', { name: 'Confirm not spam', exact: true }).click()
  await expect(cards).toHaveCount(1)
  await expect(cards.first()).toContainText('Bob Jones')
  await expect(page.getByRole('status')).toContainText('Marked as not spam.')
  expect(mutations).toEqual([{ path: `/api/review/messages/${chatId}/7`, body: '{"isSpam":false}' }])
  await expect(page.getByRole('button', { name: 'Remove restrictions' })).toHaveCount(0)
})

test('spam confirms once and does not implicitly remove restrictions', async ({ page }) => {
  const bodies: unknown[] = []
  await page.route(apiRoute, async route => {
    if (route.request().method() === 'GET') await route.fulfill({ contentType: 'application/json', body: lastPage })
    else {
      expect(new URL(route.request().url()).pathname).toBe(`/api/review/messages/${chatId}/8`)
      bodies.push(route.request().postDataJSON())
      await route.fulfill({ status: 204 })
    }
  })
  await openWorkspace(page)
  await page.getByRole('button', { name: 'Spam', exact: true }).click()
  await page.getByRole('button', { name: 'Cancel', exact: true }).click()
  expect(bodies).toHaveLength(0)
  await page.getByRole('button', { name: 'Spam', exact: true }).click()
  await page.getByRole('button', { name: 'Confirm spam', exact: true }).click()
  await expect(page.getByText('No records found.')).toBeVisible()
  expect(bodies).toEqual([{ isSpam: true }])
})

for (const status of [401, 403]) {
  test(`closes the workspace on a mutation HTTP ${status} without retrying`, async ({ page }) => {
    let checks = 0
    let mutations = 0
    page.on('request', request => { if (request.url() === accessRoute) checks++ })
    await page.route(apiRoute, route => {
      if (route.request().method() === 'GET') return route.fulfill({ contentType: 'application/json', body: firstPage })
      mutations++
      expect(route.request().headers().authorization).toBe(authorization)
      return route.fulfill({ status })
    })
    await openWorkspace(page)
    await page.getByRole('button', { name: 'Spam', exact: true }).click()
    await page.getByRole('button', { name: 'Confirm spam', exact: true }).click()
    await expect(page.getByRole('alert')).toContainText(status === 401
      ? 'reopen it from the menu in the private bot chat' : 'You do not have access')
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Load records' })).toHaveCount(0)
    await page.getByRole('button', { name: 'Close Mini App' }).click()
    await expect(page.locator('html')).toHaveAttribute('data-telegram-closed', 'true')
    expect(checks).toBe(1)
    expect(mutations).toBe(1)
  })
}

test('failed mutations are not retried and show uncertain-outcome warning', async ({ page }) => {
  let mutations = 0
  await page.route(apiRoute, async route => {
    if (route.request().method() === 'GET') await route.fulfill({ contentType: 'application/json', body: firstPage })
    else { mutations++; await route.fulfill({ status: 500, body: 'Server error' }) }
  })
  await openWorkspace(page)
  await page.getByRole('button', { name: 'Not spam', exact: true }).click()
  await page.getByRole('button', { name: 'Confirm not spam', exact: true }).click()
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('The action may already have completed.')
  await expect(page.getByRole('button', { name: 'Confirm not spam', exact: true })).toBeDisabled()
  expect(mutations).toBe(1)
  await expect(page.locator('.message-row')).toHaveCount(1)
})

test('continues after an empty page with a continuation token', async ({ page }) => {
  let calls = 0
  await page.route(apiRoute, route => route.fulfill({ contentType: 'application/json',
    body: ++calls === 1 ? '{"Messages":[],"ContinuationToken":"next"}' : emptyPage }))
  await openWorkspace(page)
  await expect(page.getByText('No records found.')).toBeVisible()
  await page.getByRole('button', { name: 'Load more' }).click()
  await expect(page.getByText('No records found.')).toBeVisible()
})

test('renders desktop and mobile without horizontal overflow or clipped action labels', async ({ page }, testInfo) => {
  await page.route(apiRoute, route => route.fulfill({ contentType: 'application/json', body: firstPage }))
  await openWorkspace(page)
  await expect(page.getByRole('heading', { name: 'Alice Smith' })).toBeVisible()
  for (const width of [1440, 768, 375, 320]) {
    await page.setViewportSize({ width, height: 900 })
    await page.evaluate(() => document.fonts.ready)
    const overflow = await page.evaluate(() => ({
      width: innerWidth, scrollWidth: document.documentElement.scrollWidth,
      elements: [...document.querySelectorAll('#root *')].filter(element => element.getBoundingClientRect().right > innerWidth)
        .map(element => `${element.tagName}.${element.className}`),
    }))
    expect(overflow.scrollWidth, JSON.stringify(overflow)).toBeLessThanOrEqual(width)
    expect(await page.locator('.button').evaluateAll(buttons => buttons.filter(button =>
      button.getBoundingClientRect().width > 0 && button.scrollWidth > button.clientWidth + 1,
    ).map(button => button.textContent))).toEqual([])
    await page.screenshot({ path: testInfo.outputPath(`review-${width}.png`), fullPage: true })
  }
  expect(await page.evaluate(() => [...document.fonts].some(font => font.family.includes('Manrope') && font.status === 'loaded'))).toBe(true)
})

test('uses Telegram theme and safe-area updates in a narrow viewport', async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 680 })
  await page.route(apiRoute, route => route.fulfill({ contentType: 'application/json', body: firstPage }))
  await openWorkspace(page)
  await page.evaluate(() => {
    const style = document.documentElement.style
    style.setProperty('--tg-color-scheme', 'dark')
    style.setProperty('--tg-theme-bg-color', '#202a31')
    style.setProperty('--tg-theme-text-color', '#f2f4f5')
    style.setProperty('--tg-safe-area-inset-top', '18px')
    style.setProperty('--tg-content-safe-area-inset-top', '24px')
    style.setProperty('--tg-safe-area-inset-left', '12px')
    style.setProperty('--tg-safe-area-inset-right', '12px')
  })
  await expect(page.locator('html')).toHaveCSS('color-scheme', 'dark')
  await expect(page.locator('.app')).toHaveCSS('padding-top', '42px')
  await expect(page.locator('.topbar')).toHaveCSS('background-color', 'rgb(32, 42, 49)')
  await expect(page.locator('.message-text').first()).toHaveCSS('color', 'rgb(242, 244, 245)')
  await page.getByRole('button', { name: 'Spam', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toHaveCSS('top', '42px')
  await expect(dialog).toHaveCSS('background-color', 'rgb(32, 42, 49)')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})

test('browser calls a separate Function origin directly with CORS preflight', { tag: '@real-network' }, async ({ page }) => {
  const received: { url: string; key: string | undefined; method: string; body: string; origin: string | undefined }[] = []
  const preflights: string[] = []
  const server = createServer(async (incoming, response) => {
    response.setHeader('Access-Control-Allow-Origin', 'http://127.0.0.1:5179')
    response.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
    response.setHeader('Access-Control-Allow-Headers', 'authorization, content-type')
    if (incoming.method === 'OPTIONS') {
      preflights.push(incoming.headers['access-control-request-headers'] ?? '')
      response.writeHead(204)
      response.end()
      return
    }
    let body = ''
    for await (const chunk of incoming) body += chunk
    received.push({ url: incoming.url!, key: incoming.headers.authorization,
      method: incoming.method!, body, origin: incoming.headers.origin })
    if (incoming.url === '/api/review/access' || incoming.method !== 'GET') {
      response.writeHead(204)
      response.end()
      return
    }
    response.writeHead(200, { 'Content-Type': 'application/json' })
    response.end(incoming.url?.startsWith('/api/review/chats?') ? chatPage : firstPage)
  })
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve))
  const endpoint = `http://127.0.0.1:${(server.address() as AddressInfo).port}/api`
  try {
    await page.goto('/', { waitUntil: 'commit' })
    await page.evaluate(async ({ endpoint, initData, chatId }) => {
      const modulePath = '/src/api/client.ts'
      const api = await import(modulePath)
      const connection = { endpoint, initData, onAccessFailure() {} }
      await api.checkReviewAccess(connection, new AbortController().signal)
      await api.getChats(connection, null)
      const result = await api.getMessages(connection, chatId, null, new AbortController().signal)
      await api.reviewMessage(connection, result.rows[0], false)
    }, { endpoint, initData, chatId })
    const origin = 'http://127.0.0.1:5179'
    expect(received).toEqual([
      { url: '/api/review/access', key: authorization, method: 'GET', body: '', origin },
      { url: '/api/review/chats?pageSize=100', key: authorization, method: 'GET', body: '', origin },
      { url: `/api/review/messages?chatId=${chatId}&pageSize=100`, key: authorization, method: 'GET', body: '', origin },
      { url: `/api/review/messages/${chatId}/7`, key: authorization, method: 'POST', body: '{"isSpam":false}', origin },
    ])
    expect(preflights.some(headers => headers.includes('authorization'))).toBe(true)
    expect(preflights.some(headers => headers.includes('content-type'))).toBe(true)
  } finally {
    server.closeAllConnections()
    await new Promise<void>(resolve => server.close(() => resolve()))
  }
})

test('API_URL from the test environment overrides a previously remembered endpoint', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('antispam-review.settings', JSON.stringify({ endpoint: 'https://stale.example.test', chatId: '-1' }))
  })
  await page.route(apiRoute, route => route.fulfill({ contentType: 'application/json', body: emptyPage }))
  await openWorkspace(page)
  await expect(page.getByText('No records found.')).toBeVisible()
})

test('network failures include an English CORS diagnostic', async ({ page }) => {
  await page.route(apiRoute, route => route.abort('failed'))
  await openWorkspace(page)
  await expect(page.getByRole('alert')).toContainText('Check its URL, network access, and CORS settings.')
})

test('loads all chat pages, including empty pages, and refreshes names', async ({ page }) => {
  const tokens: (string | null)[] = []
  await page.route(apiRoute, route => {
    const url = new URL(route.request().url())
    if (url.pathname.endsWith('/chats')) {
      tokens.push(url.searchParams.get('continuationToken'))
      const body = tokens.length === 1 ? '{"Chats":[],"ContinuationToken":"opaque+/="}' : chatPage
      return route.fulfill({ contentType: 'application/json', body })
    }
    return route.fulfill({ contentType: 'application/json', body: emptyPage })
  })
  await openWorkspace(page, { mockChats: false })
  await expect(page.getByLabel('Chat', { exact: true })).toHaveValue(chatId)
  expect(tokens).toEqual([null, 'opaque+/='])
  await openSection(page, 'Refresh chats')
  await expect.poll(() => tokens.length).toBe(3)
  await expect(page.getByLabel('Chat', { exact: true })).toBeEnabled()
})

test('empty catalog and catalog errors do not automatically load message queues', async ({ page }) => {
  let calls = 0
  await page.route(apiRoute, route => {
    expect(new URL(route.request().url()).pathname).toBe('/api/review/chats')
    return ++calls === 1 ? route.fulfill({ contentType: 'application/json', body: '{"Chats":[],"ContinuationToken":null}' })
      : route.fulfill({ status: 503, body: '' })
  })
  await openWorkspace(page, { mockChats: false, loadQueue: false })
  await expect(page.getByLabel('Chat', { exact: true })).toBeEnabled()
  await expect(page.getByLabel('Chat', { exact: true })).toHaveValue('')
  await expect(page.getByRole('button', { name: 'Load records' })).toBeDisabled()
  await openSection(page, 'Refresh chats')
  await expect(page.getByRole('alert')).toContainText('Request failed (HTTP 503).')
  expect(calls).toBe(2)
})

test('command calls filters, paginates, refreshes and stays read-only', async ({ page }, testInfo) => {
  const queries: URL[] = []
  await page.route('**/api/review/command-calls?*', route => {
    expect(route.request().method()).toBe('GET')
    expect(route.request().headers().authorization).toBe(authorization)
    const url = new URL(route.request().url())
    queries.push(url)
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify({
      calls: [{ chatId, command: url.searchParams.has('continuationToken') ? '/stat' : '/report', lastCall: '2026-10-31T12:00:00Z' }],
      continuationToken: url.searchParams.has('continuationToken') ? null : 'next+/= page',
    }) })
  })
  await openWorkspace(page, { loadQueue: false })
  await openSection(page, 'Command calls')
  await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
  await page.getByRole('button', { name: 'Load records' }).click()
  const table = page.getByRole('region', { name: 'Command calls records' })
  await expect(table.getByRole('listitem')).toHaveCount(1)
  await expect(table).toContainText('/report')
  await expect(table).toContainText('31 Oct 2026')
  expect(queries[0].searchParams.get('chatId')).toBe(chatId)
  expect(queries[0].searchParams.get('pageSize')).toBe('100')
  await page.getByRole('button', { name: 'Load more', exact: true }).click()
  await expect(table.getByRole('listitem')).toHaveCount(2)
  expect(queries[1].searchParams.get('continuationToken')).toBe('next+/= page')
  await page.getByRole('button', { name: 'Load records' }).click()
  await expect(table.getByRole('listitem')).toHaveCount(1)
  expect(queries[2].searchParams.get('continuationToken')).toBeNull()
  await page.screenshot({ path: testInfo.outputPath('calls-desktop.png'), fullPage: true })
  await page.setViewportSize({ width: 390, height: 844 })
  const menu = page.getByRole('button', { name: 'Menu', exact: true })
  await menu.click()
  await expect(page.getByRole('navigation').getByRole('button', { name: 'Message history' })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(menu).toHaveAttribute('aria-expanded', 'false')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
  await page.screenshot({ path: testInfo.outputPath('calls-mobile.png'), fullPage: true })
  await openSection(page, 'Review queue')
  await expect(page.getByRole('button', { name: 'Load records', exact: true })).toBeVisible()
})

test('message history preserves numeric precision, plain text and reply context', async ({ page }, testInfo) => {
  const queries: URL[] = []
  await page.route('**/api/review/history?*', route => {
    const url = new URL(route.request().url())
    queries.push(url)
    expect(route.request().headers().authorization).toBe(authorization)
    const body = url.searchParams.has('continuationToken')
      ? `{"messages":[{"chatId":${chatId},"messageId":8,"dateTimeTicks":${ticks},"content":{"from":null,"replyTo":null,"text":""}}],"continuationToken":null}`
      : `{"messages":[{"chatId":${chatId},"messageId":7,"dateTimeTicks":${ticks},"content":{"from":{"id":${userId},"name":"Alice Smith","username":"alice"},"text":"Hello team!\\n<em>Not HTML</em>","replyTo":{"messageId":6,"from":{"id":43,"name":"Bob Jones","username":null},"text":"Original question"}}}],"continuationToken":"history+/="}`
    return route.fulfill({ contentType: 'application/json', body })
  })
  await openWorkspace(page, { loadQueue: false })
  await openSection(page, 'Message history')
  await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
  await page.getByRole('button', { name: 'Load records' }).click()
  const table = page.getByRole('region', { name: 'Message history records' })
  await expect(table).toContainText(userId)
  await expect(table.locator(`[title="${ticks}"]`)).toContainText('1 Jan 2025, 00:00')
  await table.locator('summary').first().click()
  await expect(table).toContainText('Original question')
  await expect(table).toContainText('Bob Jones')
  await expect(table.locator('em')).toHaveCount(0)
  await expect(table.locator('summary .history-preview')).toHaveText('Hello team!\n<em>Not HTML</em>')
  await expect(table.getByRole('region', { name: 'User text', exact: true })).toContainText('Hello team!\n<em>Not HTML</em>')
  expect(queries[0].searchParams.get('chatId')).toBe(chatId)
  await page.getByRole('button', { name: 'Load more', exact: true }).click()
  await expect(table.getByRole('listitem')).toHaveCount(2)
  await expect(table).toContainText('Unknown author')
  expect(queries[1].searchParams.get('continuationToken')).toBe('history+/=')
  expect(queries.map(url => url.searchParams.get('pageSize'))).toEqual(['100', '100'])
  await page.screenshot({ path: testInfo.outputPath('history-desktop.png'), fullPage: true })
  await page.setViewportSize({ width: 390, height: 844 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
  await page.screenshot({ path: testInfo.outputPath('history-mobile.png'), fullPage: true })
})

test('message history displays typed payload fields separately and handles image-only and empty payloads', async ({ page }) => {
  await page.route('**/api/review/history?*', route => route.fulfill({
    contentType: 'application/json',
    body: `{"messages":[
      {"chatId":${chatId},"messageId":7,"dateTimeTicks":${ticks},"content":{"from":{"id":${userId},"name":"Alice Smith","username":"alice"},"text":"Ignored legacy text","payload":{"userMessage":"Caption\\n<em>Not HTML</em>","imageDescription":"A sign\\n<strong>Not HTML</strong>","imageText":"Welcome\\n<script>Not HTML</script>"},"replyTo":{"messageId":9223372036854775806,"from":{"id":9223372036854775805,"name":"Bob Jones","username":null},"text":"Original question"}}},
      {"chatId":${chatId},"messageId":8,"dateTimeTicks":${ticks},"content":{"from":null,"replyTo":null,"payload":{"userMessage":null,"imageDescription":"Image-only description","imageText":"Image-only transcription"}}},
      {"chatId":${chatId},"messageId":9,"dateTimeTicks":${ticks},"content":{"from":null,"replyTo":null,"payload":{"imageText":"Transcription only"}}},
      {"chatId":${chatId},"messageId":10,"dateTimeTicks":${ticks},"content":{"from":null,"replyTo":null,"text":"Must not replace empty typed payload","payload":{}}},
      {"chatId":${chatId},"messageId":11,"dateTimeTicks":${ticks},"content":{"from":null,"replyTo":null,"payload":{"userMessage":null,"imageDescription":null,"imageText":null}}},
      {"chatId":${chatId},"messageId":12,"dateTimeTicks":${ticks},"content":{"from":null,"replyTo":null,"payload":{"userMessage":"","imageDescription":"","imageText":""}}}
    ],"continuationToken":null}`,
  }))
  await openWorkspace(page, { loadQueue: false })
  await openSection(page, 'Message history')
  await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
  await page.getByRole('button', { name: 'Load records' }).click()
  const table = page.getByRole('region', { name: 'Message history records' })
  const rows = table.getByRole('listitem')
  await expect(rows).toHaveCount(6)
  await expect(rows.first()).toContainText(userId)
  await expect(rows.first().locator(`[title="${ticks}"]`)).toContainText('1 Jan 2025, 00:00')
  await expect(rows.first().locator('summary')).toHaveText('Caption\n<em>Not HTML</em>')
  await rows.first().locator('summary').click()
  await expect(rows.first().getByRole('region', { name: 'User text', exact: true }).locator('p')).toHaveText('Caption\n<em>Not HTML</em>')
  await expect(rows.first().getByRole('region', { name: 'Image description' }).locator('p')).toHaveText('A sign\n<strong>Not HTML</strong>')
  await expect(rows.first().getByRole('region', { name: 'Image text', exact: true }).locator('p')).toHaveText('Welcome\n<script>Not HTML</script>')
  await expect(rows.first().locator('blockquote')).toHaveText('Bob Jones · #9223372036854775806Original question')
  await expect(table.locator('em, script, p strong')).toHaveCount(0)
  await expect(table).not.toContainText('Ignored legacy text')
  await expect(rows.nth(1).locator('summary')).toHaveText('Image-only description')
  await rows.nth(1).locator('summary').click()
  await expect(rows.nth(1).getByRole('region', { name: 'User text', exact: true })).toHaveCount(0)
  await expect(rows.nth(1).getByRole('region', { name: 'Image text', exact: true })).toContainText('Image-only transcription')
  await expect(rows.nth(2).locator('summary')).toHaveText('Transcription only')
  await rows.nth(2).locator('summary').click()
  await expect(rows.nth(2).getByRole('region', { name: 'Image description' })).toHaveCount(0)
  await expect(rows.nth(2).getByRole('region', { name: 'Image text', exact: true })).toContainText('Transcription only')
  for (const index of [3, 4, 5]) {
    await expect(rows.nth(index).locator('summary')).toHaveText('No text or caption')
    await rows.nth(index).locator('summary').click()
    await expect(rows.nth(index).getByRole('region')).toHaveCount(0)
  }
  await expect(table).not.toContainText('Must not replace empty typed payload')
})

test('message history distinguishes edited messages and channel posts with safe legacy update types', async ({ page }) => {
  const updateTypes = ['Message', 'EditedMessage', 'ChannelPost', 'EditedChannelPost', undefined, null, 'FutureUpdate', 42]
  await page.route('**/api/review/history?*', route => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({
      messages: updateTypes.map((updateType, index) => ({
        chatId, messageId: index + 1, dateTimeTicks: ticks, updateType,
        content: {
          from: null,
          ...(index === 4 ? { text: 'Legacy message' } : { payload: { userMessage: `Record ${index + 1}` } }),
          replyTo: index === 1 ? { messageId: 1, from: null, text: 'Original question' } : null,
        },
      })),
      continuationToken: null,
    }),
  }))
  await openWorkspace(page, { loadQueue: false })
  await openSection(page, 'Message history')
  await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
  await page.getByRole('button', { name: 'Load records' }).click()
  const table = page.getByRole('region', { name: 'Message history records' })
  await expect(table.locator('.history-update-type')).toHaveText([
    'Message', 'Edited message', 'Channel post', 'Edited channel post', 'Message', 'Message', 'Message', 'Message',
  ])
  await expect(table.locator('details[open]')).toHaveCount(0)
  const rows = table.getByRole('listitem')
  await expect(rows.nth(4).locator('summary')).toHaveText('Legacy message')
  await rows.nth(1).locator('summary').click()
  await expect(rows.nth(1).getByRole('region', { name: 'User text', exact: true })).toContainText('Record 2')
  await expect(rows.nth(1).locator('blockquote')).toHaveText('Unknown author · #1Original question')
  await page.getByRole('button', { name: 'Load records' }).click()
  await expect(table.getByRole('listitem')).toHaveCount(8)
  await expect(rows.nth(3).locator('.history-update-type')).toHaveText('Edited channel post')
})

test('record lists handle empty pages, errors and retry without mutating data', async ({ page }) => {
  let attempts = 0
  await page.route('**/api/review/command-calls?*', route => {
    attempts++
    expect(route.request().method()).toBe('GET')
    if (attempts === 2) return route.fulfill({ status: 500, contentType: 'application/json', body: '{"error":"Storage unavailable"}' })
    return route.fulfill({ contentType: 'application/json', body: '{"calls":[],"continuationToken":null}' })
  })
  await openWorkspace(page, { loadQueue: false })
  await openSection(page, 'Command calls')
  await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
  await page.getByRole('button', { name: 'Load records' }).click()
  await expect(page.getByRole('region', { name: 'Command calls records' })).toContainText('No records found.')
  await page.getByRole('button', { name: 'Load records' }).click()
  await expect(page.getByRole('alert')).toContainText('Storage unavailable')
  await page.getByRole('button', { name: 'Load records' }).click()
  await expect(page.getByRole('region', { name: 'Command calls records' })).toContainText('No records found.')
  await expect(page.getByRole('alert')).toHaveCount(0)
})
