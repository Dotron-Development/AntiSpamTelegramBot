import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { createServer } from 'node:http'
import type { AddressInfo } from 'node:net'

const chatId = '-1001234567890'
const userId = '9007199254740993'
const ticks = '638712864000000001'
const fakeToken = 'test-only-not-a-real-token'
const apiRoute = 'https://review.example.test/api/review/**'
const messageJSON = `{"ChatId":${chatId},"MessageId":7,"UserId":${userId},"UserName":"Alice Smith","Message":"Hello team!\\n<em>Not HTML</em>","ImageText":"Image text: special offer","ContentHash":"HASH","MessageDateTicks":${ticks}}`
const firstPage = `{"Messages":[${messageJSON}],"ContinuationToken":"next+/= page"}`
const lastPage = `{"Messages":[{"ChatId":${chatId},"MessageId":8,"UserId":43,"UserName":"Bob Jones","Message":"Another pending message","ImageText":"","ContentHash":"HASH","MessageDateTicks":${ticks}}],"ContinuationToken":null}`
const emptyPage = '{"Messages":[],"ContinuationToken":null}'
const chatPage = `{"Chats":[{"ChatId":${chatId},"Name":"Test group","LastSeenUtc":"2026-10-25T00:00:00+00:00"}],"ContinuationToken":null}`

async function mockAuthentication(page: Page, roles: string[] | null = ['Moderator'], error = false) {
  const account = roles === null ? null : { name: 'Test moderator', idTokenClaims: { roles } }
  await page.route('**/src/lib/auth.ts*', route => route.fulfill({
    contentType: 'application/javascript',
    body: `
      export const configurationError = null;
      export async function initializeAuth() { ${error ? 'throw new Error("Sign-in failed");' : ''} }
      export function currentAccount() { return ${JSON.stringify(account)}; }
      export async function signIn() { window.location.assign('https://login.example.test/authorize'); }
      export async function signOut() {}
      export async function getAccessToken() { return ${JSON.stringify(fakeToken)}; }
    `,
  }))
}

test.beforeEach(async ({ page }) => { await mockAuthentication(page) })

test('redirects automatically to sign-in without mounting the workspace', async ({ page }) => {
  await mockAuthentication(page, null)
  let apiCalls = 0
  let redirects = 0
  await page.route(apiRoute, route => { apiCalls++; return route.abort() })
  await page.route('https://login.example.test/authorize', route => {
    redirects++
    return route.fulfill({ contentType: 'text/html', body: '<h1>Microsoft sign-in</h1>' })
  })
  await page.goto('/')
  await expect(page).toHaveURL('https://login.example.test/authorize')
  expect(redirects).toBe(1)
  expect(apiCalls).toBe(0)
})

test('keeps the workspace closed without Moderator or after a sign-in error', async ({ page }) => {
  let apiCalls = 0
  await page.route(apiRoute, route => { apiCalls++; return route.abort() })
  await mockAuthentication(page, [])
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Access denied' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Load queue' })).toHaveCount(0)
  await mockAuthentication(page, null, true)
  await page.reload()
  await expect(page.getByRole('alert')).toContainText('Sign-in failed')
  await expect(page.getByRole('button', { name: 'Sign in with Microsoft' })).toHaveCount(0)
  expect(apiCalls).toBe(0)
})

