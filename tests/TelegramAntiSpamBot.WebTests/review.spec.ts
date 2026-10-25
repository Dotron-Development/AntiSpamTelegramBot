import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { createServer } from 'node:http'
import type { AddressInfo } from 'node:net'

const chatId = '-1001234567890'
const userId = '9007199254740993'
const ticks = '638712864000000001'
const fakeKey = 'test-only-not-a-real-key'
const apiRoute = 'https://review.example.test/api/review/**'
const messageJSON = `{"ChatId":${chatId},"MessageId":7,"UserId":${userId},"UserName":"Alice Smith","Message":"Hello team!\\n<em>Not HTML</em>","ImageText":"Image text: special offer","ContentHash":"HASH","MessageDateTicks":${ticks}}`
const firstPage = `{"Messages":[${messageJSON}],"ContinuationToken":"next+/= page"}`
const lastPage = `{"Messages":[{"ChatId":${chatId},"MessageId":8,"UserId":43,"UserName":"Bob Jones","Message":"Another pending message","ImageText":"","ContentHash":"HASH","MessageDateTicks":${ticks}}],"ContinuationToken":null}`
const emptyPage = '{"Messages":[],"ContinuationToken":null}'
const chatPage = `{"Chats":[{"ChatId":${chatId},"Name":"Test group","LastSeenUtc":"2026-10-25T00:00:00+00:00"}],"ContinuationToken":null}`

async function openWorkspace(page: Page, { mockChats = true, loadQueue = true }: {
  mockChats?: boolean; loadQueue?: boolean
} = {}) {
  if (mockChats) await page.route('**/api/review/chats?*', route => {
    expect(route.request().headers()['x-functions-key']).toBe(fakeKey)
    return route.fulfill({ contentType: 'application/json', body: chatPage })
  })
  await page.goto('/')
  await expect(page.getByRole('button', { name: 'Connect Function', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Connection', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Disconnect and clear key', exact: true })).toHaveCount(0)
  await expect(page.getByLabel('Function key', { exact: false })).toHaveCount(0)
  if (loadQueue) {
    await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
    await page.getByRole('button', { name: 'Load queue' }).click()
  }
}

test('automatically connects using environment settings, paginates, and does not store the key', async ({ page }) => {
  const queries: string[] = []
  await page.route(apiRoute, async route => {
    expect(route.request().headers()['x-functions-key']).toBe(fakeKey)
    expect(route.request().headers()['x-review-endpoint']).toBeUndefined()
    queries.push(route.request().url())
    await route.fulfill({ contentType: 'application/json', body: queries.length === 1 ? firstPage : lastPage })
  })
  await openWorkspace(page)
  await expect(page.getByRole('heading', { name: 'Alice Smith' })).toBeVisible()
  await expect(page.locator('.metadata')).toContainText(userId)
  await expect(page.locator(`[title="${ticks}"]`)).toContainText('1 Jan 2025, 00:00')
  await expect(page.locator('.message-text').first()).toHaveText('Hello team!\n<em>Not HTML</em>')
  await expect(page.locator('.message-text em')).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'Image transcription OCR' })).toBeVisible()
  await page.getByRole('button', { name: 'Load more', exact: true }).click()
  await expect(page.locator('.message-row')).toHaveCount(2)
  expect(new URL(queries[1]).searchParams.get('continuationToken')).toBe('next+/= page')
  expect(new URL(queries[0]).searchParams.get('pageSize')).toBe('50')
  expect(new URL(queries[0]).origin).toBe('https://review.example.test')
  expect(new URL(queries[0]).pathname).toBe('/api/review/messages')
  const storage = await page.evaluate(() => ({ local: { ...localStorage }, session: { ...sessionStorage } }))
  expect(JSON.stringify(storage)).not.toContain(fakeKey)
  expect(JSON.stringify(storage)).toContain(chatId)
  await page.reload()
  await expect(page.getByLabel('Chat', { exact: true })).toHaveValue(chatId)
  await expect(page.getByLabel('Chat', { exact: true })).toBeEnabled()
  await page.getByRole('button', { name: 'Load queue' }).click()
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
  await expect(page.locator('.metadata')).toContainText(userId)
  await expect(page.locator(`[title="${ticks}"]`)).toContainText('1 Jan 2025, 00:00')
  await page.getByRole('button', { name: 'Load more', exact: true }).click()
  await expect(page.locator('.message-row')).toHaveCount(2)
  await expect(page.getByRole('button', { name: 'Load more', exact: true })).toHaveCount(0)
  expect(chatTokens).toEqual([null, 'chat+/='])
  expect(messageTokens).toEqual([null, 'next+/= page'])
})

