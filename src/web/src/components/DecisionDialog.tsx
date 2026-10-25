import { LoaderCircle, ShieldCheck, ShieldX } from 'lucide-react'
import type { Decision } from '../lib/workspace'
import { Dialog } from './Dialog'

export function DecisionDialog({ decision, error, busy, onClose, onConfirm }: {
    decision: Decision; error: string; busy: boolean; onClose: () => void; onConfirm: () => Promise<void>
}) {
    return <Dialog title={decision.isSpam ? 'Confirm spam' : 'Return message to chat'} onClose={onClose} busy={busy}>
        <div className={`confirmation-symbol ${decision.isSpam ? 'spam-symbol' : ''}`}>{decision.isSpam ? <ShieldX size={28} /> : <ShieldCheck size={28} />}</div>
        <p className="confirmation-title">{decision.message.userName || 'Unknown sender'}<span>Message #{decision.message.messageId}</span></p>
        <p className="confirmation-text">{decision.isSpam ? 'Confirm this message as spam?' : 'Mark as not spam and return the saved text to the chat?'}<br />Restrictions remain unchanged.</p>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions"><button className="button subtle" disabled={busy} onClick={onClose}>Cancel</button>
            <button className={`button ${decision.isSpam ? 'danger' : 'primary'}`} disabled={busy || !!error} onClick={() => void onConfirm()}>
                {busy && <LoaderCircle size={16} className="spin" />}Confirm {decision.isSpam ? 'spam' : 'not spam'}</button></div>
    </Dialog>
}
