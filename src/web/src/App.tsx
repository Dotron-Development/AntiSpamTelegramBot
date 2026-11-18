import { useRef, useState } from 'react'
import { Check, CircleAlert, History, ListChecks, LogOut, ShieldCheck, Sparkles, Terminal, UnlockKeyhole, X } from 'lucide-react'
import { clearChatHistory, getCommandCalls, getHistory, validId } from './api/client'
import type { ReviewMessage } from './api/client'
import { DecisionDialog } from './components/DecisionDialog'
import { ClearHistoryDialog } from './components/ClearHistoryDialog'
import { BatchSpamDialog } from './components/BatchSpamDialog'
import { LastReview } from './components/LastReview'
import { MessageDetails } from './components/MessageDetails'
import { MessageQueue } from './components/MessageQueue'
import { MentionPreview } from './components/MentionPreview'
import { QueryForm } from './components/QueryForm'
import { RestrictionsDialog } from './components/RestrictionsDialog'
import { SearchSnapshots } from './components/SearchSnapshots'
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
    const [view, setView] = useState<'queue' | 'calls' | 'history' | 'mention' | 'snapshots'>('queue')
    const [notice, setNotice] = useState<Notice | null>(null)
    const [mentionGenerating, setMentionGenerating] = useState(false)
    const [clearChatId, setClearChatId] = useState<string | null>(null)
    const [clearError, setClearError] = useState('')
    const [clearWorking, setClearWorking] = useState(false)
    const clearInFlight = useRef(false)
    const chats = useChats(connection, setNotice)
    const queue = useReviewQueue(connection, setNotice)
    const moderation = useModeration(connection, setNotice, queue.remove, queue.completeBatch)
    const calls = useTablePage(connection, setNotice, getCommandCalls, row => `${row.chatId}:${row.command}`)
    const history = useTablePage(connection, setNotice, getHistory, row => `${row.chatId}:${row.messageId}`)
    const locked = queue.loading || moderation.working || chats.loading || calls.loading || history.loading || mentionGenerating || clearWorking
    const records = view === 'history' ? history : calls
    const title = view === 'queue' ? 'Review queue' : view === 'calls' ? 'Command calls' : view === 'history' ? 'Message history' : view === 'snapshots' ? 'Search snapshots' : 'Test mention command'
    const hasMore = !!queue.continuationToken

    function load(query: QueueQuery) {
        if (locked) return
        if (view !== 'queue') { void records.load(query); return }
        if (!query.chatId || validId(query.chatId)) moderation.clearLastReview()
        void queue.load(query)
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

    async function confirmClearHistory() {
        if (clearInFlight.current || !clearChatId || clearError) return
        const target = clearChatId
        clearInFlight.current = true
        setClearWorking(true)
        try {
            await clearChatHistory(connection, target)
            history.reset()
            setClearChatId(null)
            setNotice({ error: false, text: `Message history cleared for chat ${target}.` })
        } catch (error) {
            setClearError(`${errorText(error)} Deletion may be partial. Close this dialog and reload history before trying again.`)
        } finally {
            clearInFlight.current = false
            setClearWorking(false)
        }
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
                <button role="tab" aria-selected={view === 'mention'} disabled={locked} onClick={() => { setView('mention'); setNotice(null) }}><Sparkles size={16} />Test mention</button>
                <button role="tab" aria-selected={view === 'snapshots'} disabled={locked} onClick={() => { setView('snapshots'); setNotice(null) }}>Search snapshots</button>
            </nav>
            <section className="page-heading">
                <div><div className="eyebrow">TELEGRAM MODERATION</div><h1>{title}<span className="heading-dot">.</span></h1></div>
                {view === 'queue' && <div className="page-heading-actions"><button className="button subtle" onClick={() => openRestrictions(queue.selected ?? moderation.lastReview?.message)} disabled={locked}>
                    <UnlockKeyhole size={16} /><span>Remove restrictions</span>
                </button>
                    <button className="button danger" disabled={locked || !queue.hasLoaded || !queue.query || (!queue.messages.length && !hasMore)}
                        onClick={() => { if (queue.query) moderation.openBatch(queue.query) }}>
                        <CircleAlert size={16} /><span>Mark all as spam</span>
                    </button>
                </div>}
                {view === 'history' && <button className="button danger" disabled={locked || !validId(chats.chatId)}
                    onClick={() => { setClearError(''); setClearChatId(chats.chatId) }}>
                    <CircleAlert size={16} /><span>DANGER: Clear chat history</span>
                </button>}
            </section>

            {view !== 'mention' && view !== 'snapshots' && <QueryForm chats={chats.chats} chatId={chats.chatId} endpoint={connection.endpoint}
                busy={locked} chatsLoading={chats.loading} loading={view === 'queue' ? queue.loading : records.loading}
                submitLabel={view === 'queue' ? 'Load queue' : 'Load records'} itemLabel={view === 'calls' ? 'calls' : 'messages'}
                onChatChange={chats.setChatId} onRefresh={chats.refresh} onSubmit={load} />}

            {notice && <div className={`notice ${notice.error ? 'error' : 'success'}`} role={notice.error ? 'alert' : 'status'}>
                {notice.error ? <CircleAlert size={17} /> : <Check size={17} />}<span>{notice.text}</span>
                <button className="icon-button" title="Dismiss notification" aria-label="Dismiss notification" onClick={() => setNotice(null)}><X size={16} /></button>
            </div>}

            {view === 'snapshots' ? <SearchSnapshots connection={connection} />
                : view === 'mention' ? <MentionPreview connection={connection} busy={locked} onBusyChange={setMentionGenerating} onCopy={copy} />
                : view === 'queue' ? <section className="workspace" aria-label="Message review workspace">
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
        {moderation.dialog?.kind === 'batch' && <BatchSpamDialog query={moderation.dialog.query}
            progress={moderation.batchProgress} error={moderation.error} busy={moderation.working}
            onClose={moderation.close} onConfirm={moderation.confirmBatch} />}
        {clearChatId && <ClearHistoryDialog chatId={clearChatId} error={clearError} busy={clearWorking}
            onClose={() => { if (!clearInFlight.current) setClearChatId(null) }} onConfirm={confirmClearHistory} />}
    </div>
}

export default App