async function openWorkspace(page: Page, { mockChats = true, loadQueue = true }: {
  mockChats?: boolean; loadQueue?: boolean
} = {}) {
  if (mockChats) await page.route('**/api/review/chats?*', route => {
    expect(route.request().headers().authorization).toBe(`Bearer ${fakeToken}`)
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
    await page.getByRole('button', { name: 'Load queue' }).click()
  }
}

test('connects after authentication, paginates, and does not persist access tokens in workspace storage', async ({ page }) => {
  const queries: string[] = []
  await page.route(apiRoute, async route => {
    expect(route.request().headers().authorization).toBe(`Bearer ${fakeToken}`)
    expect(route.request().headers()['x-functions-key']).toBeUndefined()
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
  expect(JSON.stringify(storage)).not.toContain(fakeToken)
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
  await expect(page.getByRole('alert')).toContainText('Your session is invalid or expired.')
  await page.getByLabel('Chat', { exact: true }).selectOption('')
  await page.getByRole('button', { name: 'Load queue' }).click()
  await expect.poll(() => queries.length).toBe(2)
  await expect(page.getByRole('alert')).toContainText('Your session is invalid or expired.')
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
    response.writeHead(incoming.method === 'GET' ? 200 : 204, { 'Content-Type': 'application/json' })
    response.end(incoming.method === 'GET' ? incoming.url?.startsWith('/api/review/chats?') ? chatPage : firstPage : undefined)
  })
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve))
  const endpoint = `http://127.0.0.1:${(server.address() as AddressInfo).port}/api`
  try {
    await openWorkspace(page, { loadQueue: false })
    await expect(page.getByLabel('Chat', { exact: true })).toBeEnabled()
    await page.unroute('**/api/review/chats?*')
    await page.unroute('**/src/lib/auth.ts*')
    await page.evaluate(async ({ endpoint, token, chatId, userId }) => {
      const modulePath = '/src/api/client.ts'
      const api = await import(modulePath)
      const connection = { endpoint, getAccessToken: async () => token }
      await api.getChats(connection, null)
      const result = await api.getMessages(connection, chatId, 50, null)
      await api.reviewMessage(connection, result.messages[0], false)
      await api.removeRestrictions(connection, chatId, userId)
    }, { endpoint, token: fakeToken, chatId, userId })
    const origin = 'http://127.0.0.1:5179'
    expect(received).toEqual([
      { url: '/api/review/chats?pageSize=1000', key: `Bearer ${fakeToken}`, method: 'GET', body: '', origin },
      { url: `/api/review/messages?chatId=${chatId}&pageSize=50`, key: `Bearer ${fakeToken}`, method: 'GET', body: '', origin },
      { url: `/api/review/messages/${chatId}/7`, key: `Bearer ${fakeToken}`, method: 'POST', body: '{"isSpam":false}', origin },
      { url: `/api/review/restrictions/${chatId}/${userId}`, key: `Bearer ${fakeToken}`, method: 'POST', body: '', origin },
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
  await expect(page.getByRole('alert')).toContainText('Your session is invalid or expired.')
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

test('command calls tab filters, paginates, refreshes and stays read-only', async ({ page }, testInfo) => {
  const queries: URL[] = []
  await page.route('**/api/review/command-calls?*', route => {
    expect(route.request().method()).toBe('GET')
    expect(route.request().headers().authorization).toBe(`Bearer ${fakeToken}`)
    const url = new URL(route.request().url())
    queries.push(url)
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify({
      calls: [{ chatId, command: url.searchParams.has('continuationToken') ? '/stat' : '/chronicle', lastCall: '2026-10-31T12:00:00Z' }],
      continuationToken: url.searchParams.has('continuationToken') ? null : 'next+/= page',
    }) })
  })
  await openWorkspace(page, { loadQueue: false })
  await page.getByRole('tab', { name: 'Command calls', exact: true }).click()
  await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
  await page.getByRole('button', { name: 'Load records' }).click()
  const table = page.getByRole('region', { name: 'Command calls table' })
  await expect(table.locator('tbody tr')).toHaveCount(1)
  await expect(table).toContainText('/chronicle')
  await expect(table).toContainText('31 Oct 2026')
  expect(queries[0].searchParams.get('chatId')).toBe(chatId)
  expect(queries[0].searchParams.get('pageSize')).toBe('50')
  await page.getByRole('button', { name: 'Load more', exact: true }).click()
  await expect(table.locator('tbody tr')).toHaveCount(2)
  expect(queries[1].searchParams.get('continuationToken')).toBe('next+/= page')
  await page.getByRole('button', { name: 'Refresh records' }).click()
  await expect(table.locator('tbody tr')).toHaveCount(1)
  expect(queries[2].searchParams.get('continuationToken')).toBeNull()
  await page.screenshot({ path: testInfo.outputPath('calls-desktop.png'), fullPage: true })
  await page.setViewportSize({ width: 390, height: 844 })
  await expect(page.getByRole('tab', { name: 'Message history' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
  await page.screenshot({ path: testInfo.outputPath('calls-mobile.png'), fullPage: true })
  await page.getByRole('tab', { name: 'Review queue', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Load queue', exact: true })).toBeVisible()
})

test('message history preserves numeric precision, plain text and reply context', async ({ page }, testInfo) => {
  const queries: URL[] = []
  await page.route('**/api/review/history?*', route => {
    const url = new URL(route.request().url())
    queries.push(url)
    expect(route.request().headers().authorization).toBe(`Bearer ${fakeToken}`)
    const body = url.searchParams.has('continuationToken')
      ? `{"messages":[{"chatId":${chatId},"messageId":8,"dateTimeTicks":${ticks},"content":{"from":null,"replyTo":null,"text":""}}],"continuationToken":null}`
      : `{"messages":[{"chatId":${chatId},"messageId":7,"dateTimeTicks":${ticks},"content":{"from":{"id":${userId},"name":"Alice Smith","username":"alice"},"text":"Hello team!\\n<em>Not HTML</em>","replyTo":{"messageId":6,"from":{"id":43,"name":"Bob Jones","username":null},"text":"Original question"}}}],"continuationToken":"history+/="}`
    return route.fulfill({ contentType: 'application/json', body })
  })
  await openWorkspace(page, { loadQueue: false })
  await page.getByRole('tab', { name: 'Message history', exact: true }).click()
  await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
  await page.getByRole('button', { name: 'Load records' }).click()
  const table = page.getByRole('region', { name: 'Message history table' })
  await expect(table).toContainText(userId)
  await expect(table.locator(`[title="${ticks}"]`)).toContainText('1 Jan 2025, 00:00')
  await table.locator('summary').first().click()
  await expect(table).toContainText('Original question')
  await expect(table).toContainText('Bob Jones')
  await expect(table.locator('em')).toHaveCount(0)
  await expect(table.locator('.history-preview')).toHaveText('Hello team!\n<em>Not HTML</em>')
  expect(queries[0].searchParams.get('chatId')).toBe(chatId)
  await page.getByRole('button', { name: 'Load more', exact: true }).click()
  await expect(table.locator('tbody tr')).toHaveCount(2)
  await expect(table).toContainText('Unknown author')
  expect(queries[1].searchParams.get('continuationToken')).toBe('history+/=')
  await page.screenshot({ path: testInfo.outputPath('history-desktop.png'), fullPage: true })
  await page.setViewportSize({ width: 390, height: 844 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
  await page.screenshot({ path: testInfo.outputPath('history-mobile.png'), fullPage: true })
})

test('table views handle empty pages, errors and retry without mutating data', async ({ page }) => {
  let attempts = 0
  await page.route('**/api/review/command-calls?*', route => {
    attempts++
    expect(route.request().method()).toBe('GET')
    if (attempts === 2) return route.fulfill({ status: 500, contentType: 'application/json', body: '{"error":"Storage unavailable"}' })
    return route.fulfill({ contentType: 'application/json', body: '{"calls":[],"continuationToken":null}' })
  })
  await openWorkspace(page, { loadQueue: false })
  await page.getByRole('tab', { name: 'Command calls', exact: true }).click()
  await page.getByRole('button', { name: 'Load records' }).click()
  await expect(page.getByRole('region', { name: 'Command calls table' })).toContainText('No records found.')
  await page.getByRole('button', { name: 'Refresh records' }).click()
  await expect(page.getByRole('alert')).toContainText('Storage unavailable')
  await page.getByRole('button', { name: 'Load records' }).click()
  await expect(page.getByRole('region', { name: 'Command calls table' })).toContainText('No records found.')
  await expect(page.getByRole('alert')).toHaveCount(0)
})

test('Chronicle preview is available for a selected history chat and renders without Telegram posting', async ({ page }, testInfo) => {
  let posts = 0
  let release!: () => void
  const gate = new Promise<void>(resolve => { release = resolve })
  const summary = 'Alice raised the question. Bob answered.\nAI aside: apparently the future still needs a meeting. <b>Plain text</b>'
  await page.route('**/api/review/chronicle/*', async route => {
    posts++
    expect(route.request().method()).toBe('POST')
    expect(new URL(route.request().url()).pathname).toBe(`/api/review/chronicle/${chatId}`)
    expect(route.request().headers().authorization).toBe(`Bearer ${fakeToken}`)
    await gate
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ chatId, summary, skipped: false }) })
  })
  await openWorkspace(page, { loadQueue: false })
  await expect(page.getByRole('button', { name: 'Test Chronicle' })).toHaveCount(0)
  await page.getByRole('tab', { name: 'Message history', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Test Chronicle' })).toHaveCount(0)
  await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
  await page.getByRole('button', { name: 'Test Chronicle' }).click()
  await expect(page.getByRole('button', { name: 'Generating...' })).toBeDisabled()
  await expect(page.getByLabel('Chat', { exact: true })).toBeDisabled()
  release()
  const preview = page.getByRole('region', { name: 'Chronicle preview' })
  await expect(preview).toContainText(summary)
  await expect(preview.locator('b')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Copy Chronicle' })).toBeVisible()
  expect(posts).toBe(1)
  await page.screenshot({ path: testInfo.outputPath('preview-desktop.png'), fullPage: true })
  await page.setViewportSize({ width: 390, height: 844 })
  await expect(page.getByRole('button', { name: 'Test Chronicle' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
  await page.screenshot({ path: testInfo.outputPath('preview-mobile.png'), fullPage: true })
  await page.getByLabel('Chat', { exact: true }).selectOption('')
  await expect(preview).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Test Chronicle' })).toHaveCount(0)
})

test('Chronicle preview handles failure, cooldown and an explicit retry', async ({ page }) => {
  let attempts = 0
  await page.route('**/api/review/chronicle/*', route => {
    attempts++
    if (attempts === 1) return route.fulfill({ status: 502, contentType: 'application/json', body: '{"error":"AI unavailable"}' })
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify({
      chatId, summary: attempts === 2 ? null : 'A fresh recap.', skipped: attempts === 2,
    }) })
  })
  await openWorkspace(page, { loadQueue: false })
  await page.getByRole('tab', { name: 'Message history', exact: true }).click()
  await page.getByLabel('Chat', { exact: true }).selectOption(chatId)
  await page.getByRole('button', { name: 'Test Chronicle' }).click()
  await expect(page.getByRole('alert')).toContainText('AI unavailable')
  expect(attempts).toBe(1)
  await page.getByRole('button', { name: 'Test Chronicle' }).click()
  await expect(page.getByRole('status')).toContainText('Chronicle is on cooldown')
  await expect(page.getByRole('alert')).toHaveCount(0)
  await expect(page.getByRole('region', { name: 'Chronicle preview' })).toHaveCount(0)
  await page.getByRole('button', { name: 'Test Chronicle' }).click()
  await expect(page.getByRole('region', { name: 'Chronicle preview' })).toContainText('A fresh recap.')
  expect(attempts).toBe(3)
})