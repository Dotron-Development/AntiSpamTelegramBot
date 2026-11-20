import { useRef, useState } from 'react'
import { ArrowDown, Check, CircleAlert, Database, History, ListChecks, LoaderCircle, Menu, RefreshCw, ShieldCheck, Terminal, Trash2, X } from 'lucide-react'
import { clearChatHistory, getCommandCalls, getGroupSnapshot, getHistory, getMessages, resetSearchSnapshot } from './api/client'
import type { Connection } from './api/client'
import { ChatPicker } from './components/ChatPicker'
import { DecisionDialog } from './components/DecisionDialog'
import { ClearHistoryDialog } from './components/ClearHistoryDialog'
import { MessageQueue } from './components/MessageQueue'
import { SearchSnapshots } from './components/SearchSnapshots'
import { RecordsTable } from './components/RecordsTable'
import { useTablePage } from './hooks/useTablePage'
import { useChats } from './hooks/useChats'
import { useModeration } from './hooks/useModeration'
import { errorText, messageKey, rememberChat } from './lib/workspace'
import type { Notice } from './lib/workspace'
import './styles/App.css'

const sections = {
    queue: { title: 'Review queue', icon: ListChecks },
    calls: { title: 'Command calls', icon: Terminal },
    history: { title: 'Message history', icon: History },
    snapshots: { title: 'Snapshots', icon: Database },
}

