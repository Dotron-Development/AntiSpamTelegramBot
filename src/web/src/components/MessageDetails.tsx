import { CheckCheck, Clipboard, FileText, Image, Inbox, ShieldCheck, ShieldX, UnlockKeyhole } from 'lucide-react'
import type { ReviewMessage } from '../api/client'
import { dateLabel, initials, reviewFromLabel } from '../lib/workspace'

export function MessageDetails({ message, busy, loading, hasLoaded, hasMore, chatsLoading, chatId, onCopy, onRestrictions, onDecision }: {
    message?: ReviewMessage; busy: boolean; loading: boolean; hasLoaded: boolean; hasMore: boolean; chatsLoading: boolean; chatId?: string;
    onCopy: (value: string) => Promise<void>; onRestrictions: (message: ReviewMessage) => void;
    onDecision: (message: ReviewMessage, isSpam: boolean) => void
}) {
    return <div className="review-pane">
        {message ? <>
            <header className="message-heading"><div className="sender"><span className="avatar detail-avatar">{initials(message.from.name)}</span>
                <div><div className="eyebrow">MESSAGE #{message.messageId}</div><h2>{reviewFromLabel(message.from)}</h2></div></div>
                <span className="pending-label"><span className="status-dot" />Awaiting review</span>
            </header>
            <dl className="metadata"><div><dt>User ID</dt><dd><code>{message.userId}</code><button className="icon-button" title="Copy user ID" aria-label="Copy user ID" onClick={() => void onCopy(message.userId)}><Clipboard size={13} /></button></dd></div>
                <div><dt>Chat ID</dt><dd><code>{message.chatId}</code></dd></div>
                <div><dt>Message date</dt><dd title={message.messageDateTicks}>{dateLabel(message)} <span className="utc">UTC</span></dd></div>
            </dl>
            <div className="message-content">
                <section className="content-section"><h3><FileText size={16} />Original text <span>{message.message.length.toLocaleString('en-US')} chars</span></h3>
                    <div className={`message-text ${!message.message ? 'no-content' : ''}`}>{message.message || 'No original text or caption.'}</div>
                </section>
                {message.imageText && <section className="content-section ocr-section"><h3><Image size={16} />Image transcription <span>OCR</span></h3>
                    <div className="message-text">{message.imageText}</div>
                </section>}
            </div>
            <footer className="review-actions"><button className="button subtle" disabled={busy}
                onClick={() => onRestrictions(message)}><UnlockKeyhole size={16} /><span>Remove restrictions</span></button>
                <div className="decision-actions"><button className="button not-spam" disabled={busy} onClick={() => onDecision(message, false)}>
                    <ShieldCheck size={17} />Not spam</button>
                    <button className="button danger" disabled={busy} onClick={() => onDecision(message, true)}>
                        <ShieldX size={17} />Spam</button></div>
            </footer>
        </> : <div className="detail-empty"><span className="empty-symbol">{hasLoaded && !hasMore ? <CheckCheck size={36} strokeWidth={1.4} /> : <Inbox size={36} strokeWidth={1.4} />}</span>
            <div className="eyebrow">{hasLoaded ? 'REVIEW QUEUE' : 'YOUR WORKSPACE'}</div>
            <h2>{loading ? 'Fetching the queue' : hasLoaded ? hasMore ? 'More messages available' : 'All clear for now' : 'Ready for review'}</h2>
            <p>{loading ? 'Waiting for the Function...' : hasLoaded ? chatId || 'All chats' : chatsLoading ? 'Loading chats from the Function...' : 'Load all chats, or choose a chat to filter the queue.'}</p>
        </div>}
    </div>
}
