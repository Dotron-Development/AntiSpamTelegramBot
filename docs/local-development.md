# Run the Telegram Review Mini App locally

This guide uses PowerShell on Windows. Running locally does not run Terraform or deploy anything to Azure. Use a dedicated test bot and local storage so moderation actions cannot affect production.

| What you want to test | What to run |
| --- | --- |
| UI, launch-data expiry, and error handling without credentials | Playwright; it starts Vite and mocks Telegram and the API |
| The frontend and Functions on your machine | Vite, Azure Functions Core Tools, and Azurite |
| Real Telegram sign-in against your local code | The same local stack, plus public HTTPS URLs and a dedicated test bot |

## 1. Install the prerequisites

- .NET 10 SDK and Azure Functions Core Tools v4 with .NET 10 isolated-worker support.
- Node.js 24 LTS and npm.
- Azurite (the VS Code extension or CLI) and Azure Storage Explorer to create local storage resources.
- For real Telegram sign-in: a dedicated test bot token and your numeric Telegram user ID. For AI features, use a development Azure AI endpoint and deployment names, and sign in with `az login` using an account with inference access, or use a development API key.

## 2. Start and prepare Azurite

In VS Code, run **Azurite: Start** from the Command Palette. With the CLI installed, run `azurite` in its own terminal. Start all three services: Blob, Queue, and Table. In Storage Explorer, connect to the local emulator and create:

- **Tables:** `Chats`, `ProbablySpam`, `MessageCount`, `SpamHash`, `SpamStats`, `MessageHistory`, `CommandsCall`, `Vectors`.
- **Queues:** `search-index`, `search-index-poison`, `reset-snapshot`, `reset-snapshot-poison`.
- **Blob container:** `search-snapshots`, with private access.

An empty chat list is normal on a fresh emulator. The panel opens with empty local storage; it will not show the records from the Azure `dev` environment. To populate data, you can send test updates through the development webhook as described below.

## 3. Configure and start Azure Functions

Create or update `src\bot\TelegramAntiSpamBot.Functions\local.settings.json`. Replace placeholders with development values; do not commit this file or copy production credentials into it. Use your Telegram **user ID**, not your username or a chat ID. For multiple users, separate IDs with commas. An empty allowlist denies everyone.

```json
{
  "IsEncrypted": false,
  "Values": {
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "SearchQueueStorage": "UseDevelopmentStorage=true",
    "AzureTablesConfiguration__StorageAccountUrl": "UseDevelopmentStorage=true",
    "SnapshotStorageConfiguration__ServiceUrl": "UseDevelopmentStorage=true",
    "SnapshotStorageConfiguration__ContainerName": "search-snapshots",
    "TelegramBotConfiguration__Token": "<test-bot-token>",
    "TelegramBotConfiguration__SecretHeader": "<test-webhook-secret>",
    "TelegramBotConfiguration__BotName": "@YourTestBot",
    "ReviewTelegram__ModeratorIds": "123456789",
    "OpenAiServicesConfiguration__ServiceUrl": "https://<dev-ai-endpoint>/",
    "OpenAiServicesConfiguration__ImageRecognitionDeployment": "images",
    "OpenAiServicesConfiguration__SpamRecognitionDeployment": "antispam",
    "OpenAiServicesConfiguration__MentionDeployment": "mention",
    "OpenAiServicesConfiguration__SearchEmbeddingDeployment": "text-embedding-3-large"
  }
}
```

Use the deployment names from your AI resource; the names above are examples. You can set `OpenAiServicesConfiguration__ApiKey` in the ignored settings file if you use a key instead of Azure credentials. Telegram authorization does not call the AI service.

From the repository root:

```powershell
Set-Location .\src\bot\TelegramAntiSpamBot.Functions
func start --port 7071 --cors http://localhost:5173
```

Wait until Core Tools lists `CheckTelegramReviewAccess` and `GetReviewChats`. In another terminal, confirm both review routes reject unauthenticated requests:

```powershell
curl.exe -i http://localhost:7071/api/review/access
curl.exe -i http://localhost:7071/api/review/chats
```

Expect `401 Unauthorized` from both, not a redirect or a list of chats. Keep the host local unless you intentionally expose it for the Telegram test.

## 4. Start the frontend

Create `src\web\.env.local` with:

```dotenv
API_URL=http://localhost:7071
```

In another terminal, from the repository root:

```powershell
Set-Location .\src\web
npm ci
npm run dev
```

