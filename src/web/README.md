# AntiSpam Review

React + TypeScript Telegram Mini App for the Azure Functions review API. The interface is in English. Requests go directly from the browser to the Function; its CORS configuration must allow the frontend origin.

## Run

Use Node.js 24 LTS and npm. From this directory:

```powershell
npm ci
npm run dev
```

Vite serves `http://localhost:5173` and fails if the port is occupied. Opening the URL in a regular browser only shows launch instructions. Workspaces do not have a local auth bypass; use the [Playwright tests](../../tests/TelegramAntiSpamBot.WebTests/README.md) for mocked browser development or use a test bot and a reachable HTTPS frontend for an actual Telegram launch.

The only frontend build setting is the existing `API_URL`, configured in `.env` or the build environment:

```dotenv
API_URL=http://localhost:7071
```

The default is `http://localhost:7071`. An HTTPS URL such as `https://your-function.azurewebsites.net` is also accepted, with or without a trailing `/api`. Use the Function base URL, not a review route or a URL with query parameters. Restart Vite or rebuild the static bundle after changing it.

Configure the bot's [menu button](https://core.telegram.org/bots/webapps#launching-mini-apps-from-the-menu-button) to point to the frontend HTTPS URL. In the **private chat with the bot**, tap the menu button to open the panel. The app sends Telegram's raw signed `initData` unchanged in `Authorization: tma …` to `GET /api/review/access`. The API verifies the signature, the launch age and the moderator allowlist. The workspace opens only after HTTP 204. The layout follows Telegram's light/dark theme and respects the client and device safe areas.

Open **Menu** to switch sections or refresh the chat catalog. Select a chat and tap the upload icon to load or reload its records. There is no automatic loading or all-chat view. The same selection is shared across sections and remembered locally. Changing it clears previously loaded results. The review queue, history and command calls request 100 records per page. **Load more** follows the server’s continuation token, including after an empty page.

You need an already running Functions host with the review endpoints. Network restrictions and authentication still apply. Connecting to a deployed Function requires those endpoints to have been deployed separately.

The agreed chat and message response contracts use PascalCase JSON fields (`Chats`, `Messages`, `ContinuationToken`, `ChatId`, etc.). TypeScript contracts and dedicated parsers are defined in [contracts.ts](src/api/contracts.ts). Parsers trust the backend contract without per-field runtime validation and map responses to UI models, preserving 64-bit IDs and date ticks without rounding. Validation of user-entered IDs remains separate.

Review and history authors share the `MessageFrom` model: ID, name and username. Queue responses use `Id`, `Name`, `Username`; history responses use `id`, `name`, `username`. Deploy the updated backend and frontend together. Stored review authors must use the new format; records with `nick` are not migrated automatically.

## History and Command Calls

**Message history** and **Command calls** use the shared chat selector and show records as cards. History includes original UTC timestamps, author information, and expandable sections for user text/captions, image description and image text, alongside existing reply context. History `content.payload` contains optional, nullable `userMessage`, `imageDescription` and `imageText` fields; when the payload is absent, legacy `content.text` is shown as user text. Each history record's `updateType` is a Telegram enum name (`Message`, `EditedMessage`, `ChannelPost` or `EditedChannelPost`), displayed as Message, Edited message, Channel post or Edited channel post even when collapsed. Missing, null or unrecognized update types default to Message for legacy compatibility. Command calls show the latest admitted timestamp per chat and command, not every invocation. IDs and ticks remain lossless strings. These endpoints return camelCase JSON, unlike the existing queue/catalog contracts.

In **Message history**, select a specific chat to enable the red **DANGER: Clear chat history** action. It calls `DELETE /api/review/history/{chatId}` only after a confirmation dialog and typing `DANGER`. It deletes all stored history for that chat, not just the loaded page; other chats, the review queue and Telegram messages are untouched. The loaded history view is reset after success. This cannot be undone, is not atomic across batches, and is never retried automatically. If it fails or times out, deletion may be partial: close the dialog and reload history before deciding whether to try again. New messages can arrive during or after deletion. **Command calls** remains read-only.

