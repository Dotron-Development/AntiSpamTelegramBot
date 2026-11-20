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
  From: { Id: LosslessNumber; Name: string; Username: string | null }
  ReplyTo: { MessageId: LosslessNumber; From: { Id: LosslessNumber; Name: string; Username: string | null } | null; Text: string } | null
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

export interface MessageFrom {
  id: string
  name: string
  username: string | null
}

export interface ReviewMessageReplyTo {
  messageId: string
  from: MessageFrom | null
  text: string
}

export interface ReviewMessage {
  chatId: string
  messageId: string
  userId: string
  from: MessageFrom
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

export interface HistoryPayload {
  userMessage?: string | null
  imageText?: string | null
  imageDescription?: string | null
}

export type HistoryUpdateType = 'Message' | 'EditedMessage' | 'ChannelPost' | 'EditedChannelPost'

export interface HistoryMessage {
  chatId: string
  messageId: string
  dateTimeTicks: string
  updateType: HistoryUpdateType
  content: {
    from: MessageFrom | null
    payload: HistoryPayload
    replyTo: { messageId: string; from: MessageFrom | null; text: string } | null
  }
}

export interface TablePage<T> {
  rows: T[]
  continuationToken: string | null
}

interface MessageFromResponse {
  id: LosslessNumber
  name: string
  username: string | null
}

interface HistoryMessageResponse {
  chatId: LosslessNumber
  messageId: LosslessNumber
  dateTimeTicks: LosslessNumber
  updateType?: unknown
  content: {
    from: MessageFromResponse | null
    payload?: HistoryPayload | null
    text?: string | null
    replyTo: { messageId: LosslessNumber; from: MessageFromResponse | null; text: string } | null
  }
}

function historyUpdateType(value: unknown): HistoryUpdateType {
  switch (value) {
    case 'Message':
    case 'EditedMessage':
    case 'ChannelPost':
    case 'EditedChannelPost':
      return value
    default:
      return 'Message'
  }
}

function messageFrom(data: MessageFromResponse | null): MessageFrom | null {
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
      updateType: historyUpdateType(message.updateType),
      content: {
        from: messageFrom(message.content.from),
        payload: message.content.payload ?? { userMessage: message.content.text ?? null },
        replyTo: message.content.replyTo ? {
          messageId: message.content.replyTo.messageId.toString(), from: messageFrom(message.content.replyTo.from),
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
    from: { id: data.From.Id.toString(), name: data.From.Name, username: data.From.Username },
    replyTo: data.ReplyTo ? {
      messageId: data.ReplyTo.MessageId.toString(),
      from: data.ReplyTo.From ? {
        id: data.ReplyTo.From.Id.toString(), name: data.ReplyTo.From.Name, username: data.ReplyTo.From.Username,
      } : null,
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
