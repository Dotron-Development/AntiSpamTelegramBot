import { InteractionRequiredAuthError, PublicClientApplication } from '@azure/msal-browser'

const tenantId = import.meta.env.TENANT_ID?.trim()
const clientId = import.meta.env.CLIENT_ID?.trim()
const scope = import.meta.env.API_SCOPE?.trim()
const redirectUri = import.meta.env.REDIRECT_URI?.trim()

export const configurationError = !tenantId || !clientId || !scope || !redirectUri
    ? 'Configure TENANT_ID, CLIENT_ID, API_SCOPE and REDIRECT_URI before signing in.'
    : !/^api:\/\/[0-9a-fA-F-]{36}\/Review\.Access$/.test(scope) ||
        scope.toLowerCase() !== `api://${clientId}/Review.Access`.toLowerCase()
        ? 'API_SCOPE must be api://<CLIENT_ID>/Review.Access, including the scope name. Restart Vite after changing .env.'
        : null

const client = configurationError ? null : new PublicClientApplication({
    auth: {
        clientId,
        authority: `https://login.microsoftonline.com/${tenantId}`,
        redirectUri,
        postLogoutRedirectUri: redirectUri,
    },
    cache: { cacheLocation: 'sessionStorage' },
})

let initialization: Promise<void> | undefined

export function initializeAuth(): Promise<void> {
    return initialization ??= (async () => {
        if (!client) throw new Error(configurationError!)
        await client.initialize()
        const response = await client.handleRedirectPromise()
        const accounts = client.getAllAccounts()
        client.setActiveAccount(response?.account ?? client.getActiveAccount() ?? (accounts.length === 1 ? accounts[0] : null))
    })()
}

export function currentAccount() {
    return client?.getActiveAccount() ?? null
}

export async function signIn() {
    await initializeAuth()
    await client!.loginRedirect({ scopes: [scope], prompt: 'select_account' })
}

export async function signOut() {
    await client?.logoutRedirect({ account: currentAccount() })
}

export async function getAccessToken(): Promise<string> {
    await initializeAuth()
    const account = currentAccount()
    if (!account) throw new Error('Sign in to continue.')
    try {
        const response = await client!.acquireTokenSilent({ account, scopes: [scope] })
        return response.accessToken
    } catch (error) {
        if (error instanceof InteractionRequiredAuthError) await signIn()
        throw error
    }
}