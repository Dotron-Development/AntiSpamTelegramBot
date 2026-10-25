import { ArrowDown, ChevronRight, Inbox, LoaderCircle, RefreshCw, Shield } from 'lucide-react'
import type { ReviewMessage } from '../api/client'
import { initials, messageKey } from '../lib/workspace'
import type { QueueQuery } from '../lib/workspace'

export function MessageQueue({ messages, selected, query, loading, working, busy, hasLoaded, hasMore, onSelect, onRefresh, onLoadMore }: {
    messages: ReviewMessage[]; selected?: ReviewMessage; query: QueueQuery | null;
    loading: boolean; working: boolean; busy: boolean; hasLoaded: boolean; hasMore: boolean;
    onSelect: (id: string) => void; onRefresh: () => void; onLoadMore: () => void
}) {
    return <aside className="queue">
        <div className="queue-heading"><div><span className="section-title">Pending review</span><span className="count">{String(messages.length).padStart(2, '0')}</span></div>
            <button className="icon-button" title="Refresh queue" aria-label="Refresh queue" disabled={!query || busy} onClick={onRefresh}>
                <RefreshCw size={16} className={loading ? 'spin' : ''} />
            </button>
        </div>
        <div className="queue-context">{query ? <><span className="status-dot" /><code>{query.chatId || 'All chats'}</code></> : <span>All pending messages</span>}</div>
        <div className="message-list" aria-label="Pending messages" aria-busy={loading}>
            {messages.map((message, index) => <button type="button" className={`message-row ${selected && messageKey(message) === messageKey(selected) ? 'selected' : ''}`}
                key={messageKey(message)} aria-pressed={!!selected && messageKey(message) === messageKey(selected)} disabled={working}
                onClick={() => onSelect(messageKey(message))}>
                <span className={`avatar avatar-${index % 4}`}>{initials(message.userName)}</span>
                <span className="message-summary"><span className="message-author">{message.userName || 'Unknown sender'}</span>
                    <span className="message-preview">{(message.message || message.imageText || 'No text').replace(/\s+/g, ' ')}</span>
                    <span className="message-meta"><span>#{message.messageId}</span><span>{message.imageText ? 'Text + OCR' : 'Text'}</span></span>
                </span><ChevronRight size={15} className="row-arrow" />
            </button>)}
            {!messages.length && <div className="queue-empty"><Inbox size={27} strokeWidth={1.4} />
                <span>{loading ? 'Loading messages...' : hasLoaded ? 'Nothing on this page' : 'No messages loaded'}</span></div>}
        </div>
        {hasMore && <button className="load-more" disabled={busy} onClick={onLoadMore}>
            {loading ? <LoaderCircle size={15} className="spin" /> : <ArrowDown size={15} />}Load more</button>}
        <div className="queue-footer"><Shield size={14} /><span>{hasLoaded ? 'Queue loaded' : 'Load the queue'}</span><span className="local-label">LOCAL</span></div>
    </aside>
}
