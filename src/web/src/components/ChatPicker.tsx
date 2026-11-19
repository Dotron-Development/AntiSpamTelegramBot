import { useId } from 'react'
import { RefreshCw } from 'lucide-react'
import type { ReviewChat } from '../api/client'

export function ChatPicker({ chats, chatId, busy, chatsLoading, onChatChange, onRefresh,
    label = 'Chat', emptyLabel = 'All chats' }: {
        chats: ReviewChat[]; chatId: string; busy: boolean; chatsLoading: boolean;
        onChatChange: (chatId: string) => void; onRefresh: () => Promise<void>;
        label?: string; emptyLabel?: string
    }) {
    const id = useId()
    return <div className="chat-field"><label htmlFor={id}>{label}</label>
        <div className="chat-picker"><select id={id} value={chatId} disabled={busy}
            onChange={event => onChatChange(event.target.value)}>
            <option value="">{chatsLoading ? `${emptyLabel} (loading catalog...)` : emptyLabel}</option>
            {chats.map(chat => <option key={chat.chatId} value={chat.chatId}>{chat.name} ({chat.chatId})</option>)}
        </select><button type="button" className="icon-button" title="Refresh chats" aria-label="Refresh chats"
            disabled={busy} onClick={() => void onRefresh()}>
            <RefreshCw size={16} className={chatsLoading ? 'spin' : ''} /></button></div>
    </div>
}
