# AntiSpam Review

Local React + TypeScript workspace for the Azure Functions review API. The interface is in English. Requests go directly from the browser to the Function; its CORS configuration must allow the frontend origin.

## Run

Use Node.js 24 LTS and npm. From this directory:

```powershell
npm ci
npm run dev
```

Open the URL printed by Vite, normally `http://localhost:5173`. If the port is occupied, Vite selects another one. Keep the server bound to loopback; do not expose it to your network or through a tunnel.

Create or edit `.env` with the Function URL and optional Function key:

```dotenv
API_URL=http://localhost:7071
FUNCTION_KEY=
```

An Azure URL such as `https://your-function.azurewebsites.net` is also accepted, with or without trailing `/api`. Restart Vite after changing `.env` and reload the page. `API_URL` and `FUNCTION_KEY` are embedded into the client at dev-server startup/build time, not read at runtime by the static bundle. **A configured key is visible to anyone who can access the frontend or its bundle.** Use this only for a trusted local workspace; do not publish or share a bundle containing a real key. `.env` is ignored by Git. Without environment settings, the defaults are `http://localhost:7071` and an empty key.

The workspace automatically uses `API_URL` and `FUNCTION_KEY` from `.env` and loads the chat catalog on opening/reloading. There are no Connect, Disconnect, or credential-editing controls. Use a Function base URL, not a full review route or a URL containing `?code=...`. A standard local Functions host normally does not require a key.

Leave **Chat** set to **All chats** and click **Load queue** to load a page across all chat partitions. Select a chat only to filter the queue. There is no automatic message loading on startup. The last successfully loaded filter is remembered; a fresh workspace defaults to **All chats**. **Refresh chats** reloads the list and updated names, and can retry a failed initial catalog request. An empty or failed catalog does not prevent loading the all-chat queue. To change connection settings, edit `.env`, restart Vite, and reload the page.

You need an already running Functions host with the review endpoints. Network restrictions and authentication still apply. Connecting to a deployed Function requires those endpoints to have been deployed separately.

The agreed chat and message response contracts use PascalCase JSON fields (`Chats`, `Messages`, `ContinuationToken`, `ChatId`, etc.). TypeScript contracts and dedicated parsers are defined in [contracts.ts](src/api/contracts.ts). Parsers trust the backend contract without per-field runtime validation and map responses to UI models, preserving 64-bit IDs and date ticks without rounding. Validation of user-entered IDs remains separate.

The catalog is loaded automatically on startup, following all continuation pages. It starts empty and fills as the bot receives new or edited messages. The backend creates its `Chats` table on the first successful write; the Function identity must have permission to create it. The configured moderation archive chat is excluded. Existing review entries are not used to backfill chat names.

## Review

- Select a message to see its original text/caption, image transcription, sender IDs, and original UTC date.
- **Spam** confirms spam and removes the queue entry after the API succeeds.
- **Not spam** returns the saved text and image transcription to Telegram, not the original image. It does not remove restrictions.
- **Remove restrictions** is independent of the review decision. It remains available in the last-decision row, or through the page-level action with editable chat and user IDs.
- **Load more** follows the server's continuation token, including after an empty page. It and **Refresh queue** use the submitted chat filter/page size, even if the form has since changed. The counter reflects loaded pending messages, not the total on the server. Entries follow Azure Table Storage partition/row-key order, not date order; **All chats** does not mean the chronologically latest messages. `Page size` requests up to N entries per Azure page. Messages are identified by both chat ID and message ID, since message IDs can repeat across chats.
- No mutation is retried automatically. After an error the outcome can be uncertain; refresh before manually retrying. A repeated not-spam decision can republish text because the API is not transactional.

## CORS and Credentials

There is no proxy. Allow the exact frontend origin (normally `http://127.0.0.1:5173`) in the Function's CORS settings. `http://localhost:5173` is a different origin, as is another port. The Function must handle preflight OPTIONS requests and allow GET/POST with `x-functions-key` and `Content-Type` headers. Configure CORS separately; this project does not change Azure infrastructure.

For a local Functions host, you can start it with `func start --cors http://127.0.0.1:5173`. Remote Function URLs require HTTPS; HTTP is allowed only for loopback. Requests time out after 30 seconds, do not follow redirects, and do not send cookies. Browsers do not distinguish CORS failures from many network failures, so the error message mentions both.

The Function key from `FUNCTION_KEY` is sent in `x-functions-key` automatically. The active connection is held in React memory and recreated from the environment settings on reload. It is never placed in a query string or local/session storage. When configured in `.env`, it is also embedded in the frontend bundle. Only the chat ID is remembered in local storage; the URL defaults to `.env` on each reload. Review messages are not persisted by the client. Do not use this tool in an untrusted browser profile or share a Function key.

`npm run build` generates a static bundle; `npm run preview` serves it locally. Its origin must also be allowed by Function CORS. Production hosting and authentication are deliberately out of scope.

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