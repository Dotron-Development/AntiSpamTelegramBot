import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { ShieldCheck } from 'lucide-react'
import { accessMessages, checkReviewAccess } from '../api/client'
import type { AccessFailure, Connection } from '../api/client'
import { getTelegramWebApp } from '../lib/auth'
import { errorText } from '../lib/workspace'

type AccessState = { kind: 'starting' } | { kind: AccessFailure } |
    { kind: 'failed'; message: string } | { kind: 'ready'; connection: Connection }

export function AuthGate({ children }: { children: (connection: Connection) => ReactNode }) {
    const webApp = getTelegramWebApp()
    const initData = webApp?.initData
    const [state, setState] = useState<AccessState>({ kind: 'starting' })

    useEffect(() => {
        const controller = new AbortController()
        const { signal } = controller
        // Let StrictMode's setup/cleanup probe finish before making the access check.
        void Promise.resolve().then(async () => {
            if (signal.aborted || !webApp) return
            webApp.ready()
            if (!initData) return
            webApp.expand()
            let accessFailure: AccessFailure | undefined
            const connection: Connection = {
                endpoint: import.meta.env.API_URL,
                initData,
                onAccessFailure(failure) {
                    if (signal.aborted || accessFailure) return
                    accessFailure = failure
                    setState({ kind: failure })
                },
            }
            try {
                await checkReviewAccess(connection, signal)
                if (!signal.aborted) setState({ kind: 'ready', connection })
            } catch (error) {
                if (!signal.aborted && !accessFailure) setState({ kind: 'failed', message: errorText(error) })
            }
        })
        return () => controller.abort()
    }, [initData, webApp])

    if (initData && state.kind === 'ready') return children(state.connection)
    const title = !initData ? 'Open in Telegram' : state.kind === 'reopen' ? 'Reopen in Telegram'
        : state.kind === 'denied' ? 'Access denied' : state.kind === 'failed' ? 'Review unavailable' : 'Checking review access'
    return <div className="app">
        <header className="topbar"><span className="brand"><ShieldCheck size={23} />AntiSpam / Review</span></header>
        <main>
            <section className="page-heading"><h1>{title}</h1></section>
            {!initData ? <p>Open the private chat with the bot in Telegram and tap its menu button to launch the review panel.</p>
                : state.kind === 'reopen' ? <div className="notice error" role="alert">{accessMessages.reopen} Check the queue or history before repeating any action.</div>
                : state.kind === 'denied' ? <div className="notice error" role="alert">{accessMessages.denied}</div>
                : state.kind === 'failed' ? <div className="notice error" role="alert">{state.message} Close and reopen the Mini App from the bot menu.</div>
                : <p role="status">Connecting to the review API…</p>}
            {initData && state.kind !== 'starting' &&
                <button className="button subtle" onClick={() => webApp?.close()}>Close Mini App</button>}
        </main>
    </div>
}