import { useEffect, useRef, useState } from 'react'
import { Check, CircleAlert, Copy, History, ListChecks, LoaderCircle, LogOut, ShieldCheck, Sparkles, Terminal, UnlockKeyhole, X } from 'lucide-react'
import { getCommandCalls, getHistory, previewChronicle, validId } from './api/client'
import type { ReviewMessage } from './api/client'
import { DecisionDialog } from './components/DecisionDialog'
import { LastReview } from './components/LastReview'
import { MessageDetails } from './components/MessageDetails'
import { MessageQueue } from './components/MessageQueue'
import { QueryForm } from './components/QueryForm'
import { RestrictionsDialog } from './components/RestrictionsDialog'
import { RecordsTable } from './components/RecordsTable'
import { useTablePage } from './hooks/useTablePage'
import { useChats } from './hooks/useChats'
import { useModeration } from './hooks/useModeration'
import { useReviewQueue } from './hooks/useReviewQueue'
import { connection, errorText } from './lib/workspace'
import type { Notice, QueueQuery } from './lib/workspace'
import { currentAccount, signOut } from './lib/auth'
import './styles/App.css'

function App() {
    const [view, setView] = useState<'queue' | 'calls' | 'history'>('queue')
    const [notice, setNotice] = useState<Notice | null>(null)
    const [preview, setPreview] = useState<{ chatId: string; summary: string } | null>(null)
    const [generating, setGenerating] = useState(false)
    const previewRequest = useRef<AbortController | null>(null)
    useEffect(() => () => previewRequest.current?.abort(), [])
    const chats = useChats(connection, setNotice)
    const queue = useReviewQueue(connection, setNotice)
    const moderation = useModeration(connection, setNotice, queue.remove)
    const calls = useTablePage(connection, setNotice, getCommandCalls, row => `${row.chatId}:${row.command}`)
    const history = useTablePage(connection, setNotice, getHistory, row => `${row.chatId}:${row.messageId}`)
    const locked = queue.loading || moderation.working || chats.loading || calls.loading || history.loading || generating
    const records = view === 'history' ? history : calls
    const title = view === 'queue' ? 'Review queue' : view === 'calls' ? 'Command calls' : 'Message history'
    const hasMore = !!queue.continuationToken

    function load(query: QueueQuery) {
        if (locked) return
        if (view !== 'queue') { void records.load(query); return }
        if (!query.chatId || validId(query.chatId)) moderation.clearLastReview()
        void queue.load(query)
    }

    async function generateChronicle() {
        if (locked || previewRequest.current || !validId(chats.chatId)) return
        const chatId = chats.chatId
        const controller = new AbortController()
        previewRequest.current = controller
        setGenerating(true)
        setPreview(null)
        setNotice(null)
        try {
            const result = await previewChronicle(connection, chatId, controller.signal)
            if (controller.signal.aborted) return
            if (result.chatId !== chatId) throw new Error('Chronicle response belongs to a different chat.')
            if (result.skipped) setNotice({ error: false, text: 'Chronicle is on cooldown. Try again later.' })
            else if (result.summary) setPreview({ chatId, summary: result.summary })
            else throw new Error('Chronicle returned an empty response.')
        } catch (error) {
            if (!controller.signal.aborted) setNotice({ error: true, text: errorText(error) })
        } finally {
            previewRequest.current = null
            if (!controller.signal.aborted) setGenerating(false)
        }
    }

    function openRestrictions(message?: ReviewMessage) {
        moderation.openRestrictions({
            chatId: message?.chatId ?? queue.query?.chatId ?? chats.chatId,
            userId: message?.userId ?? '',
        })
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
                <span className="connection-status configured">
                    <span className="status-dot" />{currentAccount()?.name || 'Moderator'}
                </span>
                <button className="icon-button" title="Sign out" aria-label="Sign out" onClick={() => { void signOut() }}><LogOut size={16} /></button>
            </div>
        </header>

        <main>
            <nav className="workspace-tabs" role="tablist" aria-label="Workspace sections">
                <button role="tab" aria-selected={view === 'queue'} disabled={locked} onClick={() => { setView('queue'); setNotice(null) }}><ListChecks size={16} />Review queue</button>
                <button role="tab" aria-selected={view === 'calls'} disabled={locked} onClick={() => { setView('calls'); setNotice(null) }}><Terminal size={16} />Command calls</button>
                <button role="tab" aria-selected={view === 'history'} disabled={locked} onClick={() => { setView('history'); setNotice(null) }}><History size={16} />Message history</button>
            </nav>
            <section className="page-heading">
                <div><div className="eyebrow">TELEGRAM MODERATION</div><h1>{title}<span className="heading-dot">.</span></h1></div>
                {view === 'queue' && <button className="button subtle" onClick={() => openRestrictions(queue.selected ?? moderation.lastReview?.message)} disabled={locked}>
                    <UnlockKeyhole size={16} /><span>Remove restrictions</span>
                </button>}
                {view === 'history' && chats.chatId && <button className="button subtle chronicle-button" disabled={locked}
                    onClick={() => void generateChronicle()}>
                    {generating ? <LoaderCircle size={16} className="spin" /> : <Sparkles size={16} />}
                    <span>{generating ? 'Generating...' : 'Test Chronicle'}</span>
                </button>}
            </section>

            <QueryForm chats={chats.chats} chatId={chats.chatId} endpoint={connection.endpoint}
                busy={locked} chatsLoading={chats.loading} loading={view === 'queue' ? queue.loading : records.loading}
                submitLabel={view === 'queue' ? 'Load queue' : 'Load records'} itemLabel={view === 'calls' ? 'calls' : 'messages'}
                onChatChange={chatId => { chats.setChatId(chatId); setPreview(null) }} onRefresh={chats.refresh} onSubmit={load} />

            {notice && <div className={`notice ${notice.error ? 'error' : 'success'}`} role={notice.error ? 'alert' : 'status'}>
                {notice.error ? <CircleAlert size={17} /> : <Check size={17} />}<span>{notice.text}</span>
                <button className="icon-button" title="Dismiss notification" aria-label="Dismiss notification" onClick={() => setNotice(null)}><X size={16} /></button>
            </div>}

            {view === 'history' && preview?.chatId === chats.chatId && <section className="chronicle-preview" aria-label="Chronicle preview">
                <div className="chronicle-preview-heading"><div><h2>Chronicle</h2>
                    <span>{chats.chats.find(chat => chat.chatId === preview.chatId)?.name ?? preview.chatId}</span></div>
                    <div><button className="icon-button" title="Copy Chronicle" aria-label="Copy Chronicle" onClick={() => void copy(preview.summary)}><Copy size={16} /></button>
                        <button className="icon-button" title="Close Chronicle" aria-label="Close Chronicle" onClick={() => setPreview(null)}><X size={16} /></button></div>
                </div><div className="chronicle-preview-text">{preview.summary}</div>
            </section>}

            {view === 'queue' ? <section className="workspace" aria-label="Message review workspace">
                <MessageQueue messages={queue.messages} selected={queue.selected} query={queue.query}
                    loading={queue.loading} working={moderation.working} busy={locked} hasLoaded={queue.hasLoaded}
                    hasMore={hasMore} onSelect={queue.select} onLoadMore={queue.loadMore}
                    onRefresh={() => { if (queue.query) load(queue.query) }} />
                <MessageDetails message={queue.selected} busy={locked} loading={queue.loading}
                    hasLoaded={queue.hasLoaded} hasMore={hasMore} chatsLoading={chats.loading}
                    chatId={queue.query?.chatId} onCopy={copy} onRestrictions={openRestrictions}
                    onDecision={moderation.openDecision} />
            </section> : <RecordsTable kind={view} calls={calls.rows} messages={history.rows} chats={chats.chats}
                chatId={records.query?.chatId} loading={records.loading} busy={locked} hasLoaded={records.hasLoaded}
                hasMore={!!records.continuationToken} onLoadMore={records.loadMore}
                onRefresh={() => { if (records.query) void records.load(records.query) }} />}

            {view === 'queue' && moderation.lastReview && <LastReview decision={moderation.lastReview}
                restored={moderation.isRestored(moderation.lastReview.message)} busy={locked} onRestrictions={openRestrictions} />}
            <footer className="app-footer"><span>AntiSpam Telegram Bot</span><span>Human review workspace<span className="footer-dot" />Local session</span></footer>
        </main>

        {moderation.dialog?.kind === 'decision' && <DecisionDialog decision={moderation.dialog.decision}
            error={moderation.error} busy={moderation.working} onClose={moderation.close} onConfirm={moderation.confirm} />}
        {moderation.dialog?.kind === 'restrictions' && <RestrictionsDialog values={moderation.dialog.values}
            error={moderation.error} busy={moderation.working} onClose={moderation.close} onSubmit={moderation.restore} />}
    </div>
}

export default App