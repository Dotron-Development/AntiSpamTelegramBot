import { useState } from 'react'
import { ArrowRight, LoaderCircle } from 'lucide-react'
import type { ReviewChat } from '../api/client'
import type { QueueQuery } from '../lib/workspace'
import { ChatPicker } from './ChatPicker'

export function QueryForm({ chats, chatId, endpoint, busy, chatsLoading, loading, onChatChange, onRefresh, onSubmit,
    submitLabel = 'Load queue', itemLabel = 'messages' }: {
        chats: ReviewChat[]; chatId: string; endpoint: string; busy: boolean; chatsLoading: boolean; loading: boolean;
        onChatChange: (chatId: string) => void; onRefresh: () => Promise<void>; onSubmit: (query: QueueQuery) => void
        submitLabel?: string; itemLabel?: string
    }) {
    const [pageSize, setPageSize] = useState(50)
    return <form className="querybar" onSubmit={event => {
        event.preventDefault()
        onSubmit({ chatId: chatId.trim(), pageSize })
    }} noValidate>
        <ChatPicker chats={chats} chatId={chatId} busy={busy} chatsLoading={chatsLoading}
            onChatChange={onChatChange} onRefresh={onRefresh} />
        <div className="page-size"><label htmlFor="page-size">Page size</label>
            <select id="page-size" value={pageSize} onChange={event => setPageSize(Number(event.target.value))}>
                {[25, 50, 100, 250].map(size => <option key={size} value={size}>{size} {itemLabel}</option>)}
            </select>
        </div>
        <button className="button primary" type="submit" disabled={busy}>
            {loading ? <LoaderCircle size={16} className="spin" /> : <ArrowRight size={16} />}<span>{submitLabel}</span>
        </button>
        <div className="query-endpoint" title={endpoint}>{endpoint}</div>
    </form>
}
