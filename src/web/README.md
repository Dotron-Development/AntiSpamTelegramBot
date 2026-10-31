# AntiSpam Review

Local React + TypeScript workspace for the Azure Functions review API. The interface is in English. Requests go directly from the browser to the Function; its CORS configuration must allow the frontend origin.

## Run

Use Node.js 24 LTS and npm. From this directory:

```powershell
npm ci
npm run dev
```

Open `http://localhost:5173`. Vite fails if this port is occupied so the address stays consistent with the registered Entra redirect URI. Keep the server bound to loopback.

Create or edit `.env` with the Function URL and public Entra configuration:

```dotenv
API_URL=http://localhost:7071
TENANT_ID=<Directory tenant ID>
CLIENT_ID=<Application client ID>
API_SCOPE=api://<Application client ID>/Review.Access
REDIRECT_URI=http://localhost:5173
```

An Azure URL such as `https://your-function.azurewebsites.net` is also accepted, with or without trailing `/api`. Restart Vite after changing `.env`. These settings are embedded at dev-server startup/build time, not read at runtime by the static bundle. They are public identifiers, not secrets. `.env` is ignored by Git. Never configure a client secret in the frontend. `FUNCTION_KEY` is no longer read or embedded.

Opening the site automatically redirects to Microsoft sign-in when there is no account. There is no login button. The workspace mounts only after sign-in, a `Moderator` role check, and successful API access-token acquisition. Errors and missing roles keep the workspace closed without a redirect loop. Sign out is available in the header. MSAL keeps its session in session storage and silently renews tokens when possible; interaction-required renewal redirects to sign-in. Use a Function base URL, not a full review route or a URL containing `?code=...`.

Follow the [Entra registration guide](../../docs/entra-id-setup.md) and [Functions Easy Auth guide](../../docs/functions-easy-auth.md). Configure the tenant, client ID and explicit user allowlist in Easy Auth; Functions does not read the frontend `.env` or perform Entra JWT validation. The UI requires `Moderator`, but API access is enforced by the platform's user and client allowlists, not by the UI role check. Review triggers now use Anonymous host authorization and rely on Easy Auth; verify platform protection before deploying them to any environment. Older deployed packages may still require Function keys and return 401 until Functions is redeployed. Local hosts do not run Easy Auth, so bind them to loopback.

Leave **Chat** set to **All chats** and click **Load queue** to load a page across all chat partitions. Select a chat only to filter the queue. There is no automatic message loading on startup. The last successfully loaded filter is remembered; a fresh workspace defaults to **All chats**. **Refresh chats** reloads the list and updated names, and can retry a failed initial catalog request. An empty or failed catalog does not prevent loading the all-chat queue. To change connection settings, edit `.env`, restart Vite, and reload the page.

You need an already running Functions host with the review endpoints. Network restrictions and authentication still apply. Connecting to a deployed Function requires those endpoints to have been deployed separately.

The agreed chat and message response contracts use PascalCase JSON fields (`Chats`, `Messages`, `ContinuationToken`, `ChatId`, etc.). TypeScript contracts and dedicated parsers are defined in [contracts.ts](src/api/contracts.ts). Parsers trust the backend contract without per-field runtime validation and map responses to UI models, preserving 64-bit IDs and date ticks without rounding. Validation of user-entered IDs remains separate.

## History and Command Calls

**Message history** and **Command calls** are read-only table views with optional chat filtering, refresh and continuation-token pagination. History includes original UTC timestamps, author information, plain text/captions and expandable reply context. Command calls show the latest admitted timestamp per chat and command, not every invocation. IDs and ticks remain lossless strings. These endpoints return camelCase JSON, unlike the existing queue/catalog contracts.

Select a specific chat on **Message history** to enable **Test Chronicle**. It generates an inline, copyable recap using the same last-hour/latest-100 selection, embedded prompt and AI service as `/chronicle`. The selected chat is used even if the displayed history page was loaded with another filter. Changing chat clears the preview. Nothing is sent to Telegram.

Preview uses `POST /api/review/chronicle/{chatId}` and shares the per-chat `/chronicle` cooldown with Telegram. Admitted attempts update `CommandsCall`, including failures; cooldown skips show an informational notice. Configure Functions `TelegramBotConfiguration__CommandCooldown` (default `00:05:00`) or Terraform `command_cooldown`. Requests are not automatically retried. A timeout can leave generation running on the backend; wait for cooldown before explicitly trying again.

The catalog is loaded automatically on startup, following all continuation pages. It starts empty and fills as the bot receives new or edited messages. The backend creates its `Chats` table on the first successful write; the Function identity must have permission to create it. The configured moderation archive chat is excluded. Existing review entries are not used to backfill chat names.

## Review

