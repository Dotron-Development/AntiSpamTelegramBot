import { parse } from 'lossless-json'

export interface Connection {
  endpoint: string
  key: string
}

export interface ReviewMessage {
  chatId: string
  messageId: string
  userId: string
  userName: string
  message: string
  imageText: string
  messageDateTicks: string
}

export interface ReviewPage {
  messages: ReviewMessage[]
  continuationToken: string | null
}

export interface ReviewChat {
  chatId: string
  name: string
  lastSeenUtc: string
}

export interface ReviewChatPage {
  chats: ReviewChat[]
  continuationToken: string | null
}

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

export function normalizeEndpoint(value: string): string {
  const url = new URL(value.trim())
  const local = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)
  if ((url.protocol !== 'https:' && !(url.protocol === 'http:' && local)) ||
      url.username || url.password || url.search || url.hash) {
    throw new Error('Use an HTTPS Function URL, or HTTP for localhost. Remove query parameters and credentials.')
  }
  return url.toString().replace(/\/$/, '')
}

export function validId(value: string, positive = false): boolean {
  if (!/^-?\d+$/.test(value)) return false
  const id = BigInt(value)
  return id !== 0n && id >= (positive ? 1n : -9223372036854775808n) && id <= 9223372036854775807n
}

export function messageDate(ticks: string): Date | null {
  try {
    const milliseconds = (BigInt(ticks) - 621355968000000000n) / 10000n
    const date = new Date(Number(milliseconds))
    return Number.isNaN(date.getTime()) ? null : date
  } catch {
    return null
  }
}

async function request(connection: Connection, path: string, method: 'GET' | 'POST', body?: unknown,
  signal?: AbortSignal): Promise<string> {
  const endpoint = normalizeEndpoint(connection.endpoint)
  const api = endpoint.endsWith('/api') ? endpoint : `${endpoint}/api`
  let response: Response
  try {
    response = await fetch(`${api}/review${path}`, {
      method,
      headers: {
        ...(connection.key ? { 'x-functions-key': connection.key } : {}),
        ...(body ? { 'Content-Type': 'application/json' } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
      signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(30_000)]) : AbortSignal.timeout(30_000),
      cache: 'no-store',
      credentials: 'omit',
      redirect: 'error',
    })
  } catch (error) {
    if (signal?.aborted) throw error
    throw new Error('Could not reach the Function or the request timed out. Check its URL, network access, and CORS settings.')
  }
  const text = await response.text()
  if (!response.ok) {
    let detail = ''
    try {
      const data = JSON.parse(text) as { error?: string }
      if (typeof data.error === 'string') detail = data.error
    } catch {}
    const defaults: Record<number, string> = {
      401: 'Access denied. Check your Function key.',
      403: 'Access denied. Check your Function key and network permissions.',
      404: 'The message or API route was not found.',
      409: 'The Function could not complete this action.',
    }
    throw new ApiError(response.status, detail || defaults[response.status] || `Request failed (HTTP ${response.status}).`)
  }
  return text
}

export async function getChats(connection: Connection, token: string | null, signal?: AbortSignal): Promise<ReviewChatPage> {
  const query = new URLSearchParams({ pageSize: '1000' })
  if (token) query.set('continuationToken', token)
  const data = parse(await request(connection, `/chats?${query}`, 'GET', undefined, signal)) as {
    chats?: Record<string, unknown>[]; continuationToken?: unknown
  }
  if (!data || !Array.isArray(data.chats) ||
      (data.continuationToken != null && typeof data.continuationToken !== 'string')) {
    throw new Error('The Function returned an unexpected chat response.')
  }
  const chats = data.chats.map((item): ReviewChat => {
    const chatId = String(item.chatId)
    if (!validId(chatId) || typeof item.name !== 'string' || typeof item.lastSeenUtc !== 'string')
      throw new Error('The Function returned an invalid chat.')
    return { chatId, name: item.name, lastSeenUtc: item.lastSeenUtc }
  })
  return { chats, continuationToken: data.continuationToken as string | null ?? null }
}

export async function getMessages(connection: Connection, chatId: string, pageSize: number,
  token: string | null, signal?: AbortSignal): Promise<ReviewPage> {
  const query = new URLSearchParams({ chatId, pageSize: String(pageSize) })
  if (token) query.set('continuationToken', token)
  const text = await request(connection, `/messages?${query}`, 'GET', undefined, signal)
  const data = parse(text) as { messages?: Record<string, unknown>[]; continuationToken?: unknown }
  if (!data || !Array.isArray(data.messages) ||
      (data.continuationToken != null && typeof data.continuationToken !== 'string')) {
    throw new Error('The Function returned an unexpected review response.')
  }
  const messages = data.messages.map((item): ReviewMessage => {
    const chat = String(item.chatId)
    const message = String(item.messageId)
    const user = String(item.userId)
    const ticks = String(item.messageDateTicks)
    if (!validId(chat) || !validId(message, true) || !validId(user, true) ||
        !/^\d+$/.test(ticks) || typeof item.userName !== 'string' ||
        typeof item.message !== 'string' || typeof item.imageText !== 'string') {
      throw new Error('The Function returned an invalid message.')
    }
    return { chatId: chat, messageId: message, userId: user, userName: item.userName,
      message: item.message, imageText: item.imageText, messageDateTicks: ticks }
  })
  return { messages, continuationToken: data.continuationToken as string | null ?? null }
}

export async function reviewMessage(connection: Connection, message: ReviewMessage, isSpam: boolean) {
  await request(connection, `/messages/${message.chatId}/${message.messageId}`, 'POST', { isSpam })
}

export async function removeRestrictions(connection: Connection, chatId: string, userId: string) {
  await request(connection, `/restrictions/${chatId}/${userId}`, 'POST')
}