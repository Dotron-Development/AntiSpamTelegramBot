import { useEffect, useRef, useState } from 'react'
import type { Connection, TablePage } from '../api/client'
import { errorText } from '../lib/workspace'
import type { Notify } from '../lib/workspace'

export function useTablePage<T>(connection: Connection, notify: Notify,
    fetchPage: (connection: Connection, chatId: string, token: string | null, signal: AbortSignal) => Promise<TablePage<T>>,
    rowKey: (row: T) => string) {
    const [loadedChatId, setLoadedChatId] = useState('')
    const [rows, setRows] = useState<T[]>([])
    const [continuationToken, setContinuationToken] = useState<string | null>(null)
    const [loading, setLoading] = useState(false)
    const [hasLoaded, setHasLoaded] = useState(false)
    const pending = useRef<AbortController | null>(null)

    useEffect(() => () => pending.current?.abort(), [])

    async function load(chatId: string, token: string | null = null) {
        pending.current?.abort()
        const controller = new AbortController()
        pending.current = controller
        setLoading(true)
        notify(null)
        if (!token) {
            setRows([])
            setContinuationToken(null)
            setHasLoaded(false)
            setLoadedChatId(chatId)
        }
        try {
            const page = await fetchPage(connection, chatId, token, controller.signal)
            if (controller.signal.aborted) return
            setRows(previous => Array.from(new Map((token ? [...previous, ...page.rows] : page.rows)
                .map(row => [rowKey(row), row])).values()))
            setContinuationToken(page.continuationToken)
            setHasLoaded(true)
        } catch (error) {
            if (!controller.signal.aborted) notify({ error: true, text: errorText(error) })
        } finally {
            if (!controller.signal.aborted) setLoading(false)
        }
    }

    function loadMore() {
        if (loadedChatId && continuationToken && !loading) void load(loadedChatId, continuationToken)
    }

    function reset() {
        pending.current?.abort()
        setRows([])
        setLoadedChatId('')
        setContinuationToken(null)
        setLoading(false)
        setHasLoaded(false)
    }

    function remove(row: T) {
        const key = rowKey(row)
        setRows(previous => previous.filter(item => rowKey(item) !== key))
    }

    return { rows, loadedChatId, continuationToken, loading, hasLoaded, load, loadMore, reset, remove }
}