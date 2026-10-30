import { parseApiError, parseReviewChatPage, parseReviewMessagePage } from './contracts'
import type { ReviewChatPage, ReviewMessage, ReviewMessageRequest, ReviewPage } from './contracts'

export type { ReviewChat, ReviewChatPage, ReviewMessage, ReviewPage } from './contracts'

export interface Connection {
  endpoint: string
  getAccessToken: () => Promise<string>
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
  const accessToken = await connection.getAccessToken()
  let response: Response
  try {
    response = await fetch(`${api}/review${path}`, {
      method,
      headers: {
        Authorization: `Bearer ${accessToken}`,
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
      detail = parseApiError(text).error
    } catch {}
    const defaults: Record<number, string> = {
      401: 'Your session is invalid or expired. Sign out and sign in again.',
      403: 'Access denied. Check your Moderator role and API permission.',
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
  return parseReviewChatPage(await request(connection, `/chats?${query}`, 'GET', undefined, signal))
}

export async function getMessages(connection: Connection, chatId: string, pageSize: number,
  token: string | null, signal?: AbortSignal): Promise<ReviewPage> {
  const query = new URLSearchParams()
  if (chatId) query.set('chatId', chatId)
  query.set('pageSize', String(pageSize))
  if (token) query.set('continuationToken', token)
  return parseReviewMessagePage(await request(connection, `/messages?${query}`, 'GET', undefined, signal))
}

export async function reviewMessage(connection: Connection, message: ReviewMessage, isSpam: boolean) {
  const body: ReviewMessageRequest = { isSpam }
  await request(connection, `/messages/${message.chatId}/${message.messageId}`, 'POST', body)
}

export async function removeRestrictions(connection: Connection, chatId: string, userId: string) {
  await request(connection, `/restrictions/${chatId}/${userId}`, 'POST')
}