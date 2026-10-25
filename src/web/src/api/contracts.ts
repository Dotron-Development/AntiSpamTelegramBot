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
  UserName: string
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
    userName: data.UserName,
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
