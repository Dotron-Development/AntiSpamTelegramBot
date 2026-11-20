import { useId } from 'react'
import { LoaderCircle, Upload } from 'lucide-react'
import type { ReviewChat } from '../api/client'

export function ChatPicker({ chats, chatId, disabled, chatsLoading, loading, group, onChatChange, onLoad }: {
    chats: ReviewChat[]; chatId: string; disabled: boolean; chatsLoading: boolean; loading: boolean; group: boolean;
    onChatChange: (chatId: string) => void; onLoad: () => void
}) {
    const id = useId()
    const label = group ? 'Discussion group' : 'Chat'
    const loadLabel = group ? 'Load snapshot' : 'Load records'
    return <form className="chat-picker" onSubmit={event => {
        event.preventDefault()
        if (!disabled && chatId) onLoad()
    }}>
        <label htmlFor={id}>{label}</label>
        <div className="chat-controls">
            <select id={id} value={chatId} disabled={disabled} onChange={event => onChatChange(event.target.value)}>
                <option value="">{chatsLoading ? 'Loading chats…' : !chats.length ? `No ${group ? 'groups' : 'chats'} available` : `Choose a ${label.toLowerCase()}`}</option>
                {chats.map(chat => <option key={chat.chatId} value={chat.chatId}>{chat.name} ({chat.chatId})</option>)}
            </select>
            <button className="button primary upload-button" type="submit" aria-label={loadLabel} title={loadLabel} disabled={disabled || !chatId}>
                {loading ? <LoaderCircle size={20} className="spin" /> : <Upload size={20} />}
            </button>
        </div>
    </form>
}
