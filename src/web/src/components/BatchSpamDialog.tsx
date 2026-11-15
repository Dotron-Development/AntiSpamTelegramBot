import { LoaderCircle, ShieldX } from 'lucide-react'
import type { QueueQuery } from '../lib/workspace'
import { Dialog } from './Dialog'

export function BatchSpamDialog({ query, progress, error, busy, onClose, onConfirm }: {
    query: QueueQuery; progress: { completed: number; total: number } | null;
    error: string; busy: boolean; onClose: () => void; onConfirm: () => Promise<void>
}) {
    return <Dialog title="DANGER: Mark all as spam" onClose={onClose} busy={busy}>
        <div className="confirmation-symbol spam-symbol"><ShieldX size={28} /></div>
        <p className="confirmation-title">{query.chatId ? `Chat ${query.chatId}` : 'All chats'}</p>
        <p className="confirmation-text">Mark every queued message matching this loaded filter as spam, including all remaining pages?
            Messages will be processed one at a time. This removes them from the review queue and records their content as spam.</p>
        {(busy || progress) && <div role="status" aria-live="polite" className="confirmation-text">
            {busy && <LoaderCircle size={16} className="spin" />}
            {progress ? `Marked as spam: ${progress.completed}/${progress.total}` : 'Loading all queue pages...'}
        </div>}
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions">
            <button className="button subtle" disabled={busy} onClick={onClose}>{progress || error ? 'Close' : 'Cancel'}</button>
            {!progress && <button className="button danger" disabled={busy || !!error} onClick={() => void onConfirm()}>
                <ShieldX size={16} />Confirm mark all as spam
            </button>}
        </div>
    </Dialog>
}