test('not-spam returns a decision, then restrictions can be removed independently', async ({ page }) => {
  const mutations: { path: string; body: string | null }[] = []
  await page.route(apiRoute, async route => {
    if (route.request().method() === 'GET') {
      await route.fulfill({ contentType: 'application/json', body: firstPage })
    } else {
      mutations.push({ path: new URL(route.request().url()).pathname, body: route.request().postData() })
      await route.fulfill({ status: 204 })
    }
  })
  await openWorkspace(page)
  await page.getByRole('button', { name: 'Not spam', exact: true }).click()
  expect(mutations).toHaveLength(0)
  await page.getByRole('button', { name: 'Confirm not spam', exact: true }).click()
  await expect(page.locator('.message-row')).toHaveCount(0)
  await expect(page.getByRole('status')).toContainText('Saved text returned to chat')
  expect(mutations).toEqual([{ path: `/api/review/messages/${chatId}/7`, body: '{"isSpam":false}' }])
  await page.getByRole('region', { name: 'Last review' }).getByRole('button', { name: 'Remove restrictions' }).click()
  await expect(page.getByRole('dialog').getByLabel('User ID', { exact: true })).toHaveValue(userId)
  await page.getByRole('dialog').getByRole('button', { name: 'Remove restrictions', exact: true }).click()
  await expect(page.getByRole('status')).toContainText(`Restrictions removed for user ${userId}`)
  expect(mutations[1].path).toBe(`/api/review/restrictions/${chatId}/${userId}`)
  expect(mutations[1].body).toBeFalsy()
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
  await expect(page.getByRole('heading', { name: 'All clear for now' })).toBeVisible()
  expect(bodies).toEqual([{ isSpam: true }])
})

test('handles auth errors for both filtered and all-chat queues', async ({ page }) => {
  const queries: URL[] = []
  await page.route(apiRoute, route => {
    queries.push(new URL(route.request().url()))
    return route.fulfill({ status: 401, body: '' })
  })
  await openWorkspace(page)
  await expect(page.getByRole('alert')).toContainText('Access denied. Check your Function key.')
  await page.getByLabel('Chat', { exact: true }).selectOption('')
  await page.getByRole('button', { name: 'Load queue' }).click()
  await expect.poll(() => queries.length).toBe(2)
  await expect(page.getByRole('alert')).toContainText('Access denied. Check your Function key.')
  expect(queries[0].searchParams.get('chatId')).toBe(chatId)
  expect(queries[1].searchParams.has('chatId')).toBe(false)
})

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
  await expect(page.getByRole('heading', { name: 'More messages available' })).toBeVisible()
  await page.getByRole('button', { name: 'Load more' }).click()
  await expect(page.getByRole('heading', { name: 'All clear for now' })).toBeVisible()
})

test('renders desktop and mobile without horizontal overflow or clipped action labels', async ({ page }, testInfo) => {
  await page.route(apiRoute, route => route.fulfill({ contentType: 'application/json', body: firstPage }))
  await openWorkspace(page)
  await expect(page.getByRole('heading', { name: 'Alice Smith' })).toBeVisible()
  for (const width of [1440, 768, 375, 320]) {
    await page.setViewportSize({ width, height: 900 })
    await page.evaluate(() => document.fonts.ready)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    expect(await page.locator('.button').evaluateAll(buttons => buttons.filter(button =>
      button.getBoundingClientRect().width > 0 && button.scrollWidth > button.clientWidth + 1,
    ).map(button => button.textContent))).toEqual([])
    await page.screenshot({ path: testInfo.outputPath(`review-${width}.png`), fullPage: true })
  }
  expect(await page.evaluate(() => [...document.fonts].some(font => font.family.includes('Manrope') && font.status === 'loaded'))).toBe(true)
})