- Select a message to see its original text/caption, image transcription, sender name and `@nick` when available, sender ID, and original UTC date.
- **Spam** confirms spam and removes the queue entry after the API succeeds.
- **Not spam** returns the saved text and image transcription to Telegram, not the original image, only while the original message is within the configured spam restriction timeout (three hours by default). Older messages are classified as safe and removed from the queue without being republished. It does not remove restrictions.
- **Remove restrictions** is independent of the review decision. It remains available in the last-decision row, or through the page-level action with editable chat and user IDs.
- **Load more** follows the server's continuation token, including after an empty page. It and **Refresh queue** use the submitted chat filter/page size, even if the form has since changed. The counter reflects loaded pending messages, not the total on the server. Entries follow Azure Table Storage partition/row-key order, not date order; **All chats** does not mean the chronologically latest messages. `Page size` requests up to N entries per Azure page. Messages are identified by both chat ID and message ID, since message IDs can repeat across chats.
- No mutation is retried automatically. After an error the outcome can be uncertain; refresh before manually retrying. A repeated not-spam decision can republish text because the API is not transactional.

## CORS and Credentials

The shared restriction/republishing timeout is configured on Functions as
`TelegramBotConfiguration__SpamRestrictionTimeout` in .NET TimeSpan format,
for example `03:00:00`. Terraform supplies it from `spam_restriction_timeout`,
which can be overridden in each environment's `.tfvars`. It is not a SPA setting.

There is no proxy. Allow the exact frontend origin (normally `http://localhost:5173`) in the Function's CORS settings. `http://127.0.0.1:5173` is a different origin, as is another port. The Function host must handle preflight OPTIONS requests and allow GET/POST with `Authorization` and `Content-Type` headers. Terraform manages deployed Function CORS, automatically including the deployed SPA origin and the configured additional origins.

For a local Functions host, you can start it with `func start --cors http://localhost:5173`. Remote Function URLs require HTTPS; HTTP is allowed only for loopback. Requests time out after 30 seconds (two minutes for Chronicle previews), do not follow redirects, and do not send cookies. Browsers do not distinguish CORS failures from many network failures, so the error message mentions both.

Each API request obtains an access token through MSAL and sends it in `Authorization: Bearer ...`. No Function key or ID token is sent to review endpoints. The application does not copy tokens into its own storage or query strings. Only the chat filter is remembered in local storage; review messages are not persisted. Use a trusted browser profile. Client-side role checks are only a UI gate: configure Easy Auth to enforce API access. Easy Auth does not run in the local Functions host. Static HTML/JS remains public; no sensitive data is included before login.

`npm run build` generates a static bundle; `npm run preview` serves it locally. Its origin must be registered as a SPA redirect URI, configured as `REDIRECT_URI`, and allowed by Function CORS.

## Azure Hosting

Terraform creates a Linux Web App with a separate paid App Service plan (default `B1`) and Node.js 24. The startup command serves the deployed static bundle with SPA fallback. Both release workflows invoke [webapp.yaml](../../.github/workflows/webapp.yaml) after deploying infrastructure and Functions.

All five public frontend settings (`API_URL`, `TENANT_ID`, `CLIENT_ID`, `API_SCOPE`, `REDIRECT_URI`) are configured on the Web App by Terraform. The build job reads and validates them before lint/build, then uploads and deploys `dist`. The tenant comes from the existing Azure provider, not an additional setting. Changing these settings requires rebuilding the SPA; static JavaScript does not read server environment variables at runtime.

Before the first sign-in, add the Terraform output `review_web_app_url` to the existing registration's SPA redirect URIs. See the [hosting and Easy Auth configuration](../../docs/functions-easy-auth.md) for environment variables and the required platform-protection checks before Functions deployment. No Function key or client secret belongs in the SPA.

## Source structure

- [App.tsx](src/App.tsx) composes the workspace and connects its actions.
- [api/](src/api/) contains the HTTP client and response contracts/parsers.
- [hooks/](src/hooks/) owns chat loading, queue pagination/selection, and moderation state. Each loader cancels superseded requests and requests still pending on unmount.
- [components/](src/components/) contains the query form, queue, message details, last decision, and dialogs.
- [lib/](src/lib/) contains shared workspace types, environment configuration, chat preferences, and display helpers.
- [styles/](src/styles/) contains application and global styles.

The restrictions dialog uses React Hook Form for field validation and submission state. IDs stay strings and are validated with the existing 64-bit ID validator. Each opening mounts a fresh form with the current chat/user defaults, so cancelled input and errors are not carried into the next opening. API errors remain separate from field errors. The query form uses ordinary React state for its two selects. Moderation operations share a synchronous submission guard and are never retried automatically.

## Checks

```powershell
npm run build
npm run lint
```

Playwright lives in the independent [web tests project](../../tests/TelegramAntiSpamBot.WebTests/README.md), with its own dependencies, lock file, and test command. The web app does not depend on Playwright.