Visit `http://localhost:5173`. **Open in Telegram** is the expected screen in a regular browser: it does not have Telegram's signed launch data. This check confirms the site loads, but it does not sign you in. Restart Vite after changing `API_URL`.

## 5. Test real Telegram sign-in with the local stack

On Telegram Desktop running on the same computer as Functions, you can try using only a frontend tunnel to Vite on port `5173`. Keep `API_URL=http://localhost:7071`, allow the public frontend origin in Functions CORS, and set the test bot's menu URL to the public frontend URL. This only works if the desktop WebView permits access to the local HTTP API.

For a phone, or if the desktop WebView blocks localhost, use two public HTTPS URLs as described below: one forwarding to Vite on port `5173`, and one forwarding to Functions on port `7071`. A phone cannot reach your PC using its own `localhost`.

1. Set `API_URL` in `src\web\.env.local` to the public HTTPS **Functions** origin and restart Vite. Use only the origin, without an API path or credentials. If the proxy forwards the public hostname to Vite and Vite rejects it, add only that hostname to the per-process allowlist (no scheme or port): `$env:__VITE_ADDITIONAL_SERVER_ALLOWED_HOSTS = '<frontend-tunnel-hostname>'` before starting Vite. Do not allow all hosts.
2. Restart Functions with the exact public **frontend** origin in CORS, including `https://` and no trailing slash:

   ```powershell
   func start --port 7071 --cors 'http://localhost:5173,https://<frontend-tunnel-hostname>'
   ```

3. In `@BotFather`, run `/setmenubutton` for the dedicated test bot. Set the URL to the public HTTPS **frontend** URL. Keep the test bot token in the local Functions settings; it must match this bot.
4. In the bot's private Telegram chat, open the menu button. Verify that your allowlisted account can open the workspace and load the chat list. An unlisted Telegram account should see **Access denied**. A normal browser tab should still show **Open in Telegram**.
5. Every review API request sends `Authorization: tma <raw Telegram initData>`. Functions accepts signed launch data at most 30 minutes old with up to 30 seconds of future clock skew. There is no token exchange or refresh. After expiry, close and reopen the Mini App from the bot menu to get fresh launch data. To check expiry without waiting, use the Playwright expiry test.

**Webhook delivery is separate.** You do not need to change the webhook to test sign-in. If you also need to send test messages to local Functions, follow the [webhook setup in the root README](../README.md#9-run-locally), using the public **Functions** origin and the same local webhook secret. Registering a webhook replaces the bot's previous destination. Do this only with your dedicated test bot and restore its previous configuration if needed. Close the tunnels when finished; do not expose Azurite.

## 6. Run the browser tests without a bot

From the repository root:

```powershell
npm --prefix .\src\web ci
npm --prefix .\tests\TelegramAntiSpamBot.WebTests ci
npm --prefix .\tests\TelegramAntiSpamBot.WebTests exec -- playwright install chromium
npm --prefix .\tests\TelegramAntiSpamBot.WebTests test
```

The suite starts its own Vite server on port `5179`, intercepts Telegram and the API, and does not require Functions, Azurite, or Azure credentials. See the [Playwright README](../tests/TelegramAntiSpamBot.WebTests/README.md) for what it covers.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Functions cannot start the storage listeners | Check that Azurite is running and that `AzureWebJobsStorage` and `SearchQueueStorage` point to it. If startup instead reports a missing `Microsoft.Extensions.Options` assembly from the Storage Queues extension, that is a host/extension compatibility error, not Telegram auth. Check the Core Tools/runtime version before debugging the UI. |
| Telegram cannot load the panel or the UI reports a network/CORS error | Check that both HTTPS URLs are reachable from the device, that `API_URL` points to Functions, and that Functions allows the exact frontend origin. |
| Review API returns `401` | Check that the request includes the unmodified Telegram launch string in the `Authorization` header and that the bot in `@BotFather` matches the token in local settings. Reopen the Mini App to get fresh launch data; check the server clock if fresh data is rejected. |
| Review API returns `403` | Check that `ReviewTelegram__ModeratorIds` contains your numeric Telegram user ID and that Functions has been restarted after the change. |
| The panel opens but has no chats | Check that the local `Chats` table exists. A new emulator has no chat records until a test webhook update registers a chat. |

For deployed environments instead of a local stack, see the [Telegram Mini App deployment guide](telegram-mini-app.md).
