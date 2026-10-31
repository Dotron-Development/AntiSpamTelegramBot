import { parse } from 'lossless-json'
import type { LosslessNumber } from 'lossless-json'

export interface ReviewChatResponse {
  ChatId: LosslessNumber
  Name: string
  LastSeenUtc: string
}

export interface ReviewChatPageResponse {
  Chats: ReviewChatResponse[]
  ContinuationToken: string | null
}

export interface ReviewMessageResponse {
  ChatId: LosslessNumber
  MessageId: LosslessNumber
  UserId: LosslessNumber
  From: { Name: string; Nick: string | null }
  ReplyTo: { MessageId: LosslessNumber; From: { Name: string; Nick: string | null } | null; Text: string } | null
  Message: string
  ImageText: string
  ContentHash: string
  MessageDateTicks: LosslessNumber
}

export interface ReviewMessagePageResponse {
  Messages: ReviewMessageResponse[]
  ContinuationToken: string | null
}

export interface ReviewMessageRequest {
  isSpam: boolean
}

export interface ApiErrorResponse {
  error: string
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

export interface ReviewMessageFrom {
  name: string
  nick: string | null
}

export interface ReviewMessageReplyTo {
  messageId: string
  from: ReviewMessageFrom | null
  text: string
}

export interface ReviewMessage {
  chatId: string
  messageId: string
  userId: string
  from: ReviewMessageFrom
  replyTo: ReviewMessageReplyTo | null
  message: string
  imageText: string
  messageDateTicks: string
}

export interface ReviewPage {
  messages: ReviewMessage[]
  continuationToken: string | null
}

export interface CommandCall {
  chatId: string
  command: string
  lastCall: string
}

export interface HistoryAuthor {
  id: string
  name: string
  username: string | null
}

export interface HistoryMessage {
  chatId: string
  messageId: string
  dateTimeTicks: string
  content: {
    from: HistoryAuthor | null
    text: string
    replyTo: { messageId: string; from: HistoryAuthor | null; text: string } | null
  }
}

export interface TablePage<T> {
  rows: T[]
  continuationToken: string | null
}

export interface ChroniclePreview {
  chatId: string
  summary: string | null
  skipped: boolean
}

export function parseChroniclePreview(text: string): ChroniclePreview {
  const data = parse(text) as { chatId: LosslessNumber; summary: string | null; skipped: boolean }
  
  if (typeof data.skipped !== 'boolean' || (data.summary !== null && typeof data.summary !== 'string'))
    throw new Error('Invalid Chronicle response.')
  
  return { chatId: data.chatId.toString(), summary: data.summary, skipped: data.skipped }
}

interface HistoryAuthorResponse {
  id: LosslessNumber
  name: string
  username: string | null
}

interface HistoryMessageResponse {
  chatId: LosslessNumber
  messageId: LosslessNumber
  dateTimeTicks: LosslessNumber
  content: {
    from: HistoryAuthorResponse | null
    text: string
    replyTo: { messageId: LosslessNumber; from: HistoryAuthorResponse | null; text: string } | null
  }
}

function historyAuthor(data: HistoryAuthorResponse | null): HistoryAuthor | null {
  return data ? { id: data.id.toString(), name: data.name, username: data.username } : null
}

export function parseCommandCallPage(text: string): TablePage<CommandCall> {
  const data = parse(text) as { calls: { chatId: LosslessNumber; command: string; lastCall: string }[]; continuationToken: string | null }
  return {
    rows: data.calls.map(call => ({ chatId: call.chatId.toString(), command: call.command, lastCall: call.lastCall })),
    continuationToken: data.continuationToken,
  }
}

export function parseHistoryPage(text: string): TablePage<HistoryMessage> {
  const data = parse(text) as { messages: HistoryMessageResponse[]; continuationToken: string | null }
  return {
    rows: data.messages.map(message => ({
      chatId: message.chatId.toString(), messageId: message.messageId.toString(), dateTimeTicks: message.dateTimeTicks.toString(),
      content: {
        from: historyAuthor(message.content.from), text: message.content.text,
        replyTo: message.content.replyTo ? {
          messageId: message.content.replyTo.messageId.toString(), from: historyAuthor(message.content.replyTo.from),
          text: message.content.replyTo.text,
        } : null,
      },
    })),
    continuationToken: data.continuationToken,
  }
}

export function parseReviewChat(data: ReviewChatResponse): ReviewChat {
  return {
    chatId: data.ChatId.toString(),
    name: data.Name,
    lastSeenUtc: data.LastSeenUtc,
  }
}

export function parseReviewChatPage(text: string): ReviewChatPage {
  const data = parse(text) as ReviewChatPageResponse
  return {
    chats: data.Chats.map(parseReviewChat),
    continuationToken: data.ContinuationToken,
  }
}

export function parseReviewMessage(data: ReviewMessageResponse): ReviewMessage {
  return {
    chatId: data.ChatId.toString(),
    messageId: data.MessageId.toString(),
    userId: data.UserId.toString(),
    from: { name: data.From.Name, nick: data.From.Nick },
    replyTo: data.ReplyTo ? {
      messageId: data.ReplyTo.MessageId.toString(),
      from: data.ReplyTo.From ? { name: data.ReplyTo.From.Name, nick: data.ReplyTo.From.Nick } : null,
      text: data.ReplyTo.Text,
    } : null,
    message: data.Message,
    imageText: data.ImageText,
    messageDateTicks: data.MessageDateTicks.toString(),
  }
}

export function parseReviewMessagePage(text: string): ReviewPage {
  const data = parse(text) as ReviewMessagePageResponse
  return {
    messages: data.Messages.map(parseReviewMessage),
    continuationToken: data.ContinuationToken,
  }
}

export function parseApiError(text: string): ApiErrorResponse {
  return JSON.parse(text) as ApiErrorResponse
}
