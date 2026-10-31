import { ArrowDown, LoaderCircle, RefreshCw } from 'lucide-react'
import { messageDate } from '../api/client'
import type { CommandCall, HistoryMessage, ReviewChat } from '../api/client'

const utcFormat = new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'UTC' })

function utc(value: Date | string | null): string {
    const date = typeof value === 'string' ? new Date(value) : value
    return date && !Number.isNaN(date.getTime()) ? utcFormat.format(date) : 'Unknown'
}

export function RecordsTable({ kind, calls, messages, chats, chatId, loading, busy, hasLoaded, hasMore, onRefresh, onLoadMore }: {
    kind: 'calls' | 'history'; calls: CommandCall[]; messages: HistoryMessage[]; chats: ReviewChat[];
    chatId?: string; loading: boolean; busy: boolean; hasLoaded: boolean; hasMore: boolean;
    onRefresh: () => void; onLoadMore: () => void
}) {
    const count = kind === 'calls' ? calls.length : messages.length
    const names = new Map(chats.map(chat => [chat.chatId, chat.name]))
    return <section className="records-panel" aria-label={kind === 'calls' ? 'Command calls table' : 'Message history table'}>
        <div className="records-toolbar">
            <span>{chatId ? names.get(chatId) ?? chatId : 'All chats'}<span className="records-count">{count} records</span></span>
            <button className="icon-button" aria-label="Refresh records" title="Refresh records" disabled={busy || !hasLoaded}
                onClick={onRefresh}><RefreshCw size={16} /></button>
        </div>
        {count > 0 && <div className="records-scroll"><table className={kind === 'calls' ? 'calls-table' : 'history-table'}>
            <thead><tr><th scope="col">Chat</th>
                {kind === 'calls' ? <><th scope="col">Command</th><th scope="col">Last call (UTC)</th></> :
                    <><th scope="col">Sent (UTC)</th><th scope="col">Author</th><th scope="col">Message</th></>}
            </tr></thead>
            <tbody>{kind === 'calls' ? calls.map(call => <tr key={`${call.chatId}:${call.command}`}>
                <td className="record-chat">{names.get(call.chatId) ?? 'Unknown chat'}<small>{call.chatId}</small></td>
                <td><code>{call.command}</code></td><td className="record-time"><time dateTime={call.lastCall}>{utc(call.lastCall)}</time></td>
            </tr>) : messages.map(message => <tr key={`${message.chatId}:${message.messageId}`}>
                <td className="record-chat">{names.get(message.chatId) ?? 'Unknown chat'}<small>{message.chatId}</small><small>#{message.messageId}</small></td>
                <td className="record-time" title={message.dateTimeTicks}>{utc(messageDate(message.dateTimeTicks))}</td>
                <td className="record-author">{message.content.from?.name ?? 'Unknown author'}
                    {message.content.from && <><small>{message.content.from.id}</small>
                        {message.content.from.username && <small>@{message.content.from.username}</small>}</>}
                </td>
                <td><details className="history-message"><summary><span className="history-preview">{message.content.text || 'No text or caption'}</span></summary>
                    {message.content.replyTo && <blockquote><strong>{message.content.replyTo.from?.name ?? 'Unknown author'} · #{message.content.replyTo.messageId}</strong>
                        <p>{message.content.replyTo.text || 'No reply text'}</p></blockquote>}
                </details></td>
            </tr>)}</tbody>
        </table></div>}
        {count === 0 && !loading && <div className="records-empty">{hasLoaded ? 'No records found.' : 'No records loaded.'}</div>}
        {loading && <div className="records-loading" role="status"><LoaderCircle size={18} className="spin" />Loading records...</div>}
        {hasMore && <div className="records-pagination"><button className="button subtle" disabled={busy} onClick={onLoadMore}>
            <ArrowDown size={16} /><span>Load more</span></button></div>}
    </section>
}