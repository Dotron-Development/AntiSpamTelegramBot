# AntiSpam Review

Local React + TypeScript workspace for the Azure Functions review API. The interface is in English. Requests go directly from the browser to the Function; its CORS configuration must allow the frontend origin.

## Run

Use Node.js 24 LTS and npm. From this directory:

```powershell
npm ci
Copy-Item .env.example .env
npm run dev
```

Open the URL printed by Vite, normally `http://127.0.0.1:5173`. If the port is occupied, Vite selects another one. Keep the server bound to loopback; do not expose it to your network or through a tunnel.

Set the initial Function URL in `.env`:

```dotenv
API_URL=http://localhost:7071
```

An Azure URL such as `https://your-function.azurewebsites.net` is also accepted, with or without trailing `/api`. Restart Vite after changing `.env` and reload the page. The URL is embedded into the client at dev-server startup/build time, not read at runtime by the static bundle. Only `API_URL` is exposed from these unprefixed variables; never put a Function key in frontend environment variables. `.env` is ignored by Git; `.env.example` contains the default.

1. Open **Connect Function**.
2. Confirm the Function URL from `.env`, or temporarily override it for this session. Do not paste a full review route or a URL containing `?code=...`.
3. Enter a Function key for Azure. A standard local Functions host normally does not require one.
4. Select a **Chat** from the backend catalog and load the queue. **Refresh chats** reloads the list and updated names.

You need an already running Functions host with the review endpoints. Network restrictions and authentication still apply. Connecting to a deployed Function requires those endpoints to have been deployed separately.

The catalog is loaded after connecting, following all continuation pages. It starts empty and fills as the bot receives new or edited messages. The backend creates its `Chats` table on the first successful write; the Function identity must have permission to create it. The configured moderation archive chat is excluded. Existing review entries are not used to backfill chat names.

## Review

- Select a message to see its original text/caption, image transcription, sender IDs, and original UTC date.
- **Spam** confirms spam and removes the queue entry after the API succeeds.
- **Not spam** returns the saved text and image transcription to Telegram, not the original image. It does not remove restrictions.
- **Remove restrictions** is independent of the review decision. It remains available in the last-decision row, or through the page-level action with editable chat and user IDs.
- **Load more** follows the server's continuation token, including after an empty page. The counter reflects loaded pending messages, not the total on the server. Entries follow Table Storage order, not date order.
- No mutation is retried automatically. After an error the outcome can be uncertain; refresh before manually retrying. A repeated not-spam decision can republish text because the API is not transactional.

## CORS and Credentials

There is no proxy. Allow the exact frontend origin (normally `http://127.0.0.1:5173`) in the Function's CORS settings. `http://localhost:5173` is a different origin, as is another port. The Function must handle preflight OPTIONS requests and allow GET/POST with `x-functions-key` and `Content-Type` headers. Configure CORS separately; this project does not change Azure infrastructure.

For a local Functions host, you can start it with `func start --cors http://127.0.0.1:5173`. Remote Function URLs require HTTPS; HTTP is allowed only for loopback. Requests time out after 30 seconds, do not follow redirects, and do not send cookies. Browsers do not distinguish CORS failures from many network failures, so the error message mentions both.

The Function key is sent in `x-functions-key`, held only in React memory, and cleared by disconnecting or reloading. It is never placed in a query string, local/session storage, or an environment file. Only the chat ID is remembered in local storage; the URL defaults to `.env` on each reload. Review messages are not persisted by the client. Do not use this tool in an untrusted browser profile or share a Function key.

`npm run build` generates a static bundle; `npm run preview` serves it locally. Its origin must also be allowed by Function CORS. Production hosting and authentication are deliberately out of scope.

## Checks

```powershell
npm run build
npm run lint
```

Playwright lives in the independent [web tests project](../../tests/TelegramAntiSpamBot.WebTests/README.md), with its own dependencies, lock file, and test command. The web app does not depend on Playwright.