function App({ connection }: { connection: Connection }) {
    const [view, setView] = useState<keyof typeof sections>('queue')
    const [menuOpen, setMenuOpen] = useState(false)
    const [notice, setNotice] = useState<Notice | null>(null)
    const [clearChatId, setClearChatId] = useState('')
    const [clearError, setClearError] = useState('')
    const [clearWorking, setClearWorking] = useState(false)
    const [rebuilding, setRebuilding] = useState(false)
    const clearInFlight = useRef(false)
    const rebuildInFlight = useRef(false)
    const menuButton = useRef<HTMLButtonElement>(null)
    const chats = useChats(connection, setNotice)
    const queue = useTablePage(connection, setNotice, getMessages, messageKey)
    const moderation = useModeration(connection, setNotice, queue.remove)
    const calls = useTablePage(connection, setNotice, getCommandCalls, row => `${row.chatId}:${row.command}`)
    const history = useTablePage(connection, setNotice, getHistory, messageKey)
    const snapshots = useTablePage(connection, setNotice, (api, groupId, _, signal) => getGroupSnapshot(api, groupId, signal), row => row.channelId)
    const records = { queue, calls, history, snapshots }[view]
    const group = view === 'snapshots'
    const options = group ? chats.chats.filter(chat => chat.chatId.startsWith('-')) : chats.chats
    const chatId = options.some(chat => chat.chatId === chats.chatId) ? chats.chatId : ''
    const current = !!chatId && records.loadedChatId === chatId
    const locked = records.loading || moderation.working || chats.loading || clearWorking || rebuilding
    const snapshot = current ? snapshots.rows[0] : undefined

    function selectChat(value: string) {
        setNotice(null)
        chats.setChatId(value)
        rememberChat(value)
        for (const list of [queue, calls, history, snapshots]) list.reset()
    }

    function closeMenu() {
        setMenuOpen(false)
        menuButton.current?.focus()
    }

    async function rebuildSnapshot(channelId: string) {
        if (rebuildInFlight.current) return
        rebuildInFlight.current = true
        setRebuilding(true)
        setNotice(null)
        try {
            await resetSearchSnapshot(connection, channelId)
            setNotice({ error: false, text: 'Rebuild queued. Load the snapshot again when it is ready.' })
        } catch (error) {
            setNotice({ error: true, text: errorText(error) })
        } finally {
            rebuildInFlight.current = false
            setRebuilding(false)
        }
    }

    async function confirmClearHistory() {
        if (clearInFlight.current || !clearChatId || clearError) return
        const target = clearChatId
        clearInFlight.current = true
        setClearWorking(true)
        try {
            await clearChatHistory(connection, target)
            history.reset()
            setClearChatId('')
            setNotice({ error: false, text: `Message history cleared for chat ${target}.` })
        } catch (error) {
            setClearError(`${errorText(error)} Deletion may be partial. Close this dialog and reload history before trying again.`)
        } finally {
            clearInFlight.current = false
            setClearWorking(false)
        }
    }

    return <div className="app">
        <header className="topbar" onKeyDown={event => { if (menuOpen && event.key === 'Escape') closeMenu() }}>
            <span className="brand">
                <span className="brand-mark"><ShieldCheck size={23} strokeWidth={1.8} /></span>
                <span>AntiSpam<span className="brand-divider">/</span><span className="brand-product">Review</span></span>
            </span>
            <button ref={menuButton} className="icon-button menu-toggle" type="button" aria-label="Menu"
                aria-expanded={menuOpen} aria-controls="workspace-menu" onClick={() => setMenuOpen(value => !value)}>
                {menuOpen ? <X size={23} /> : <Menu size={23} />}
            </button>
            <nav className="workspace-menu" id="workspace-menu" aria-label="Workspace sections" hidden={!menuOpen}>
                {(Object.keys(sections) as (keyof typeof sections)[]).map(key => {
                    const { icon: Icon, title } = sections[key]
                    return <button type="button" key={key} aria-current={view === key ? 'page' : undefined}
                        disabled={locked} onClick={() => { setView(key); setNotice(null); closeMenu() }}><Icon size={19} />{title}</button>
                })}
                <button type="button" disabled={locked} onClick={() => { closeMenu(); void chats.refresh() }}><RefreshCw size={19} />Refresh chats</button>
            </nav>
        </header>

        <main>
            <section className="page-heading"><h1>{sections[view].title}</h1></section>
            <ChatPicker chats={options} chatId={chatId} disabled={locked} chatsLoading={chats.loading}
                loading={records.loading} group={group} onChatChange={selectChat}
                onLoad={() => { if (!locked && chatId) void records.load(chatId) }} />

            {notice && <div className={`notice ${notice.error ? 'error' : 'success'}`} role={notice.error ? 'alert' : 'status'}>
                {notice.error ? <CircleAlert size={18} /> : <Check size={18} />}<span>{notice.text}</span>
                <button className="icon-button" title="Dismiss notification" aria-label="Dismiss notification" onClick={() => setNotice(null)}><X size={18} /></button>
            </div>}

            <section aria-label={`${sections[view].title} records`} aria-busy={records.loading}>
                {current && records.rows.length > 0 && (view === 'queue'
                    ? <MessageQueue messages={queue.rows} disabled={locked} onDecision={moderation.open} />
                    : group ? snapshot && <SearchSnapshots {...snapshot} disabled={locked} working={rebuilding}
                        onRebuild={() => void rebuildSnapshot(snapshot.channelId)} />
                        : <RecordsTable kind={view} calls={calls.rows} messages={history.rows} />)}
                {records.loading ? <p className="list-status" role="status"><LoaderCircle size={18} className="spin" />Loading…</p>
                    : !current || !records.hasLoaded ? <p className="list-status">No records loaded.</p>
                        : !records.rows.length && <p className="list-status">No records found.</p>}
                {current && records.continuationToken && <div className="list-actions">
                    <button className="button subtle" disabled={locked} onClick={records.loadMore}><ArrowDown size={18} />Load more</button>
                </div>}
            </section>

            {view === 'history' && <div className="list-actions">
                <button className="button danger" disabled={locked || !chatId}
                    onClick={() => { setClearError(''); setClearChatId(chatId) }}><Trash2 size={18} />DANGER: Clear chat history</button>
            </div>}
        </main>

        {moderation.decision && <DecisionDialog decision={moderation.decision} error={moderation.error}
            busy={moderation.working} onClose={moderation.close} onConfirm={moderation.confirm} />}
        {clearChatId && <ClearHistoryDialog chatId={clearChatId} error={clearError} busy={clearWorking}
            onClose={() => { if (!clearInFlight.current) setClearChatId('') }} onConfirm={confirmClearHistory} />}
    </div>
}

export default App