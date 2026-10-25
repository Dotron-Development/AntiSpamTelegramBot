import { useEffect, useRef, useState } from 'react'
import { getMessages, validId } from '../api/client'
import type { Connection, ReviewMessage } from '../api/client'
import { errorText, messageKey, rememberChat } from '../lib/workspace'
import type { Notify, QueueQuery } from '../lib/workspace'

export function useReviewQueue(connection: Connection, notify: Notify) {
    const [query, setQuery] = useState<QueueQuery | null>(null)
    const [messages, setMessages] = useState<ReviewMessage[]>([])
    const [selectedId, setSelectedId] = useState<string | null>(null)
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
            setMessages([])
            setSelectedId(null)
            setContinuationToken(null)
            setHasLoaded(false)
            setQuery(target)
        }
        try {
            const page = await getMessages(connection, target.chatId, target.pageSize,
                append ? continuationToken : null, controller.signal)
            if (controller.signal.aborted) return
            setMessages(previous => {
                const combined = append ? [...previous, ...page.messages] : page.messages
                return Array.from(new Map(combined.map(message => [messageKey(message), message])).values())
            })
            setContinuationToken(page.continuationToken)
            setHasLoaded(true)
            rememberChat(target.chatId)
        } catch (error) {
            if (!controller.signal.aborted) notify({ error: true, text: errorText(error) })
        } finally {
            if (!controller.signal.aborted) setLoading(false)
        }
    }

    function remove(message: ReviewMessage) {
        setMessages(previous => previous.filter(item => messageKey(item) !== messageKey(message)))
    }

    function loadMore() {
        if (query && continuationToken) void load(query, true)
    }

    return {
        query, messages, selected: messages.find(message => messageKey(message) === selectedId) ?? messages[0],
        select: setSelectedId, continuationToken, loading, hasLoaded, load, loadMore, remove,
    }
}
