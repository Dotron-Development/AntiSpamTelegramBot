import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { LogOut, ShieldCheck } from 'lucide-react'
import { configurationError, currentAccount, getAccessToken, initializeAuth, signIn, signOut } from '../lib/auth'

export function AuthGate({ children }: { children: ReactNode }) {
    const [ready, setReady] = useState(false)
    const [error, setError] = useState<string | null>(configurationError)

    useEffect(() => {
        let active = true
        void initializeAuth().then(async () => {
            if (!active) return
            if (!currentAccount()) {
                await signIn()
                return
            }
            if (currentAccount()?.idTokenClaims?.roles?.includes('Moderator')) await getAccessToken()
            if (active) setReady(true)
        })
            .catch((failure: unknown) => { if (active) setError(failure instanceof Error ? failure.message : 'Sign-in failed.') })
        return () => { active = false }
    }, [])

    const account = ready ? currentAccount() : null
    const moderator = account?.idTokenClaims?.roles?.includes('Moderator') === true
    if (account && moderator) return children

    return <div className="app">
        <header className="topbar"><span className="brand"><ShieldCheck size={23} />AntiSpam / Review</span></header>
        <main>
            <section className="page-heading"><h1>{account ? 'Access denied' : error ? 'Sign-in unavailable' : 'Signing in'}</h1></section>
            {error && <div className="notice error" role="alert">{error}</div>}
            {account && <div className="notice error" role="alert">Your account does not have the Moderator role.</div>}
            {!account && !error && <p role="status">Redirecting to Microsoft sign-in...</p>}
            {account && <button className="button subtle" onClick={() => { void signOut() }}><LogOut size={16} />Sign out</button>}
        </main>
    </div>
}