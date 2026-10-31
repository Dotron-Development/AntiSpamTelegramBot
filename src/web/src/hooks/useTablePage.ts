import { useEffect, useRef, useState } from 'react'
import { validId } from '../api/client'
import type { Connection, TablePage } from '../api/client'
import { errorText, rememberChat } from '../lib/workspace'
import type { Notify, QueueQuery } from '../lib/workspace'

export function useTablePage<T>(connection: Connection, notify: Notify,
    fetchPage: (connection: Connection, chatId: string, pageSize: number, token: string | null, signal?: AbortSignal) => Promise<TablePage<T>>,
    rowKey: (row: T) => string) {
    const [query, setQuery] = useState<QueueQuery | null>(null)
    const [rows, setRows] = useState<T[]>([])
    const [continuationToken, setContinuationToken] = useState<string | null>(null)
    const [loading, setLoading] = useState(false)
    const [hasLoaded, setHasLoaded] = useState(false)
    const pending = useRef<AbortController | null>(null)

    useEffect(() => () => pending.current?.abort(), [])

    async function load(target: QueueQuery, append = false) {
        if (target.chatId && !validId(target.chatId)) {
            notify({ error: true, text: 'Select a valid chat from the list.' })
            return
        }
        pending.current?.abort()
        const controller = new AbortController()
        pending.current = controller
        setLoading(true)
        notify(null)
        if (!append) {
            setRows([])
            setContinuationToken(null)
            setHasLoaded(false)
            setQuery(target)
        }
        try {
            const page = await fetchPage(connection, target.chatId, target.pageSize,
                append ? continuationToken : null, controller.signal)
            if (controller.signal.aborted) return
            setRows(previous => Array.from(new Map((append ? [...previous, ...page.rows] : page.rows)
                .map(row => [rowKey(row), row])).values()))
            setContinuationToken(page.continuationToken)
            setHasLoaded(true)
            rememberChat(target.chatId)
        } catch (error) {
            if (!controller.signal.aborted) notify({ error: true, text: errorText(error) })
        } finally {
            if (!controller.signal.aborted) setLoading(false)
        }
    }

    function loadMore() {
        if (query && continuationToken && !loading) void load(query, true)
    }

    return { rows, query, continuationToken, loading, hasLoaded, load, loadMore }
}