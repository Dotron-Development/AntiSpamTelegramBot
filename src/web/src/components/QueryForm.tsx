import { useState } from 'react'
import { ArrowRight, LoaderCircle, RefreshCw } from 'lucide-react'
import type { ReviewChat } from '../api/client'
import type { QueueQuery } from '../lib/workspace'

export function QueryForm({ chats, chatId, endpoint, busy, chatsLoading, loading, onChatChange, onRefresh, onSubmit }: {
    chats: ReviewChat[]; chatId: string; endpoint: string; busy: boolean; chatsLoading: boolean; loading: boolean;
    onChatChange: (chatId: string) => void; onRefresh: () => Promise<void>; onSubmit: (query: QueueQuery) => void
}) {
    const [pageSize, setPageSize] = useState(50)
    return <form className="querybar" onSubmit={event => {
        event.preventDefault()
        onSubmit({ chatId: chatId.trim(), pageSize })
    }} noValidate>
        <div className="chat-field"><label htmlFor="chat-id">Chat</label>
            <div className="chat-picker"><select id="chat-id" value={chatId} disabled={busy}
                onChange={event => onChatChange(event.target.value)}>
                <option value="">{chatsLoading ? 'All chats (loading catalog...)' : 'All chats'}</option>
                {chats.map(chat => <option key={chat.chatId} value={chat.chatId}>{chat.name} ({chat.chatId})</option>)}
            </select><button type="button" className="icon-button" title="Refresh chats" aria-label="Refresh chats"
                disabled={busy} onClick={() => void onRefresh()}>
                    <RefreshCw size={16} className={chatsLoading ? 'spin' : ''} /></button></div>
        </div>
        <div className="page-size"><label htmlFor="page-size">Page size</label>
            <select id="page-size" value={pageSize} onChange={event => setPageSize(Number(event.target.value))}>
                {[25, 50, 100, 250].map(size => <option key={size} value={size}>{size} messages</option>)}
            </select>
        </div>
        <button className="button primary" type="submit" disabled={busy}>
            {loading ? <LoaderCircle size={16} className="spin" /> : <ArrowRight size={16} />}<span>Load queue</span>
        </button>
        <div className="query-endpoint" title={endpoint}>{endpoint}</div>
    </form>
}
