import { useState } from 'react'
import { Check, CircleAlert, ShieldCheck, UnlockKeyhole, X } from 'lucide-react'
import { validId } from './api/client'
import type { ReviewMessage } from './api/client'
import { DecisionDialog } from './components/DecisionDialog'
import { LastReview } from './components/LastReview'
import { MessageDetails } from './components/MessageDetails'
import { MessageQueue } from './components/MessageQueue'
import { QueryForm } from './components/QueryForm'
import { RestrictionsDialog } from './components/RestrictionsDialog'
import { useChats } from './hooks/useChats'
import { useModeration } from './hooks/useModeration'
import { useReviewQueue } from './hooks/useReviewQueue'
import { connection } from './lib/workspace'
import type { Notice, QueueQuery } from './lib/workspace'
import './styles/App.css'

function App() {
    const [notice, setNotice] = useState<Notice | null>(null)
    const chats = useChats(connection, setNotice)
    const queue = useReviewQueue(connection, setNotice)
    const moderation = useModeration(connection, setNotice, queue.remove)
    const locked = queue.loading || moderation.working || chats.loading
    const hasMore = !!queue.continuationToken

    function load(query: QueueQuery) {
        if (locked) return
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

    return <div className="app">
        <header className="topbar">
            <a className="brand" href="/" aria-label="AntiSpam review home">
                <span className="brand-mark"><ShieldCheck size={23} strokeWidth={1.8} /></span>
                <span>AntiSpam<span className="brand-divider">/</span><span className="brand-product">Review</span></span>
            </a>
            <div className="topbar-actions">
                <span className="connection-status configured">
                    <span className="status-dot" />Configured from environment
                </span>
            </div>
        </header>

        <main>
            <section className="page-heading">
                <div><div className="eyebrow">TELEGRAM MODERATION</div><h1>Review queue<span className="heading-dot">.</span></h1></div>
                <button className="button subtle" onClick={() => openRestrictions(queue.selected ?? moderation.lastReview?.message)} disabled={locked}>
                    <UnlockKeyhole size={16} /><span>Remove restrictions</span>
                </button>
            </section>

            <QueryForm chats={chats.chats} chatId={chats.chatId} endpoint={connection.endpoint}
                busy={locked} chatsLoading={chats.loading} loading={queue.loading}
                onChatChange={chats.setChatId} onRefresh={chats.refresh} onSubmit={load} />

            {notice && <div className={`notice ${notice.error ? 'error' : 'success'}`} role={notice.error ? 'alert' : 'status'}>
                {notice.error ? <CircleAlert size={17} /> : <Check size={17} />}<span>{notice.text}</span>
                <button className="icon-button" title="Dismiss notification" aria-label="Dismiss notification" onClick={() => setNotice(null)}><X size={16} /></button>
            </div>}

            <section className="workspace" aria-label="Message review workspace">
                <MessageQueue messages={queue.messages} selected={queue.selected} query={queue.query}
                    loading={queue.loading} working={moderation.working} busy={locked} hasLoaded={queue.hasLoaded}
                    hasMore={hasMore} onSelect={queue.select} onLoadMore={queue.loadMore}
                    onRefresh={() => { if (queue.query) load(queue.query) }} />
                <MessageDetails message={queue.selected} busy={locked} loading={queue.loading}
                    hasLoaded={queue.hasLoaded} hasMore={hasMore} chatsLoading={chats.loading}
                    chatId={queue.query?.chatId} onCopy={copy} onRestrictions={openRestrictions}
                    onDecision={moderation.openDecision} />
            </section>

            {moderation.lastReview && <LastReview decision={moderation.lastReview}
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