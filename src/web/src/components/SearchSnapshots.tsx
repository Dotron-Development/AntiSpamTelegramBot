import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { RefreshCw } from 'lucide-react'
import { getSearchSnapshotChannel, getSearchSnapshots, resetSearchSnapshot, validId } from '../api/client'
import type { Connection, ReviewChat, SearchSnapshot } from '../api/client'
import { errorText } from '../lib/workspace'
import { ChatPicker } from './ChatPicker'

export function SearchSnapshots({ connection, chats, chatsLoading, onRefreshChats }: {
    connection: Connection; chats: ReviewChat[]; chatsLoading: boolean; onRefreshChats: () => Promise<void>
}) {
    const [items, setItems] = useState<SearchSnapshot[]>([])
    const [refresh, setRefresh] = useState(0)
    const [loading, setLoading] = useState(true)
    const [queued, setQueued] = useState('')
    const [working, setWorking] = useState(false)
    const [error, setError] = useState('')
    const [manualId, setManualId] = useState('')
    const [selectedGroup, setSelectedGroup] = useState('')
    const [linkRefresh, setLinkRefresh] = useState(0)
    const [linked, setLinked] = useState<{ groupId: string; channelId: string; error?: string } | null>(null)
    const groups = chats.filter(chat => chat.chatId.startsWith('-') && validId(chat.chatId))
    const groupId = groups.some(chat => chat.chatId === selectedGroup) ? selectedGroup : ''
    const link = groupId && linked?.groupId === groupId ? linked : null

    useEffect(() => {
        if (!groupId) return
        const abort = new AbortController()
        void getSearchSnapshotChannel(connection, groupId, abort.signal).then(channelId => {
            if (!abort.signal.aborted) setLinked({ groupId, channelId })
        }).catch(cause => {
            if (!abort.signal.aborted) setLinked({ groupId, channelId: '', error: errorText(cause) })
        })
        return () => abort.abort()
    }, [connection, groupId, linkRefresh])

    useEffect(() => {
        const abort = new AbortController()
        void getSearchSnapshots(connection, abort.signal).then(rows => {
            if (!abort.signal.aborted) setItems(rows)
        }).catch(cause => {
            if (!abort.signal.aborted) setError(errorText(cause))
        }).finally(() => {
            if (!abort.signal.aborted) setLoading(false)
        })
        return () => abort.abort()
    }, [connection, refresh])

    async function reset(id: string) {
        setWorking(true)
        setQueued('')
        setError('')
        try {
            await resetSearchSnapshot(connection, id)
            setQueued(`Rebuild queued for ${id}. Refresh to see the updated blob when it is ready.`)
        } catch (cause) {
            setError(errorText(cause))
        } finally {
            setWorking(false)
        }
    }

    const id = groupId ? (link?.channelId ?? '') : manualId.trim()
    const canRebuild = !working && id.startsWith('-') && validId(id) && (!groupId || !chatsLoading)

    function submit(event: FormEvent<HTMLFormElement>) {
        event.preventDefault()
        if (canRebuild) void reset(id)
    }

    return <section className="records-panel" aria-label="Search snapshots">
        <div className="records-toolbar">
            <span>{items.length} blobs</span>
            <button className="icon-button" aria-label="Refresh snapshots" title="Refresh snapshots" disabled={loading}
                onClick={() => { setLoading(true); setError(''); setRefresh(value => value + 1) }}><RefreshCw size={16} /></button>
        </div>
        <form className="querybar" onSubmit={submit}>
            <ChatPicker chats={groups} chatId={groupId} busy={working || chatsLoading} chatsLoading={chatsLoading}
                label="Discussion group" emptyLabel="Enter channel ID manually"
                onRefresh={async () => {
                    setLinked(null)
                    setLinkRefresh(value => value + 1)
                    await onRefreshChats()
                }} onChatChange={value => { setLinked(null); setSelectedGroup(value) }} />
            <div className="chat-field"><label htmlFor="snapshot-channel-id">Channel ID</label>
                <input id="snapshot-channel-id" value={groupId ? (link?.channelId ?? '') : manualId} readOnly={!!groupId} disabled={working}
                    onChange={event => setManualId(event.target.value)} placeholder={groupId && !link ? 'Looking up linked channel…' : '-100…'} />
            </div>
            <button className="button subtle" disabled={!canRebuild}>Queue rebuild</button>
        </form>
        {link?.error && <div className="notice error" role="alert">{link.error}</div>}
        {error && <div className="notice error" role="alert">{error}</div>}
        {queued && <div className="notice success" role="status">{queued}</div>}
        {items.length > 0 && <div className="records-scroll"><table className="calls-table">
            <thead><tr><th scope="col">Blob name / channel ID</th><th scope="col">Size</th><th scope="col">Last modified (UTC)</th><th scope="col">Action</th></tr></thead>
            <tbody>{items.map(blob => <tr key={blob.name}>
                <td><code>{blob.name}</code></td>
                <td title={`${blob.sizeBytes.toLocaleString()} bytes`}>{(blob.sizeBytes / 1048576).toFixed(2)} MiB</td>
                <td><time dateTime={blob.lastModified}>{new Date(blob.lastModified).toISOString().replace('T', ' ').slice(0, 19)}</time></td>
                <td><button className="button subtle" disabled={working || !blob.name.startsWith('-') || !validId(blob.name)}
                    onClick={() => { void reset(blob.name) }}>Rebuild</button></td>
            </tr>)}</tbody>
        </table></div>}
        {loading && <div className="records-loading" role="status">Loading snapshots…</div>}
        {!loading && !error && items.length === 0 && <div className="records-empty">No snapshots published yet.</div>}
    </section>
}
