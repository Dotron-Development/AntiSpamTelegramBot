import { useCallback, useEffect, useRef, useState } from 'react'
import { getChats } from '../api/client'
import type { Connection, ReviewChat } from '../api/client'
import { errorText, readChatId } from '../lib/workspace'
import type { Notify } from '../lib/workspace'

export function useChats(connection: Connection, notify: Notify) {
    const [chatId, setChatId] = useState(readChatId)
    const [chats, setChats] = useState<ReviewChat[]>([])
    const [loading, setLoading] = useState(true)
    const pending = useRef<AbortController | null>(null)

    const refresh = useCallback(async () => {
        pending.current?.abort()
        const controller = new AbortController()
        pending.current = controller
        setLoading(true)
        notify(null)
        try {
            const catalog = new Map<string, ReviewChat>()
            const tokens = new Set<string>()
            let token: string | null = null
            do {
                const page = await getChats(connection, token, controller.signal)
                for (const chat of page.chats) catalog.set(chat.chatId, chat)
                token = page.continuationToken
                if (token && tokens.has(token)) throw new Error('The Function returned a repeated chat continuation token.')
                if (token) tokens.add(token)
            } while (token && !controller.signal.aborted)
            if (controller.signal.aborted) return
            const items = [...catalog.values()].sort((left, right) => left.name.localeCompare(right.name, 'en'))
            setChats(items)
            setChatId(previous => catalog.has(previous) ? previous : '')
        } catch (error) {
            if (!controller.signal.aborted) notify({ error: true, text: errorText(error) })
        } finally {
            if (!controller.signal.aborted) setLoading(false)
        }
    }, [connection, notify])

    useEffect(() => {
        const startup = setTimeout(() => void refresh(), 0)
        return () => {
            clearTimeout(startup)
            pending.current?.abort()
        }
    }, [refresh])

    return { chats, chatId, setChatId, loading, refresh }
}
