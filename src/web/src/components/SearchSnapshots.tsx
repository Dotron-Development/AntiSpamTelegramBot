import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { RefreshCw } from 'lucide-react'
import { getSearchSnapshots, resetSearchSnapshot, validId } from '../api/client'
import type { Connection, SearchSnapshot } from '../api/client'
import { errorText } from '../lib/workspace'

export function SearchSnapshots({ connection }: { connection: Connection }) {
    const [items, setItems] = useState<SearchSnapshot[]>([])
    const [refresh, setRefresh] = useState(0)
    const [loading, setLoading] = useState(true)
    const [queued, setQueued] = useState('')
    const [working, setWorking] = useState(false)
    const [error, setError] = useState('')
    const [channelId, setChannelId] = useState('')

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

    function submit(event: FormEvent<HTMLFormElement>) {
        event.preventDefault()
        void reset(channelId.trim())
    }

    const id = channelId.trim()
    return <section className="records-panel" aria-label="Search snapshots">
        <div className="records-toolbar">
            <span>{items.length} blobs</span>
            <button className="icon-button" aria-label="Refresh snapshots" title="Refresh snapshots" disabled={loading}
                onClick={() => { setLoading(true); setError(''); setRefresh(value => value + 1) }}><RefreshCw size={16} /></button>
        </div>
        <form className="records-toolbar" onSubmit={submit}>
            <label>Channel ID <input value={channelId} onChange={event => setChannelId(event.target.value)} placeholder="-100…" /></label>
            <button className="button subtle" disabled={working || !id.startsWith('-') || !validId(id)}>Queue rebuild</button>
        </form>
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
