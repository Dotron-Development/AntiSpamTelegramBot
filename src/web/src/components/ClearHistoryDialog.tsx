import { useState } from 'react'
import { LoaderCircle, Trash2 } from 'lucide-react'
import { Dialog } from './Dialog'

export function ClearHistoryDialog({ chatId, error, busy, onClose, onConfirm }: {
    chatId: string; error: string; busy: boolean; onClose: () => void; onConfirm: () => Promise<void>
}) {
    const [confirmation, setConfirmation] = useState('')
    return <Dialog title="DANGER: Clear chat history" onClose={onClose} busy={busy}>
        <div className="confirmation-symbol spam-symbol"><Trash2 size={28} /></div>
        <p className="confirmation-title">Chat <code>{chatId}</code></p>
        <p className="confirmation-text">Permanently delete all stored message history for this chat, not just this page.
            This cannot be undone. Other chats, the review queue and Telegram messages remain unchanged.</p>
        <label className="confirmation-text">Type DANGER to confirm
            <input aria-label="Type DANGER to confirm" autoComplete="off" value={confirmation} disabled={busy}
                onChange={event => setConfirmation(event.target.value)} />
        </label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions">
            <button className="button subtle" disabled={busy} onClick={onClose}>Cancel</button>
            <button className="button danger" disabled={busy || !!error || confirmation !== 'DANGER'} onClick={() => void onConfirm()}>
                {busy ? <LoaderCircle size={16} className="spin" /> : <Trash2 size={16} />}Delete chat history
            </button>
        </div>
    </Dialog>
}
