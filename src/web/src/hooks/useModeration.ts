import { useRef, useState } from 'react'
import { ApiError, reviewMessage } from '../api/client'
import type { Connection, ReviewMessage } from '../api/client'
import { errorText } from '../lib/workspace'
import type { Decision, Notify } from '../lib/workspace'

export function useModeration(connection: Connection, notify: Notify, remove: (message: ReviewMessage) => void) {
    const [decision, setDecision] = useState<Decision | null>(null)
    const [working, setWorking] = useState(false)
    const [error, setError] = useState('')
    const pending = useRef(false)

    function open(message: ReviewMessage, isSpam: boolean) {
        if (pending.current) return
        setError('')
        setDecision({ message, isSpam })
    }

    function close() {
        if (!pending.current) setDecision(null)
    }

    async function confirm() {
        if (!decision || error || pending.current) return
        pending.current = true
        setWorking(true)
        try {
            await reviewMessage(connection, decision.message, decision.isSpam)
            remove(decision.message)
            notify({ error: false, text: decision.isSpam ? 'Marked as spam.' : 'Marked as not spam.' })
            setDecision(null)
        } catch (error) {
            setError(`${errorText(error)} The action may already have completed. Reload before retrying; a repeated not-spam decision can republish text.`)
            if (error instanceof ApiError && error.status === 404)
                notify({ error: true, text: 'This message is no longer in the queue. Reload to update the list.' })
        } finally {
            pending.current = false
            setWorking(false)
        }
    }

    return { decision, working, error, open, close, confirm }
}