The catalog is loaded automatically on startup, following all continuation pages. It starts empty and fills as the bot receives new or edited messages. The backend creates its `Chats` table on the first successful write; the Function identity must have permission to create it. The configured moderation archive chat is excluded. Existing review entries are not used to backfill chat names.

## Snapshots

In **Snapshots**, select a discussion group in the same picker and tap the upload icon. The bot resolves its linked channel and loads only that channel’s snapshot metadata. **Queue rebuild** works even if a snapshot has not yet been published; load the snapshot again after the worker finishes.

## Review

- Each card shows the message text or caption, available image transcription and reply context, sender, and original UTC date.
- **Spam** and **Not spam** ask for confirmation and remove only the reviewed message after the API succeeds.
- **Not spam** can return the saved text and image transcription to Telegram, not the original image, while the message is within the configured spam restriction timeout (three hours by default). Older messages are classified as safe without being republished. Neither decision changes restrictions.
- No mutation is retried automatically. After an error the outcome can be uncertain; reload before manually retrying. A repeated not-spam decision can republish text because the API is not transactional.

## CORS and Credentials

The shared restriction/republishing timeout is configured on Functions as
`TelegramBotConfiguration__SpamRestrictionTimeout` in .NET TimeSpan format,
for example `03:00:00`. Terraform supplies it from `spam_restriction_timeout`,
which can be overridden in each environment's `.tfvars`. It is not a SPA setting.

There is no proxy. Allow the exact frontend origin (normally `http://localhost:5173`) in the Function's CORS settings. `http://127.0.0.1:5173` is a different origin, as is another port. The Function host must handle preflight OPTIONS requests and allow GET, POST, and DELETE with `Authorization` and `Content-Type` headers. Allow the deployed frontend origin for use in Telegram.

For a local Functions host, you can start it with `func start --cors http://localhost:5173`. Remote Function URLs require HTTPS; HTTP is allowed only for loopback. Requests time out after 30 seconds (two minutes for history deletion), do not follow redirects, and do not send cookies. Browsers do not distinguish CORS failures from many network failures, so the error message mentions both.

Every review request sends `Authorization: tma …` with the same raw `initData`. Functions accept it for up to 30 minutes from Telegram's signed `auth_date` and check the moderator allowlist on each request. The app does not put the credential in URLs or browser storage; the official Telegram SDK manages its own launch parameters. Only the chat filter is remembered by the app in local storage. Review data is not persisted.

The next request after expiry returns HTTP 401, closes the workspace and asks you to reopen the Mini App from the private bot chat. An empty HTTP 403 from the access guard also closes the workspace and shows access denied. Operations are never retried automatically. Check the current queue or history before repeating a moderation action that may have been interrupted.

`npm run build` generates a static bundle; `npm run preview` serves it locally. For Telegram, host `dist` over HTTPS, allow its origin in the review API's CORS settings, and set the bot menu URL to that origin. Azure Static Web Apps can serve the public static assets; the review API runs separately and must enforce authorization. [staticwebapp.config.json](public/staticwebapp.config.json) provides SPA navigation fallback.

## Source structure

- [App.tsx](src/App.tsx) composes the workspace and connects its actions.
- [api/](src/api/) contains the HTTP client and response contracts/parsers.
- [hooks/](src/hooks/) owns chat loading, list pagination, and moderation state. Each loader cancels superseded requests and requests still pending on unmount.
- [components/](src/components/) contains the shared chat picker, record cards, and dialogs.
- [lib/](src/lib/) contains the Telegram Web App integration, workspace types, chat preferences, and display helpers.
- [styles/](src/styles/) contains application and global styles.

## Checks

```powershell
npm run build
npm run lint
```

Playwright lives in the independent [web tests project](../../tests/TelegramAntiSpamBot.WebTests/README.md), with its own dependencies, lock file, and test command. The web app does not depend on Playwright.