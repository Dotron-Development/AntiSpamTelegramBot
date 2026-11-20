import { parseApiError, parseReviewChatPage, parseReviewMessagePage, parseCommandCallPage, parseHistoryPage, parseMentionPreview } from './contracts'
import type { MentionPreviewRequest, ReviewChatPage, ReviewMessage, ReviewMessageRequest, ReviewPage } from './contracts'

export type { ReviewChat, ReviewChatPage, ReviewMessage, MessageFrom, ReviewMessageReplyTo, ReviewPage } from './contracts'
export type { CommandCall, HistoryMessage, TablePage } from './contracts'

export type AccessFailure = 'reopen' | 'denied'

export interface Connection {
  endpoint: string
  initData: string
  onAccessFailure: (failure: AccessFailure) => void
}

export const accessMessages: Record<AccessFailure, string> = {
  reopen: 'Your Telegram launch is invalid or expired. Close this Mini App and reopen it from the menu in the private bot chat.',
  denied: 'You do not have access to the review workspace.',
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

async function fetchReview(connection: Connection, path: string, method: 'GET' | 'POST' | 'DELETE', body?: unknown,
  signal?: AbortSignal, timeout = 30_000): Promise<Response> {
  const base = normalizeEndpoint(connection.endpoint)
  const api = base.endsWith('/api') ? base : `${base}/api`
  try {
    return await fetch(`${api}/review${path}`, {
      method,
      headers: {
        Authorization: `tma ${connection.initData}`,
        ...(body ? { 'Content-Type': 'application/json' } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
      signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(timeout)]) : AbortSignal.timeout(timeout),
      cache: 'no-store',
      credentials: 'omit',
      redirect: 'error',
    })
  } catch (error) {
    if (signal?.aborted) throw error
    throw new Error('Could not reach the Function or the request timed out. Check its URL, network access, and CORS settings.')
  }
}

async function request(connection: Connection, path: string, method: 'GET' | 'POST' | 'DELETE', body?: unknown,
  signal?: AbortSignal, timeout?: number): Promise<string> {
  return readReviewResponse(await fetchReview(connection, path, method, body, signal, timeout), connection)
}

async function readReviewResponse(response: Response, connection: Connection): Promise<string> {
  if (response.status === 401) failAccess(connection, 401, 'reopen')
  const text = await response.text()
  // The access guard returns an empty 403; action-specific errors can include a body.
  if (response.status === 403 && text === '') failAccess(connection, 403, 'denied')
  if (!response.ok) {
    let detail = ''
    try {
      detail = parseApiError(text).error
    } catch {}
    const defaults: Record<number, string> = {
      403: 'Access denied for this review action.',
      404: 'The message or API route was not found.',
      409: 'The Function could not complete this action.',
    }
    throw new ApiError(response.status, detail || defaults[response.status] || `Request failed (HTTP ${response.status}).`)
  }
  return text
}

function failAccess(connection: Connection, status: number, failure: AccessFailure): never {
  connection.onAccessFailure(failure)
  throw new ApiError(status, accessMessages[failure])
}

export async function checkReviewAccess(connection: Connection, signal: AbortSignal): Promise<void> {
  const response = await fetchReview(connection, '/access', 'GET', undefined, signal)
  if (response.status === 204) return
  await readReviewResponse(response, connection)
  throw new ApiError(response.status, 'The review API returned an unexpected access response.')
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

function tableQuery(chatId: string, pageSize: number, token: string | null): URLSearchParams {
  const query = new URLSearchParams({ pageSize: String(pageSize) })
  if (chatId) query.set('chatId', chatId)
  if (token) query.set('continuationToken', token)
  return query
}

export async function getCommandCalls(connection: Connection, chatId: string, pageSize: number,
  token: string | null, signal?: AbortSignal) {
  return parseCommandCallPage(await request(connection, `/command-calls?${tableQuery(chatId, pageSize, token)}`, 'GET', undefined, signal))
}

export async function getHistory(connection: Connection, chatId: string, pageSize: number,
  token: string | null, signal?: AbortSignal) {
  return parseHistoryPage(await request(connection, `/history?${tableQuery(chatId, pageSize, token)}`, 'GET', undefined, signal))
}

export async function clearChatHistory(connection: Connection, chatId: string) {
  if (!validId(chatId)) throw new Error('Select a valid chat before clearing its history.')
  await request(connection, `/history/${chatId}`, 'DELETE', undefined, undefined, 120_000)
}

export async function previewMention(connection: Connection, body: MentionPreviewRequest, signal?: AbortSignal) {
  return parseMentionPreview(await request(connection, '/mention', 'POST', body, signal, 120_000))
}

export async function reviewMessage(connection: Connection, message: ReviewMessage, isSpam: boolean) {
  const body: ReviewMessageRequest = { isSpam }
  await request(connection, `/messages/${message.chatId}/${message.messageId}`, 'POST', body)
}

export async function removeRestrictions(connection: Connection, chatId: string, userId: string) {
  await request(connection, `/restrictions/${chatId}/${userId}`, 'POST')
}
export interface SearchSnapshot {
  name: string
  sizeBytes: number
  lastModified: string
}

export async function getSearchSnapshots(connection: Connection, signal?: AbortSignal): Promise<SearchSnapshot[]> {
  const rows: SearchSnapshot[] = JSON.parse(await request(connection, '/search-snapshots', 'GET', undefined, signal))
  if (!Array.isArray(rows) || rows.some(row => !row || typeof row.name !== 'string' ||
    !Number.isSafeInteger(row.sizeBytes) || row.sizeBytes < 0 || typeof row.lastModified !== 'string' ||
    !Number.isFinite(Date.parse(row.lastModified)))) throw new Error('Invalid snapshot list response.')
  return rows
}

export async function getSearchSnapshotChannel(connection: Connection, groupId: string, signal: AbortSignal): Promise<string> {
  const { channelId } = JSON.parse(await request(connection, `/search-snapshots/groups/${encodeURIComponent(groupId)}/channel`, 'GET', undefined, signal))
  if (typeof channelId !== 'string' || !channelId.startsWith('-') || !validId(channelId)) {
    throw new Error('Invalid linked channel response.')
  }
  return channelId
}

export async function resetSearchSnapshot(connection: Connection, channelId: string) {
  await request(connection, `/search-snapshots/${encodeURIComponent(channelId)}/reset`, 'POST')
}
