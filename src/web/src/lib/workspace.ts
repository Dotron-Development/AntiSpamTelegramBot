import { messageDate } from '../api/client'
import type { Connection, ReviewMessage } from '../api/client'

export interface QueueQuery { chatId: string; pageSize: number }
export interface Notice { error: boolean; text: string }
export interface Decision { message: ReviewMessage; isSpam: boolean }
export interface RestrictionValues { chatId: string; userId: string }
export type Notify = (notice: Notice | null) => void

export const connection: Connection = {
    endpoint: import.meta.env.API_URL,
    key: (import.meta.env.FUNCTION_KEY || '').trim(),
}

const settingsKey = 'antispam-review.settings'
const dateFormatter = new Intl.DateTimeFormat('en-GB', {
    dateStyle: 'medium', timeStyle: 'short', timeZone: 'UTC',
})

export function readChatId(): string {
    try {
        const value = JSON.parse(localStorage.getItem(settingsKey) ?? '{}')
        return typeof value.chatId === 'string' ? value.chatId : ''
    } catch { return '' }
}

export function rememberChat(chatId: string) {
    try { localStorage.setItem(settingsKey, JSON.stringify({ chatId })) } catch { }
}

export const errorText = (error: unknown) => error instanceof Error ? error.message : 'An unexpected error occurred.'

export function messageKey(message: ReviewMessage): string {
    return `${message.chatId}:${message.messageId}`
}

export function dateLabel(message: ReviewMessage): string {
    const date = messageDate(message.messageDateTicks)
    return date ? dateFormatter.format(date) : 'Date unavailable'
}

export function initials(name: string): string {
    return name.trim().split(/\s+/).slice(0, 2).map(part => Array.from(part)[0]).join('').toUpperCase() || '?'
}
