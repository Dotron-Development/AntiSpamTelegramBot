import { useEffect, useId, useRef, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import {
    ArrowDown, ArrowRight, Check, CheckCheck, ChevronRight, CircleAlert, Clipboard,
    Eye, EyeOff, FileText, Image, Inbox, LoaderCircle, Plug, RefreshCw, Settings2,
    Shield, ShieldCheck, ShieldX, Unplug, UnlockKeyhole, X
} from 'lucide-react'
import {
    ApiError, getChats, getMessages, messageDate, normalizeEndpoint, removeRestrictions,
    reviewMessage, validId
} from './api'
import type { Connection, ReviewChat, ReviewMessage } from './api'
import './App.css'

const settingsKey = 'antispam-review.settings'
const dateFormatter = new Intl.DateTimeFormat('en-GB', {
    dateStyle: 'medium', timeStyle: 'short', timeZone: 'UTC',
})

function readSettings(): { endpoint: string; chatId: string } {
    try {
        const value = JSON.parse(localStorage.getItem(settingsKey) ?? '{}')
        return {
            endpoint: import.meta.env.API_URL,
            chatId: typeof value.chatId === 'string' ? value.chatId : ''
        }
    } catch {
        return { endpoint: import.meta.env.API_URL, chatId: '' }
    }
}

function remember(chatId: string) {
    try { localStorage.setItem(settingsKey, JSON.stringify({ chatId })) } catch { }
}

function dateLabel(message: ReviewMessage): string {
    const date = messageDate(message.messageDateTicks)
    return date ? dateFormatter.format(date) : 'Date unavailable'
}

function initials(name: string): string {
    return name.trim().split(/\s+/).slice(0, 2).map(part => Array.from(part)[0]).join('').toUpperCase() || '?'
}

function Dialog({ title, children, onClose, busy = false }: {
    title: string; children: ReactNode; onClose: () => void; busy?: boolean
}) {
    const element = useRef<HTMLDialogElement>(null)
    const titleId = useId()
    useEffect(() => {
        const dialog = element.current!
        dialog.showModal()
        return () => dialog.close()
    }, [])
    return <dialog ref={element} className="dialog" aria-labelledby={titleId}
        onCancel={event => { event.preventDefault(); if (!busy) onClose() }}>
        <header className="dialog-heading"><h2 id={titleId}>{title}</h2>
            <button className="icon-button" aria-label="Close dialog" title="Close dialog"
                onClick={onClose} disabled={busy}><X size={19} /></button>
        </header>
        {children}
    </dialog>
}

function App() {
    const [saved] = useState(readSettings)
    const [connection, setConnection] = useState<Connection | null>(null)
    const [endpoint, setEndpoint] = useState(saved.endpoint)
    const [key, setKey] = useState('')
    const [showKey, setShowKey] = useState(false)
    const [chatId, setChatId] = useState(saved.chatId)
    const [chats, setChats] = useState<ReviewChat[]>([])
    const [chatsLoading, setChatsLoading] = useState(false)
    const [pageSize, setPageSize] = useState(50)
    const [query, setQuery] = useState<{ chatId: string; pageSize: number } | null>(null)
    const [messages, setMessages] = useState<ReviewMessage[]>([])
    const [selectedId, setSelectedId] = useState<string | null>(null)
    const [continuationToken, setContinuationToken] = useState<string | null>(null)
    const [loading, setLoading] = useState(false)
    const [hasLoaded, setHasLoaded] = useState(false)
    const [working, setWorking] = useState(false)
    const [dialog, setDialog] = useState<'connection' | 'decision' | 'restrictions' | null>(null)
    const [decision, setDecision] = useState<{ message: ReviewMessage; isSpam: boolean } | null>(null)
    const [restoreChat, setRestoreChat] = useState('')
    const [restoreUser, setRestoreUser] = useState('')
    const [dialogError, setDialogError] = useState('')
    const [notice, setNotice] = useState<{ error: boolean; text: string } | null>(null)
    const [lastReview, setLastReview] = useState<{ message: ReviewMessage; isSpam: boolean } | null>(null)
    const [restoredUsers, setRestoredUsers] = useState<string[]>([])
    const pending = useRef<AbortController | null>(null)
    const requestId = useRef(0)

    useEffect(() => () => pending.current?.abort(), [])

    const selected = messages.find(message => message.messageId === selectedId) ?? messages[0]
    const locked = loading || working || chatsLoading
    const errorText = (error: unknown) => error instanceof Error ? error.message : 'An unexpected error occurred.'

    async function loadChats(api: Connection) {
        pending.current?.abort()
        const controller = new AbortController()
        pending.current = controller
        const id = ++requestId.current
        setChatsLoading(true)
        setNotice(null)
        try {
            const catalog = new Map<string, ReviewChat>()
            const tokens = new Set<string>()
            let token: string | null = null
            do {
                const page = await getChats(api, token, controller.signal)
                for (const chat of page.chats) catalog.set(chat.chatId, chat)
                token = page.continuationToken
                if (token && tokens.has(token)) throw new Error('The Function returned a repeated chat continuation token.')
                if (token) tokens.add(token)
            } while (token)
            if (id !== requestId.current) return
            const items = [...catalog.values()].sort((left, right) => left.name.localeCompare(right.name, 'en'))
            setChats(items)
            setChatId(previous => catalog.has(previous) ? previous : items[0]?.chatId ?? '')
        } catch (error) {
            if (!controller.signal.aborted && id === requestId.current)
                setNotice({ error: true, text: errorText(error) })
        } finally {
            if (id === requestId.current) setChatsLoading(false)
        }
    }

    async function load(api: Connection, targetChat: string, size: number, append = false) {
        if (!validId(targetChat)) {
            setNotice({ error: true, text: 'Select a chat from the list.' })
            return
        }
        pending.current?.abort()
        const controller = new AbortController()
        pending.current = controller
        const id = ++requestId.current
        setLoading(true)
        setNotice(null)
        if (!append) {
            setMessages([])
            setSelectedId(null)
            setContinuationToken(null)
            setHasLoaded(false)
            setQuery({ chatId: targetChat, pageSize: size })
            setLastReview(null)
        }
        try {
            const page = await getMessages(api, targetChat, size, append ? continuationToken : null, controller.signal)
            if (id !== requestId.current) return
            setMessages(previous => {
                const combined = append ? [...previous, ...page.messages] : page.messages
                return Array.from(new Map(combined.map(message => [message.messageId, message])).values())
            })
            setContinuationToken(page.continuationToken)
            setHasLoaded(true)
            remember(targetChat)
        } catch (error) {
            if (!controller.signal.aborted && id === requestId.current)
                setNotice({ error: true, text: errorText(error) })
        } finally {
            if (id === requestId.current) setLoading(false)
        }
    }

    function connect(event: FormEvent) {
        event.preventDefault()
        try {
            const next = { endpoint: normalizeEndpoint(endpoint), key: key.trim() }
            setConnection(next)
            setEndpoint(next.endpoint)
            remember(chatId.trim())
            setDialog(null)
            setDialogError('')
            setMessages([])
            setQuery(null)
            setHasLoaded(false)
            setLastReview(null)
            setNotice(null)
            setContinuationToken(null)
            setRestoredUsers([])
            setChats([])
            void loadChats(next)
        } catch (error) { setDialogError(errorText(error)) }
    }

    function submitChat(event: FormEvent) {
        event.preventDefault()
        if (!connection) { setDialogError(''); setDialog('connection'); return }
        void load(connection, chatId.trim(), pageSize)
    }

    function disconnect() {
        pending.current?.abort()
        requestId.current++
        setConnection(null)
        setKey('')
        setMessages([])
        setQuery(null)
        setHasLoaded(false)
        setLoading(false)
        setChatsLoading(false)
        setChats([])
        setContinuationToken(null)
        setLastReview(null)
        setNotice(null)
        setRestoredUsers([])
    }

    function openRestrictions(message?: ReviewMessage) {
        setRestoreChat(message?.chatId ?? query?.chatId ?? chatId)
        setRestoreUser(message?.userId ?? '')
        setDialogError('')
        setDialog('restrictions')
    }

    async function applyDecision() {
        if (!connection || !decision || working) return
        setWorking(true)
        setDialogError('')
        try {
            await reviewMessage(connection, decision.message, decision.isSpam)
            setMessages(previous => previous.filter(message => message.messageId !== decision.message.messageId))
            setLastReview(decision)
            setNotice({ error: false, text: decision.isSpam ? 'Marked as spam.' : 'Marked as not spam. Saved text returned to chat.' })
            setDialog(null)
        } catch (error) {
            setDialogError(`${errorText(error)} The action may already have completed. Refresh before retrying; a repeated not-spam decision can republish text.`)
            if (error instanceof ApiError && error.status === 404)
                setNotice({ error: true, text: 'This message is no longer in the queue. Refresh to update the list.' })
        } finally { setWorking(false) }
    }

    async function restore(event: FormEvent) {
        event.preventDefault()
        if (!connection || working) return
        const targetChat = restoreChat.trim()
        const targetUser = restoreUser.trim()
        if (!validId(targetChat) || !validId(targetUser, true)) {
            setDialogError('Enter a valid chat ID and a positive user ID.')
            return
        }
        setWorking(true)
        setDialogError('')
        try {
            await removeRestrictions(connection, targetChat, targetUser)
            setRestoredUsers(previous => [...previous, `${targetChat}:${targetUser}`])
            setNotice({ error: false, text: `Restrictions removed for user ${targetUser}.` })
            setDialog(null)
        } catch (error) { setDialogError(errorText(error)) }
        finally { setWorking(false) }
    }

    async function copy(value: string) {
        try {
            await navigator.clipboard.writeText(value)
            setNotice({ error: false, text: 'Copied to clipboard.' })
        } catch { setNotice({ error: true, text: 'Clipboard access is unavailable.' }) }
    }

    return <div className="app">
        <header className="topbar">
            <a className="brand" href="/" aria-label="AntiSpam review home">
                <span className="brand-mark"><ShieldCheck size={23} strokeWidth={1.8} /></span>
                <span>AntiSpam<span className="brand-divider">/</span><span className="brand-product">Review</span></span>
            </a>
            <div className="topbar-actions">
                <span className={`connection-status ${connection ? 'configured' : ''}`}>
                    <span className="status-dot" />{connection ? 'Configured' : 'Disconnected'}
                </span>
                <button className="button subtle connection-button" aria-label={connection ? 'Connection' : 'Connect Function'}
                    title={connection ? 'Connection' : 'Connect Function'} onClick={() => { setDialogError(''); setDialog('connection') }} disabled={locked}>
                    <Settings2 size={16} /><span>{connection ? 'Connection' : 'Connect Function'}</span>
                </button>
                {connection && <button className="icon-button" title="Disconnect and clear key" aria-label="Disconnect and clear key"
                    onClick={disconnect} disabled={working}><Unplug size={17} /></button>}
            </div>
        </header>

        <main>
            <section className="page-heading">
                <div><div className="eyebrow">TELEGRAM MODERATION</div><h1>Review queue<span className="heading-dot">.</span></h1></div>
                <button className="button subtle" onClick={() => openRestrictions(selected ?? lastReview?.message)} disabled={!connection || locked}>
                    <UnlockKeyhole size={16} /><span>Remove restrictions</span>
                </button>
            </section>

            <form className="querybar" onSubmit={submitChat} noValidate>
                <div className="chat-field"><label htmlFor="chat-id">Chat</label>
                    <div className="chat-picker"><select id="chat-id" value={chatId} disabled={!connection || locked}
                        onChange={event => setChatId(event.target.value)}>
                        <option value="">{chatsLoading ? 'Loading chats...' : chats.length ? 'Select a chat' : 'No chats loaded'}</option>
                        {chats.map(chat => <option key={chat.chatId} value={chat.chatId}>{chat.name} ({chat.chatId})</option>)}
                    </select><button type="button" className="icon-button" title="Refresh chats" aria-label="Refresh chats"
                        disabled={!connection || locked} onClick={() => { if (connection) void loadChats(connection) }}>
                            <RefreshCw size={16} className={chatsLoading ? 'spin' : ''} /></button></div>
                </div>
                <div className="page-size"><label htmlFor="page-size">Page size</label>
                    <select id="page-size" value={pageSize} onChange={event => setPageSize(Number(event.target.value))}>
                        {[25, 50, 100, 250].map(size => <option key={size} value={size}>{size} messages</option>)}
                    </select>
                </div>
                <button className="button primary" type="submit" disabled={locked}>
                    {loading ? <LoaderCircle size={16} className="spin" /> : <ArrowRight size={16} />}<span>Load queue</span>
                </button>
                <div className="query-endpoint" title={connection?.endpoint}>{connection ? new URL(connection.endpoint).host : 'No Function connected'}</div>
            </form>

            {notice && <div className={`notice ${notice.error ? 'error' : 'success'}`} role={notice.error ? 'alert' : 'status'}>
                {notice.error ? <CircleAlert size={17} /> : <Check size={17} />}<span>{notice.text}</span>
                <button className="icon-button" title="Dismiss notification" aria-label="Dismiss notification" onClick={() => setNotice(null)}><X size={16} /></button>
            </div>}

            <section className="workspace" aria-label="Message review workspace">
                <aside className="queue">
                    <div className="queue-heading"><div><span className="section-title">Pending review</span><span className="count">{String(messages.length).padStart(2, '0')}</span></div>
                        <button className="icon-button" title="Refresh queue" aria-label="Refresh queue" disabled={!connection || !query || locked}
                            onClick={() => { if (connection && query) void load(connection, query.chatId, query.pageSize) }}>
                            <RefreshCw size={16} className={loading ? 'spin' : ''} />
                        </button>
                    </div>
                    <div className="queue-context">{query ? <><span className="status-dot" /><code>{query.chatId}</code></> : <span>All pending messages in a chat</span>}</div>
                    <div className="message-list" aria-label="Pending messages" aria-busy={loading}>
                        {messages.map((message, index) => <button type="button" className={`message-row ${message.messageId === selected?.messageId ? 'selected' : ''}`}
                            key={message.messageId} aria-pressed={message.messageId === selected?.messageId} disabled={working}
                            onClick={() => setSelectedId(message.messageId)}>
                            <span className={`avatar avatar-${index % 4}`}>{initials(message.userName)}</span>
                            <span className="message-summary"><span className="message-author">{message.userName || 'Unknown sender'}</span>
                                <span className="message-preview">{(message.message || message.imageText || 'No text').replace(/\s+/g, ' ')}</span>
                                <span className="message-meta"><span>#{message.messageId}</span><span>{message.imageText ? 'Text + OCR' : 'Text'}</span></span>
                            </span><ChevronRight size={15} className="row-arrow" />
                        </button>)}
                        {!messages.length && <div className="queue-empty"><Inbox size={27} strokeWidth={1.4} />
                            <span>{loading ? 'Loading messages...' : hasLoaded ? 'Nothing on this page' : 'No messages loaded'}</span></div>}
                    </div>
                    {continuationToken && <button className="load-more" disabled={locked} onClick={() => {
                        if (connection && query) void load(connection, query.chatId, query.pageSize, true)
                    }}>{loading ? <LoaderCircle size={15} className="spin" /> : <ArrowDown size={15} />}Load more</button>}
                    <div className="queue-footer"><Shield size={14} /><span>{hasLoaded ? 'Queue loaded' : 'Awaiting connection'}</span><span className="local-label">LOCAL</span></div>
                </aside>

                <div className="review-pane">
                    {selected ? <>
                        <header className="message-heading"><div className="sender"><span className="avatar detail-avatar">{initials(selected.userName)}</span>
                            <div><div className="eyebrow">MESSAGE #{selected.messageId}</div><h2>{selected.userName || 'Unknown sender'}</h2></div></div>
                            <span className="pending-label"><span className="status-dot" />Awaiting review</span>
                        </header>
                        <dl className="metadata"><div><dt>User ID</dt><dd><code>{selected.userId}</code><button className="icon-button" title="Copy user ID" aria-label="Copy user ID" onClick={() => void copy(selected.userId)}><Clipboard size={13} /></button></dd></div>
                            <div><dt>Chat ID</dt><dd><code>{selected.chatId}</code></dd></div>
                            <div><dt>Message date</dt><dd title={selected.messageDateTicks}>{dateLabel(selected)} <span className="utc">UTC</span></dd></div>
                        </dl>
                        <div className="message-content">
                            <section className="content-section"><h3><FileText size={16} />Original text <span>{selected.message.length.toLocaleString('en-US')} chars</span></h3>
                                <div className={`message-text ${!selected.message ? 'no-content' : ''}`}>{selected.message || 'No original text or caption.'}</div>
                            </section>
                            {selected.imageText && <section className="content-section ocr-section"><h3><Image size={16} />Image transcription <span>OCR</span></h3>
                                <div className="message-text">{selected.imageText}</div>
                            </section>}
                        </div>
                        <footer className="review-actions"><button className="button subtle" disabled={locked}
                            onClick={() => openRestrictions(selected)}><UnlockKeyhole size={16} /><span>Remove restrictions</span></button>
                            <div className="decision-actions"><button className="button not-spam" disabled={locked} onClick={() => {
                                setDecision({ message: selected, isSpam: false }); setDialogError(''); setDialog('decision')
                            }}><ShieldCheck size={17} />Not spam</button>
                                <button className="button danger" disabled={locked} onClick={() => {
                                    setDecision({ message: selected, isSpam: true }); setDialogError(''); setDialog('decision')
                                }}><ShieldX size={17} />Spam</button></div>
                        </footer>
                    </> : <div className="detail-empty"><span className="empty-symbol">{hasLoaded && !continuationToken ? <CheckCheck size={36} strokeWidth={1.4} /> : <Inbox size={36} strokeWidth={1.4} />}</span>
                        <div className="eyebrow">{hasLoaded ? 'REVIEW QUEUE' : 'YOUR WORKSPACE'}</div>
                        <h2>{loading ? 'Fetching the queue' : hasLoaded ? continuationToken ? 'More messages available' : 'All clear for now' : 'Ready for review'}</h2>
                        <p>{loading ? 'Waiting for the Function...' : hasLoaded ? query?.chatId : 'Connect a Function and choose a chat.'}</p>
                        {!connection && <button className="button primary" onClick={() => { setDialogError(''); setDialog('connection') }}><Plug size={16} />Connect Function</button>}
                    </div>}
                </div>
            </section>

            {lastReview && <section className="recent-review" aria-label="Last review"><span className="recent-icon"><Check size={17} /></span>
                <div><span className="section-title">Last decision</span><p>{lastReview.message.userName || 'Unknown sender'}<span className="recent-divider">/</span><strong>{lastReview.isSpam ? 'Spam' : 'Not spam'}</strong><span className="recent-divider">/</span><code>#{lastReview.message.messageId}</code></p></div>
                {restoredUsers.includes(`${lastReview.message.chatId}:${lastReview.message.userId}`) ? <span className="restored-label"><Check size={15} />Restrictions removed</span>
                    : <button className="button subtle" disabled={!connection || locked} onClick={() => openRestrictions(lastReview.message)}><UnlockKeyhole size={16} />Remove restrictions</button>}
            </section>}
            <footer className="app-footer"><span>AntiSpam Telegram Bot</span><span>Human review workspace<span className="footer-dot" />Local session</span></footer>
        </main>

        {dialog === 'connection' && <Dialog title="Function connection" onClose={() => setDialog(null)}>
            <form onSubmit={connect} className="dialog-form" noValidate><label htmlFor="function-url">Function URL</label>
                <input id="function-url" type="url" required value={endpoint} onChange={event => setEndpoint(event.target.value)} placeholder="https://your-function.azurewebsites.net" autoComplete="off" spellCheck={false} />
                <label htmlFor="function-key">Function key <span className="optional">Optional for a local Function</span></label>
                <div className="password-wrap"><input id="function-key" type={showKey ? 'text' : 'password'} value={key} onChange={event => setKey(event.target.value)} autoComplete="off" spellCheck={false} />
                    <button type="button" className="icon-button" aria-label={showKey ? 'Hide key' : 'Show key'} title={showKey ? 'Hide key' : 'Show key'} onClick={() => setShowKey(!showKey)}>{showKey ? <EyeOff size={17} /> : <Eye size={17} />}</button></div>
                {dialogError && <p className="form-error" role="alert">{dialogError}</p>}
                <div className="dialog-actions"><button type="button" className="button subtle" onClick={() => setDialog(null)}>Cancel</button><button type="submit" className="button primary"><Plug size={16} />Connect</button></div>
            </form>
        </Dialog>}
        {dialog === 'decision' && decision && <Dialog title={decision.isSpam ? 'Confirm spam' : 'Return message to chat'} onClose={() => setDialog(null)} busy={working}>
            <div className={`confirmation-symbol ${decision.isSpam ? 'spam-symbol' : ''}`}>{decision.isSpam ? <ShieldX size={28} /> : <ShieldCheck size={28} />}</div>
            <p className="confirmation-title">{decision.message.userName || 'Unknown sender'}<span>Message #{decision.message.messageId}</span></p>
            <p className="confirmation-text">{decision.isSpam ? 'Confirm this message as spam?' : 'Mark as not spam and return the saved text to the chat?'}<br />Restrictions remain unchanged.</p>
            {dialogError && <p className="form-error" role="alert">{dialogError}</p>}
            <div className="dialog-actions"><button className="button subtle" disabled={working} onClick={() => setDialog(null)}>Cancel</button>
                <button className={`button ${decision.isSpam ? 'danger' : 'primary'}`} disabled={working || !!dialogError} onClick={() => void applyDecision()}>
                    {working && <LoaderCircle size={16} className="spin" />}Confirm {decision.isSpam ? 'spam' : 'not spam'}</button></div>
        </Dialog>}
        {dialog === 'restrictions' && <Dialog title="Remove restrictions" onClose={() => setDialog(null)} busy={working}>
            <form onSubmit={restore} className="dialog-form" noValidate><label htmlFor="restore-chat">Chat ID</label><input id="restore-chat" required value={restoreChat} onChange={event => setRestoreChat(event.target.value)} disabled={working} spellCheck={false} />
                <label htmlFor="restore-user">User ID</label><input id="restore-user" required value={restoreUser} onChange={event => setRestoreUser(event.target.value)} disabled={working} spellCheck={false} />
                {dialogError && <p className="form-error" role="alert">{dialogError}</p>}
                <div className="dialog-actions"><button type="button" className="button subtle" disabled={working} onClick={() => setDialog(null)}>Cancel</button>
                    <button type="submit" className="button primary" disabled={working}>{working ? <LoaderCircle size={16} className="spin" /> : <UnlockKeyhole size={16} />}Remove restrictions</button></div>
            </form>
        </Dialog>}
    </div>
}

export default App