import { messageDate } from '../api/client'
import type { CommandCall, HistoryMessage } from '../api/client'
import { messageKey, reviewFromLabel } from '../lib/workspace'

const updateTypeLabels: Record<HistoryMessage['updateType'], string> = {
    Message: 'Message',
    EditedMessage: 'Edited message',
    ChannelPost: 'Channel post',
    EditedChannelPost: 'Edited channel post',
}

const utcFormat = new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'UTC' })

function utc(value: Date | string | null): string {
    const date = typeof value === 'string' ? new Date(value) : value
    return date && !Number.isNaN(date.getTime()) ? utcFormat.format(date) : 'Unknown'
}

export function RecordsTable({ kind, calls, messages }: {
    kind: 'calls' | 'history'; calls: CommandCall[]; messages: HistoryMessage[]
}) {
    return <ul className="card-list" aria-label={kind === 'calls' ? 'Command calls' : 'Message history'}>
        {kind === 'calls' ? calls.map(call => <li className="card" key={`${call.chatId}:${call.command}`}>
            <h2><code>{call.command}</code></h2>
            <p className="record-meta">Last call <time dateTime={call.lastCall}>{utc(call.lastCall)} UTC</time></p>
        </li>) : messages.map(message => <li className="card" key={messageKey(message)}>
            <header className="card-heading">
                <h2 className="record-author">{message.content.from ? reviewFromLabel(message.content.from) : 'Unknown author'}</h2>
                <p className="record-meta"><span className="history-update-type">{updateTypeLabels[message.updateType]}</span> · <code>#{message.messageId}</code></p>
                <p className="record-meta"><time title={message.dateTimeTicks}>{utc(messageDate(message.dateTimeTicks))} UTC</time>
                    {message.content.from && <> · User <code>{message.content.from.id}</code></>}
                </p>
            </header>
            <details className="history-message"><summary><span className="history-preview">{message.content.payload.userMessage || message.content.payload.imageDescription || message.content.payload.imageText || 'No text or caption'}</span></summary>
                {message.content.payload.userMessage && <section className="message-extra" aria-label="User text"><strong>User text</strong><p className="message-text">{message.content.payload.userMessage}</p></section>}
                {message.content.payload.imageDescription && <section className="message-extra" aria-label="Image description"><strong>Image description</strong><p className="message-text">{message.content.payload.imageDescription}</p></section>}
                {message.content.payload.imageText && <section className="message-extra" aria-label="Image text"><strong>Image text</strong><p className="message-text">{message.content.payload.imageText}</p></section>}
                {message.content.replyTo && <blockquote><strong>{message.content.replyTo.from?.name ?? 'Unknown author'} · #{message.content.replyTo.messageId}</strong>
                    <p>{message.content.replyTo.text || 'No reply text'}</p></blockquote>}
            </details>
        </li>)}
    </ul>
}