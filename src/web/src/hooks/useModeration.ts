import { useRef, useState } from 'react'
import { ApiError, removeRestrictions, reviewMessage } from '../api/client'
import type { Connection, ReviewMessage } from '../api/client'
import { errorText } from '../lib/workspace'
import type { Decision, Notify, RestrictionValues } from '../lib/workspace'

type ModerationDialog =
    | { kind: 'decision'; decision: Decision }
    | { kind: 'restrictions'; values: RestrictionValues }

export function useModeration(connection: Connection, notify: Notify, remove: (message: ReviewMessage) => void) {
    const [dialog, setDialog] = useState<ModerationDialog | null>(null)
    const [working, setWorking] = useState(false)
    const [error, setError] = useState('')
    const [lastReview, setLastReview] = useState<Decision | null>(null)
    const [restoredUsers, setRestoredUsers] = useState<string[]>([])
    const pending = useRef(false)

    function open(next: ModerationDialog) {
        if (pending.current) return
        setError('')
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

    return {
        dialog, working, error, lastReview,
        openDecision: (message: ReviewMessage, isSpam: boolean) => open({ kind: 'decision', decision: { message, isSpam } }),
        openRestrictions: (values: RestrictionValues) => open({ kind: 'restrictions', values }),
        close, confirm, restore, clearLastReview: () => setLastReview(null),
        isRestored: (message: ReviewMessage) => restoredUsers.includes(`${message.chatId}:${message.userId}`),
    }
}
