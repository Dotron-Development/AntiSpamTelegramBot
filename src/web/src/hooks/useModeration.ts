import { useRef, useState } from 'react'
import { ApiError, getMessages, removeRestrictions, reviewMessage } from '../api/client'
import type { Connection, ReviewMessage } from '../api/client'
import { errorText, messageKey } from '../lib/workspace'
import type { Decision, Notify, QueueQuery, RestrictionValues } from '../lib/workspace'

type ModerationDialog =
    | { kind: 'decision'; decision: Decision }
    | { kind: 'restrictions'; values: RestrictionValues }
    | { kind: 'batch'; query: QueueQuery }

export function useModeration(connection: Connection, notify: Notify, remove: (message: ReviewMessage) => void,
    completeBatch: () => void) {
    const [dialog, setDialog] = useState<ModerationDialog | null>(null)
    const [working, setWorking] = useState(false)
    const [error, setError] = useState('')
    const [lastReview, setLastReview] = useState<Decision | null>(null)
    const [restoredUsers, setRestoredUsers] = useState<string[]>([])
    const [batchProgress, setBatchProgress] = useState<{ completed: number; total: number } | null>(null)
    const pending = useRef(false)

    function open(next: ModerationDialog) {
        if (pending.current) return
        setError('')
        setBatchProgress(null)
        setDialog(next)
    }

    function close() {
        if (!pending.current) setDialog(null)
    }

    async function run(action: () => Promise<void>) {
        if (pending.current) return
        pending.current = true
        setWorking(true)
        setError('')
        try { await action() }
        finally {
            pending.current = false
            setWorking(false)
        }
    }

    async function confirm() {
        if (dialog?.kind !== 'decision' || error) return
        const { decision } = dialog
        await run(async () => {
            try {
                await reviewMessage(connection, decision.message, decision.isSpam)
                remove(decision.message)
                setLastReview(decision)
                notify({ error: false, text: decision.isSpam ? 'Marked as spam.' : 'Marked as not spam. Saved text returned to chat.' })
                setDialog(null)
            } catch (error) {
                setError(`${errorText(error)} The action may already have completed. Refresh before retrying; a repeated not-spam decision can republish text.`)
                if (error instanceof ApiError && error.status === 404)
                    notify({ error: true, text: 'This message is no longer in the queue. Refresh to update the list.' })
            }
        })
    }

    async function restore(values: RestrictionValues) {
        await run(async () => {
            try {
                await removeRestrictions(connection, values.chatId, values.userId)
                setRestoredUsers(previous => [...previous, `${values.chatId}:${values.userId}`])
                notify({ error: false, text: `Restrictions removed for user ${values.userId}.` })
                setDialog(null)
            } catch (error) { setError(errorText(error)) }
        })
    }

    async function confirmBatch() {
        if (dialog?.kind !== 'batch' || error || batchProgress) return
        const { query } = dialog
        await run(async () => {
            let completed = 0
            let total = 0
            let prepared = false
            try {
                const messages = new Map<string, ReviewMessage>()
                const tokens = new Set<string>()
                let token: string | null = null
                do {
                    const page = await getMessages(connection, query.chatId, query.pageSize, token)
                    for (const message of page.messages)
                        messages.set(messageKey(message), message)
                    token = page.continuationToken
                    if (token) {
                        if (tokens.has(token)) throw new Error('The queue returned a repeated continuation token.')
                        tokens.add(token)
                    }
                } while (token)

                total = messages.size
                prepared = true
                setBatchProgress({ completed, total })
                setLastReview(null)
                for (const message of messages.values()) {
                    await reviewMessage(connection, message, true)
                    remove(message)
                    completed++
                    setBatchProgress({ completed, total })
                }
                completeBatch()
                notify({ error: false, text: `Marked ${completed} messages as spam.` })
            } catch (error) {
                setError(prepared
                    ? `${errorText(error)} Batch stopped at ${completed}/${total}. The failed message may already have been marked as spam. Close this dialog and refresh before trying again.`
                    : `${errorText(error)} Could not load the whole queue. No spam decisions were sent.`)
            }
        })
    }

    return {
        dialog, working, error, lastReview, batchProgress,
        openDecision: (message: ReviewMessage, isSpam: boolean) => open({ kind: 'decision', decision: { message, isSpam } }),
        openRestrictions: (values: RestrictionValues) => open({ kind: 'restrictions', values }),
        openBatch: (query: QueueQuery) => open({ kind: 'batch', query: { ...query } }),
        close, confirm, confirmBatch, restore, clearLastReview: () => setLastReview(null),
        isRestored: (message: ReviewMessage) => restoredUsers.includes(`${message.chatId}:${message.userId}`),
    }
}