test('browser calls a separate Function origin directly with CORS preflight', async ({ page }) => {
  const received: { url: string; key: string | undefined; method: string; body: string; origin: string | undefined }[] = []
  const preflights: string[] = []
  const server = createServer(async (incoming, response) => {
    response.setHeader('Access-Control-Allow-Origin', 'http://127.0.0.1:5179')
    response.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
    response.setHeader('Access-Control-Allow-Headers', 'x-functions-key, content-type')
    if (incoming.method === 'OPTIONS') {
      preflights.push(incoming.headers['access-control-request-headers'] ?? '')
      response.writeHead(204)
      response.end()
      return
    }
    let body = ''
    for await (const chunk of incoming) body += chunk
    received.push({ url: incoming.url!, key: incoming.headers['x-functions-key'] as string | undefined,
      method: incoming.method!, body, origin: incoming.headers.origin })
    response.writeHead(incoming.method === 'GET' ? 200 : 204, { 'Content-Type': 'application/json' })
    response.end(incoming.method === 'GET' ? incoming.url?.startsWith('/api/review/chats?') ? chatPage : firstPage : undefined)
  })
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve))
  const endpoint = `http://127.0.0.1:${(server.address() as AddressInfo).port}/api`
  try {
    await openWorkspace(page, { loadQueue: false })
    await expect(page.getByLabel('Chat', { exact: true })).toBeEnabled()
    await page.unroute('**/api/review/chats?*')
    await page.evaluate(async ({ endpoint, key, chatId, userId }) => {
      const modulePath = '/src/api/client.ts'
      const api = await import(modulePath)
      const connection = { endpoint, key }
      await api.getChats(connection, null)
      const result = await api.getMessages(connection, chatId, 50, null)
      await api.reviewMessage(connection, result.messages[0], false)
      await api.removeRestrictions(connection, chatId, userId)
    }, { endpoint, key: fakeKey, chatId, userId })
    const origin = 'http://127.0.0.1:5179'
    expect(received).toEqual([
      { url: '/api/review/chats?pageSize=1000', key: fakeKey, method: 'GET', body: '', origin },
      { url: `/api/review/messages?chatId=${chatId}&pageSize=50`, key: fakeKey, method: 'GET', body: '', origin },
      { url: `/api/review/messages/${chatId}/7`, key: fakeKey, method: 'POST', body: '{"isSpam":false}', origin },
      { url: `/api/review/restrictions/${chatId}/${userId}`, key: fakeKey, method: 'POST', body: '', origin },
    ])
    expect(preflights.some(headers => headers.includes('x-functions-key'))).toBe(true)
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
  await expect(page.getByRole('heading', { name: 'All clear for now' })).toBeVisible()
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
  await page.getByRole('button', { name: 'Refresh chats', exact: true }).click()
  await expect.poll(() => tokens.length).toBe(3)
  await expect(page.getByLabel('Chat', { exact: true })).toBeEnabled()
})

