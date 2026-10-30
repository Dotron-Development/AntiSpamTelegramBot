# Manual Entra ID Setup: One Registration

Use one single-tenant App registration for the React SPA and the review API.
One client ID identifies both. No client secret or Microsoft Graph application
permissions are needed.

The website uses MSAL with Authorization Code + PKCE. Azure Functions Easy Auth
validates incoming tokens and restricts API access to an explicit user allowlist.
The frontend checks `Moderator` for its UI; this role is not an Easy Auth policy.
Complete the [Functions Easy Auth setup](functions-easy-auth.md) before using
the frontend with the deployed API.

## Step 0. Select the Tenant

Select the intended tenant at https://entra.microsoft.com.

You need permission to register an application and assign users to its Enterprise
application. If either operation is unavailable, ask a tenant administrator to
perform that step. Owning an App registration does not automatically grant
assignment or consent permissions.

## Step 1. Create One Registration

App registrations > New registration:

- Name: `AntiSpam Review - dev`.
- Supported account types: Accounts in this organizational directory only.
- Redirect URI platform: Single-page application (SPA).
- Redirect URI: `http://localhost:5173`.

Click Register. The redirect is where the browser returns after sign-in, not the
API address. Later add the exact production HTTPS redirect URI. Keep the local
site on the registered address. Leave implicit grant checkboxes disabled.
Do not create a client secret or enable public client fallback flows.

Record Directory (tenant) ID as `TENANT_ID` and Application (client) ID as
`CLIENT_ID`. Do not use the object ID in their place.

## Step 2. Create the Role

In this registration > App roles > Create app role:

- Display name: `Moderator`.
- Allowed member types: Users/Groups.
- Value: `Moderator` (case-sensitive).
- Description: `Review messages and perform moderation actions`.
- Enable the role, then Apply.

## Step 3. Assign Yourself

Enterprise applications > All applications > `AntiSpam Review - dev`:

1. Verify Application ID equals `CLIENT_ID`.
2. Users and groups > Add user/group: select yourself and role `Moderator`.
3. Confirm the assignment is saved.
4. Properties > Assignment required? > Yes > Save.

If the enterprise application is missing, the assignment fails, or Properties
cannot be changed, ask an administrator to provision/configure its service
principal and assign your user. Do not continue assuming role creation alone
grants access. Direct user assignment is enough; no group license is needed for
this direct assignment. Record each moderator's user Object ID from Entra ID >
Users > the user > Overview. These IDs are also needed for the separate Easy Auth
allowlist. Moderators have access to all chats, not a per-chat permission model.

## Step 4. Select Access Token Version 2

App registrations > `AntiSpam Review - dev` > Manifest:

Find `requestedAccessTokenVersion` inside the existing `api` object. Set its
value to `2` and Save. Preserve all other fields. If already 2, leave it alone.

This selects the access token format; it does not grant access or create a secret.
Easy Auth will use the corresponding v2 issuer and client-ID audience.

## Step 5. Name the API and Create Its Scope

In the SAME registration > Expose an API:

1. Application ID URI > Add: keep `api://<CLIENT_ID>` with the actual GUID.
2. Save, then Add a scope.
3. Scope name: `Review.Access`.
4. Who can consent: Admins only.
5. Admin consent display name: `Access AntiSpam review API`.
6. Admin consent description: `Read review queues and perform moderation actions`.
7. State: Enabled > Add scope.

The scope is `api://<CLIENT_ID>/Review.Access`. This URI is an Entra resource
identifier, not a URL to open or a DNS name to configure.

There is no second SPA registration to link. The frontend will explicitly request
this scope for the same registration. Merely defining a scope does not grant
consent: tenant policy may require administrator approval even for this single
registration. If sign-in/token acquisition reports approval required, ask the
administrator to authorize this client for this custom delegated scope. Do not
switch to Graph Application permissions or weaken policy to work around it.

Default Graph `User.Read` is not needed unless the site calls Graph. Do not add
`Application.ReadWrite.OwnedBy` or `Application.ReadWrite.All` to the review app.

## Step 6. Record the Configuration

```text
TENANT_ID=<Directory (tenant) ID>
CLIENT_ID=<the single Application (client) ID>
API_SCOPE=api://<CLIENT_ID>/Review.Access
REDIRECT_URI=http://localhost:5173
API_URL=<Function App HTTPS URL>
```

Put these values in `src/web/.env` before starting or building the site. They are
public configuration, not secrets. Restart Vite after changes.

Configure the tenant and client ID in Easy Auth, not in Functions application
code. Functions does not read `src/web/.env`. The reviewed setup does not need
`TENANT_ID` or `CLIENT_ID` application settings on the Function App.

## Step 7. Verify Login and API Protection

Configure and verify [Easy Auth](functions-easy-auth.md), then run the website
at its registered redirect URI.
Opening the site redirects automatically to Microsoft when there is no session;
there is no login button. The workspace stays closed until the user has the
Moderator role and MSAL obtains an API access token.

The API receives the access token as `Authorization: Bearer ...`. Easy Auth
validates it before invoking Functions and checks the configured client and user
allowlists. It does not enforce the `Review.Access` scope name or `Moderator` role.
Do not treat the frontend role check as API authorization. Maintain the user
allowlist whenever API access changes.

Easy Auth is not available in the local Functions host. Test authentication
against the Azure endpoint. Keep Telegram webhook secret-header authorization
independent, and exclude only its exact route from Easy Auth.

The Functions source currently requires Function keys on review routes. The
Easy Auth guide explains the safe switch to Anonymous triggers after platform
protection is active. Without this switch, Bearer-only calls still return 401.
