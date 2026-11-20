import { LoaderCircle, RefreshCw } from 'lucide-react'
import type { SearchSnapshot } from '../api/client'

export function SearchSnapshots({ channelId, snapshot, disabled, working, onRebuild }: {
    channelId: string; snapshot?: SearchSnapshot; disabled: boolean; working: boolean; onRebuild: () => void
}) {
    return <article className="card">
        <h2>Linked channel</h2>
        <p><code>{channelId}</code></p>
        {snapshot ? <p className="record-meta">
            {(snapshot.sizeBytes / 1048576).toFixed(2)} MiB · <time dateTime={snapshot.lastModified}>
                {new Date(snapshot.lastModified).toISOString().replace('T', ' ').slice(0, 19)} UTC
            </time>
        </p> : <p className="record-meta">No snapshot published yet.</p>}
        <button className="button subtle" disabled={disabled} onClick={onRebuild}>
            {working ? <LoaderCircle size={18} className="spin" /> : <RefreshCw size={18} />}Queue rebuild
        </button>
    </article>
}