test('empty catalog and catalog errors do not automatically load message queues', async ({ page }) => {
  let calls = 0
  await page.route(apiRoute, route => {
    expect(new URL(route.request().url()).pathname).toBe('/api/review/chats')
    return ++calls === 1 ? route.fulfill({ contentType: 'application/json', body: '{"Chats":[],"ContinuationToken":null}' })
      : route.fulfill({ status: 401, body: '' })
  })
  await openWorkspace(page, { mockChats: false, loadQueue: false })
  await expect(page.getByLabel('Chat', { exact: true })).toBeEnabled()
  await expect(page.getByLabel('Chat', { exact: true })).toHaveValue('')
  await page.getByRole('button', { name: 'Refresh chats', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Access denied. Check your Function key.')
  expect(calls).toBe(2)
})

test('restriction form validates IDs without requests and preserves trimmed 64-bit values', async ({ page }) => {
  const mutations: string[] = []
  await page.route(apiRoute, route => {
    if (route.request().method() === 'GET') return route.fulfill({ contentType: 'application/json', body: firstPage })
    mutations.push(new URL(route.request().url()).pathname)
    return route.fulfill({ status: 204 })
  })
  await openWorkspace(page)
  await expect(page.getByRole('heading', { name: 'Alice Smith' })).toBeVisible()
  await page.locator('.page-heading').getByRole('button', { name: 'Remove restrictions' }).click()
  const dialog = page.getByRole('dialog')
  const chat = dialog.getByLabel('Chat ID', { exact: true })
  const user = dialog.getByLabel('User ID', { exact: true })
  const submit = dialog.getByRole('button', { name: 'Remove restrictions', exact: true })
  await chat.fill('0')
  await user.fill('')
  await submit.click()
  await expect(chat).toHaveAttribute('aria-invalid', 'true')
  await expect(user).toHaveAttribute('aria-invalid', 'true')
  await expect(dialog.getByRole('alert')).toHaveCount(2)
  expect(mutations).toEqual([])
  await chat.fill(` ${chatId} `)
  for (const invalid of ['-1', '9223372036854775808', '1.5']) {
    await user.fill(invalid)
    await submit.click()
    await expect(user).toHaveAttribute('aria-invalid', 'true')
    expect(mutations).toEqual([])
  }
  await user.fill(` ${userId} `)
  await submit.click()
  await expect(dialog).toHaveCount(0)
  expect(mutations).toEqual([`/api/review/restrictions/${chatId}/${userId}`])
})

test('reopening restrictions clears edited defaults and field/API errors', async ({ page }) => {
  let mutations = 0
  await page.route(apiRoute, route => {
    if (route.request().method() === 'GET') return route.fulfill({ contentType: 'application/json', body: firstPage })
    mutations++
    return route.fulfill({ status: 500 })
  })
  await openWorkspace(page)
  await expect(page.getByRole('heading', { name: 'Alice Smith' })).toBeVisible()
  const open = page.locator('.page-heading').getByRole('button', { name: 'Remove restrictions' })
  const dialog = page.getByRole('dialog')
  await open.click()
  await dialog.getByLabel('User ID', { exact: true }).fill('44')
  await dialog.getByRole('button', { name: 'Remove restrictions', exact: true }).click()
  await expect(dialog.getByRole('alert')).toContainText('Request failed')
  expect(mutations).toBe(1)
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
  await open.click()
  await expect(dialog.getByLabel('Chat ID', { exact: true })).toHaveValue(chatId)
  await expect(dialog.getByLabel('User ID', { exact: true })).toHaveValue(userId)
  await expect(dialog.getByRole('alert')).toHaveCount(0)
  await dialog.getByLabel('User ID', { exact: true }).fill('0')
  await dialog.getByRole('button', { name: 'Remove restrictions', exact: true }).click()
  await expect(dialog.getByRole('alert')).toContainText('positive user ID')
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
  await open.click()
  await expect(dialog.getByLabel('User ID', { exact: true })).toHaveValue(userId)
  await expect(dialog.getByRole('alert')).toHaveCount(0)
  expect(mutations).toBe(1)
})

test('restriction submission locks the form and cannot be cancelled or submitted twice', async ({ page }) => {
  let mutations = 0
  let complete!: () => void
  const pending = new Promise<void>(resolve => { complete = resolve })
  await page.route(apiRoute, async route => {
    if (route.request().method() === 'GET') return route.fulfill({ contentType: 'application/json', body: firstPage })
    mutations++
    await pending
    return route.fulfill({ status: 204 })
  })
  await openWorkspace(page)
  await expect(page.getByRole('heading', { name: 'Alice Smith' })).toBeVisible()
  await page.locator('.page-heading').getByRole('button', { name: 'Remove restrictions' }).click()
  const dialog = page.getByRole('dialog')
  try {
    await dialog.getByRole('button', { name: 'Remove restrictions', exact: true }).click()
    await expect.poll(() => mutations).toBe(1)
    await expect(dialog.getByLabel('User ID', { exact: true })).toBeDisabled()
    await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeDisabled()
    await expect(dialog.getByRole('button', { name: 'Close dialog', exact: true })).toBeDisabled()
    await page.keyboard.press('Escape')
    await expect(dialog).toBeVisible()
    await dialog.locator('form').evaluate(form => {
      (form as HTMLFormElement).requestSubmit()
      ;(form as HTMLFormElement).requestSubmit()
    })
  } finally { complete() }
  await expect(dialog).toHaveCount(0)
  expect(mutations).toBe(1)
})

test('pagination uses the submitted page size and keeps the selected message', async ({ page }) => {
  const sizes: string[] = []
  await page.route(apiRoute, route => {
    sizes.push(new URL(route.request().url()).searchParams.get('pageSize')!)
    return route.fulfill({ contentType: 'application/json', body: sizes.length === 1 ? firstPage : lastPage })
  })
  await openWorkspace(page, { loadQueue: false })
  await expect(page.getByLabel('Chat', { exact: true })).toBeEnabled()
  await page.getByLabel('Page size').selectOption('25')
  await page.getByRole('button', { name: 'Load queue' }).click()
  await expect(page.getByRole('heading', { name: 'Alice Smith' })).toBeVisible()
  await page.getByLabel('Page size').selectOption('100')
  await page.getByRole('button', { name: 'Load more', exact: true }).click()
  await expect(page.locator('.message-row')).toHaveCount(2)
  await expect(page.getByRole('heading', { name: 'Alice Smith' })).toBeVisible()
  expect(sizes).toEqual(['25', '25'])
  await page.getByRole('button', { name: /Bob Jones/ }).click()
  await expect(page.getByRole('heading', { name: 'Bob Jones' })).toBeVisible()
  await page.getByRole('button', { name: 'Refresh queue', exact: true }).click()
  await expect(page.locator('.message-row')).toHaveCount(1)
  expect(sizes).toEqual(['25', '25', '25'])
})

test('all-chat queue is loaded by button, paginates and distinguishes identical message IDs', async ({ page }) => {
  const otherChat = '-1009876543210'
  const thirdChat = '-1005555555555'
  const bob = messageJSON.replace(chatId, otherChat).replace('Alice Smith', 'Bob Jones')
  const carol = messageJSON.replace(chatId, thirdChat).replace('Alice Smith', 'Carol Adams')
  const queries: URL[] = []
  const mutations: string[] = []
  await page.route(apiRoute, route => {
    const request = route.request()
    if (request.method() === 'POST') {
      mutations.push(new URL(request.url()).pathname)
      return route.fulfill({ status: 204 })
    }
    queries.push(new URL(request.url()))
    const body = queries.length === 1
      ? `{"Messages":[${messageJSON},${bob}],"ContinuationToken":"global+/="}`
      : `{"Messages":[${messageJSON},${carol}],"ContinuationToken":null}`
    return route.fulfill({ contentType: 'application/json', body })
  })
  await openWorkspace(page, { loadQueue: false })
  await expect(page.getByLabel('Chat', { exact: true })).toBeEnabled()
  await expect(page.getByLabel('Chat', { exact: true })).toHaveValue('')
  expect(queries).toEqual([])
  await page.getByLabel('Page size').selectOption('25')
  await page.getByRole('button', { name: 'Load queue' }).click()
  await expect(page.locator('.message-row')).toHaveCount(2)
  await expect(page.locator('.queue-context')).toHaveText('All chats')
  await page.getByRole('button', { name: /Bob Jones/ }).click()
  await expect(page.getByRole('heading', { name: 'Bob Jones' })).toBeVisible()
  await expect(page.locator('.message-row[aria-pressed="true"]')).toHaveCount(1)
  await expect(page.locator('.metadata')).toContainText(otherChat)
  await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
  await page.getByLabel('Page size').selectOption('100')
  await page.getByRole('button', { name: 'Load more', exact: true }).click()
  await expect(page.locator('.message-row')).toHaveCount(3)
  await expect(page.locator('.message-author')).toHaveText(['Alice Smith', 'Bob Jones', 'Carol Adams'])
  await expect(page.getByRole('heading', { name: 'Bob Jones' })).toBeVisible()
  expect(queries.map(url => url.searchParams.has('chatId'))).toEqual([false, false])
  expect(queries.map(url => url.searchParams.get('pageSize'))).toEqual(['25', '25'])
  expect(queries[1].searchParams.get('continuationToken')).toBe('global+/=')
  await page.getByRole('button', { name: 'Spam', exact: true }).click()
  await page.getByRole('button', { name: 'Confirm spam', exact: true }).click()
  await expect(page.locator('.message-row')).toHaveCount(2)
  await expect(page.locator('.message-author')).toHaveText(['Alice Smith', 'Carol Adams'])
  expect(mutations).toEqual([`/api/review/messages/${otherChat}/7`])
  await page.getByRole('region', { name: 'Last review' }).getByRole('button', { name: 'Remove restrictions' }).click()
  await expect(page.getByRole('dialog').getByLabel('Chat ID', { exact: true })).toHaveValue(otherChat)
  await page.getByRole('dialog').getByRole('button', { name: 'Remove restrictions', exact: true }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(mutations[1]).toBe(`/api/review/restrictions/${otherChat}/${userId}`)
  await page.getByRole('button', { name: 'Refresh queue', exact: true }).click()
  await expect.poll(() => queries.length).toBe(3)
  await expect(page.getByRole('button', { name: 'Load queue' })).toBeEnabled()
  expect(queries[2].searchParams.has('chatId')).toBe(false)
  await page.getByRole('button', { name: 'Load queue' }).click()
  await expect.poll(() => queries.length).toBe(4)
  await expect(page.getByRole('button', { name: 'Load queue' })).toBeEnabled()
  expect(queries[3].searchParams.get('chatId')).toBe(chatId)
  expect(queries[3].searchParams.get('pageSize')).toBe('100')
  await page.getByLabel('Chat', { exact: true }).selectOption('')
  await page.getByRole('button', { name: 'Load queue' }).click()
  await expect.poll(() => queries.length).toBe(5)
  await expect(page.getByRole('button', { name: 'Load queue' })).toBeEnabled()
  await page.reload()
  await expect(page.getByLabel('Chat', { exact: true })).toBeEnabled()
  await expect(page.getByLabel('Chat', { exact: true })).toHaveValue('')
  expect(queries).toHaveLength(5)
})

for (const catalogFailure of [false, true]) {
  test(`all-chat queue works with ${catalogFailure ? 'a failed' : 'an empty'} chat catalog`, async ({ page }) => {
    let messages = 0
    await page.route(apiRoute, route => {
      if (new URL(route.request().url()).pathname.endsWith('/chats')) {
        return catalogFailure ? route.fulfill({ status: 500 })
          : route.fulfill({ contentType: 'application/json', body: '{"Chats":[],"ContinuationToken":null}' })
      }
      messages++
      expect(new URL(route.request().url()).searchParams.has('chatId')).toBe(false)
      return route.fulfill({ contentType: 'application/json', body: emptyPage })
    })
    await openWorkspace(page, { mockChats: false, loadQueue: false })
    await expect(page.getByRole('button', { name: 'Load queue' })).toBeEnabled()
    expect(messages).toBe(0)
    await page.getByRole('button', { name: 'Load queue' }).click()
    await expect(page.getByRole('heading', { name: 'All clear for now' })).toBeVisible()
    await expect(page.locator('.detail-empty')).toContainText('All chats')
    expect(messages).toBe(1)
    await page.locator('.page-heading').getByRole('button', { name: 'Remove restrictions' }).click()
    await expect(page.getByRole('dialog').getByLabel('Chat ID', { exact: true })).toHaveValue('')
  })
}