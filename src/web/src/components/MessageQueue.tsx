import { ShieldCheck, ShieldX } from 'lucide-react'
import type { ReviewMessage } from '../api/client'
import { dateLabel, messageKey, reviewFromLabel } from '../lib/workspace'

export function MessageQueue({ messages, disabled, onDecision }: {
    messages: ReviewMessage[]; disabled: boolean;
    onDecision: (message: ReviewMessage, isSpam: boolean) => void
}) {
    return <ul className="card-list" aria-label="Pending messages">
        {messages.map(message => <li className="card message-row" key={messageKey(message)}>
            <header className="card-heading">
                <h2>{reviewFromLabel(message.from)}</h2>
                <p className="record-meta"><code>#{message.messageId}</code> · <time title={message.messageDateTicks}>{dateLabel(message)} UTC</time></p>
                <p className="record-meta">User <code>{message.userId}</code></p>
            </header>
            <p className="message-text">{message.message || 'No original text or caption.'}</p>
            {message.imageText && <section className="message-extra">
                <h3>Image transcription <span className="record-meta">OCR</span></h3>
                <p className="message-text">{message.imageText}</p>
            </section>}
            {message.replyTo && <blockquote>
                <strong>{message.replyTo.from ? reviewFromLabel(message.replyTo.from) : 'Unknown author'} · #{message.replyTo.messageId}</strong>
                <p>{message.replyTo.text || 'No reply text'}</p>
            </blockquote>}
            <div className="decision-actions">
                <button className="button danger" disabled={disabled} onClick={() => onDecision(message, true)}><ShieldX size={18} />Spam</button>
                <button className="button not-spam" disabled={disabled} onClick={() => onDecision(message, false)}><ShieldCheck size={18} />Not spam</button>
            </div>
        </li>)}
    </ul>
}
