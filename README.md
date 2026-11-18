# Setup Guide

## 1. Prerequisites

- An Azure subscription and permission to manage resources and role assignments.
- A GitHub account with administration access to this repository.
- .NET 10 SDK, Node.js 24 LTS, Azure CLI and Azure Functions Core Tools v4 for local development.
- Terraform 1.17.x for local infrastructure operations.
- A separate Telegram bot for each environment, created through BotFather.

Use `dev` for testing and `prod` for production. Keep their Telegram bots, data and configuration separate.

## 2. Configure Telegram

1. Create the bots through BotFather and record their tokens and usernames.
2. Add each bot to its target groups and grant permission to receive messages, delete messages and restrict members.
3. Configure Telegram privacy mode as needed for the bot to receive group messages.
4. If using a moderation archive chat, add the bot there and record its chat ID.
5. Generate a separate webhook header value for each environment and store it as `TELEGRAM_BOT_SECRET_HEADER` in GitHub.

## 3. Prepare Azure and the Runner

Create these resources before running the deployment workflows:

- A storage account for the Terraform backend, with `dev` and `prod` blob containers. Each environment uses `terraform.tfstate` in its own container.
- The GitHub runner VNet and subnet referenced by the environment configuration.
- A self-hosted Linux GitHub Actions runner with Bash, Azure CLI and access to the Terraform backend. Use runner version 2.327.1 or later.
- A deployment App registration and service principal for each environment, or one shared deployment application with access to both.

Grant the deployment identities permission to manage the configured Azure resources, create role assignments, access Terraform state, manage Key Vault data and provision storage tables. Preserve the runner's network access to the backend and private resources. Restrict access to Terraform state and saved plans.

Check Azure AI model availability and quota in the configured region before deployment.

## 4. Configure GitHub Environments

In repository **Settings > Environments**, create `dev` and `prod`. For each environment, configure:

| Secret | Value |
| --- | --- |
| `ARM_TENANT_ID` | Deployment application's Directory (tenant) ID. |
| `ARM_SUBSCRIPTION_ID` | Target Azure subscription ID. |
| `ARM_CLIENT_ID` | Deployment application's Application (client) ID. |
| `AZ_OWNER_OBJECT_ID` | Object ID to grant Key Vault administrator access. |
| `TELEGRAM_BOT_TOKEN` | Environment's Telegram bot token. |
| `TELEGRAM_BOT_SECRET_HEADER` | Environment's webhook header value. |

Configure these environment variables:

| Variable | Value |
| --- | --- |
| `AZ_TERRAFORM_BACKEND_STORAGEACCOUNT` | Existing Terraform backend storage account name. |
| `AZ_TERRAFORM_BACKEND_RESOURCEGROUP` | Backend storage account's resource group. |
| `REVIEW_USER_OBJECT_IDS` | Non-empty JSON array of permitted moderators' Entra user Object IDs, for example `["<USER_OBJECT_ID>"]`. |
| `REVIEW_FRONTEND_ORIGINS` | Optional JSON array of additional exact frontend origins. Defaults to `["http://localhost:5173"]`; use `[]` for only the deployed website. |

For each of `ppr` and `prod`, create two approval-only GitHub Environments: `<env>-connectivity` and `<env>-apply`. On each, restrict deployment to `main`, add **Required reviewers**, disable administrator bypass, and set the **environment** variable `TERRAFORM_APPROVAL_CONFIGURED=true` only after those rules are in place. Do not set this variable at repository or organization scope. These four environments do not need Azure credentials. Keep branch restrictions on the existing `ppr` and `prod` credential environments. Existing reviewer rules on those environments may cause additional prompts. Do not remove those rules without checking the other workflows that rely on them, including infrastructure destroy. `dev` has no approval gates.

Add a distinct high-entropy secret `TERRAFORM_PLAN_ENCRYPTION_KEY` to each existing `dev`, `ppr`, and `prod` environment (for example, 32 random bytes encoded as hex). The self-hosted Linux runner needs GnuPG and Python 3.9+. The `ppr` and `prod` release waits for approval before temporary runner connectivity is changed, then prints the Terraform plan in the Plan job log and waits again before applying the saved plan. `dev` plans and applies automatically. Plan and Apply use separate runner temp directories named by workflow run ID and attempt. Apply downloads the encrypted plan by artifact ID, not by runner path. The artifact expires after two days, so restart the workflow if it expires. If a release stops after establishing temporary runner connectivity, verify and clean up the temporary endpoints before leaving the environment idle.

## 5. Configure OIDC Deployment Access

Keep deployment trust outside the bot's Terraform state so that destroying `dev` does not remove its deployment access.

1. In Entra ID **App registrations**, open the deployment application matching the environment's `ARM_CLIENT_ID`. This is not the review website's registration.
2. In **Certificates & secrets > Federated credentials**, add a **GitHub Actions** credential with:

   | Field | Value |
   | --- | --- |
   | Organization | `Dotron-Development` |
   | Organization ID | `215062107` |
   | Repository | `AntiSpamTelegramBot` |
   | Repository ID | `903421504` |
   | Entity type | **Environment** |
   | Environment | `dev` or `prod`, matching the GitHub environment exactly. |
   | Issuer | `https://token.actions.githubusercontent.com` |
   | Audience | `api://AzureADTokenExchange` |

3. Configure these exact Subject identifiers:

   ```text
   repo:Dotron-Development@215062107/AntiSpamTelegramBot@903421504:environment:dev
   repo:Dotron-Development@215062107/AntiSpamTelegramBot@903421504:environment:prod
   ```

   Add each credential to its environment's deployment application. If both environments use the same application, add both credentials there.

4. Confirm that immutable OIDC subjects are enabled for the repository:

   ```powershell
   gh api repos/Dotron-Development/AntiSpamTelegramBot/actions/oidc/customization/sub
   ```

   `use_immutable_subject` must be `true`. If enabling it for a new setup, prepare the matching Azure credentials first, then run:

   ```powershell
   gh api --method PUT repos/Dotron-Development/AntiSpamTelegramBot/actions/oidc/customization/sub -F use_default=true -F use_immutable_subject=true
   ```

For a fork, replace the organization, repository and their numeric IDs. Retrieve the IDs with `gh api orgs/<organization> --jq .id` and `gh api repos/<owner>/<repository> --jq .id`.

## 6. Configure Website Sign-In

Use a separate single-tenant Entra App registration for the review website and API. Its tenant must match the deployment tenant.

1. Add the **Single-page application** redirect URI `http://localhost:5173` for local development.
2. Create the `Moderator` app role for Users/Groups and assign the permitted moderators in the Enterprise application.
3. Set the API's `requestedAccessTokenVersion` to `2`.
4. Under **Expose an API**, set `api://<CLIENT_ID>` and create the delegated `Review.Access` scope. Arrange the required administrator consent.
5. Record each moderator's user Object ID in `REVIEW_USER_OBJECT_IDS`.

Follow the detailed [Entra registration guide](docs/entra-id-setup.md) and [Functions Easy Auth guide](docs/functions-easy-auth.md). The frontend role check does not replace the API's user allowlist. Keep the Telegram webhook excluded from Easy Auth; its own header validation remains enabled.

## 7. Configure Environment Parameters

Edit [dev.tfvars](src/infrastructure/environments/dev.tfvars) and [prod.tfvars](src/infrastructure/environments/prod.tfvars):

- `location`, environment names and prefixes.
- Non-overlapping `vnet_address_prefix` values.
- Existing GitHub runner VNet, resource group and subnet names.
- `bot_name`, including `@`, and `forwardSpamToChatId`.
- `review_client_id`, using the review website/API registration from step 6, not the deployment application.
- AI model names, versions and capacities for `spam_recognition`, `image_text_extractor` and `mention`.
- `disable_public_access`, according to the environment's network requirements.
- Optional `function_alert_emails`, `command_cooldown` and `spam_restriction_timeout`.

Function failure alerts evaluate failed Application Insights requests every five
minutes over a five-minute window. This query does not support Azure Monitor's
one-minute evaluation frequency.

## Search storage and embeddings

The `Vectors` repository in Persistence stores post titles and binary vector chunks in the bot's data storage account. The Search project reuses `OpenAiServicesConfiguration` for the endpoint and credentials; `OpenAiServicesConfiguration__SearchEmbeddingDeployment` selects the deployment. The index uses `text-embedding-3-large` version 1 (3,072 dimensions).

The Telegram webhook queues only automatic channel forwards (and their edits) in discussion groups with text or a caption. It uses the original channel ID and post ID from `forward_origin`, not the discussion copy's IDs, so channel archive imports can use the same index keys. Forwards without a channel origin are skipped. `IndexChannelPost` reads `search-index`, batches paragraph-based fragments for embeddings, and atomically replaces the post vectors. Duplicate or stale jobs do not rewrite vectors. Failed deliveries are retried by the Functions host and move to `search-index-poison` after the retry limit; jobs, including poison messages, carry temporary source text. After indexing, the worker schedules a delayed `reset-snapshot` message. `RebuildSearchSnapshot` assembles the channel's `Vectors` partition into a checksummed binary snapshot and publishes it as `<channel-id>` (no extension) in the private `search-snapshots` container. Pending requests covered by an already-built snapshot are skipped; exhausted retries go to `reset-snapshot-poison`. The review UI's **Search snapshots** tab lists blobs, sizes and last-modified timestamps and can queue a rebuild by channel ID, including for a channel with an existing index but no snapshot yet. Refresh the list after the worker finishes. A channel with no indexed posts does not produce a blob.

The indexing queues use the data storage account; the Functions host and deployment container keep their separate Function App storage account. If the queues were previously deployed in Function App storage, pause producers and drain or back up both queues before applying the change. Terraform replaces the queues and does not migrate messages.

### One-off Telegram archive import

[ChannelArchiveImport](tools/ChannelArchiveImport/ChannelArchiveImport.csproj) reads `messages.html`, `messages2.html`, etc. from a Telegram Desktop **channel HTML export**. Pass the channel's numeric Bot API ID (not the discussion group ID). Dry-run is the default and does not contact Azure:

```powershell
$archive = 'C:\path\to\ChatExport'
$channelId = '-1001234567890' # Replace with the export's channel ID.
dotnet run --project .\tools\ChannelArchiveImport -- $archive $channelId
```

Once the indexing worker is deployed, sign in with `az login` using an identity with **Storage Queue Data Message Sender** on the destination queue. Run the importer from a network that can reach the data storage queue endpoint; production requires access to its private endpoint and DNS. Use the data storage account for your environment (`satgbotdataprod` in production) and send a small test batch:

```powershell
$queue = 'https://<data-storage-account>.queue.core.windows.net/search-index'
dotnet run --project .\tools\ChannelArchiveImport -- $archive $channelId --send --queue-url $queue --limit 5
```

Remove `--limit` for the full archive. The worker skips repeated archive jobs and does not overwrite newer webhook revisions. Posts without text or captions and oversized queue payloads are skipped and counted; oversize skips produce a nonzero exit code. For a local send, use `--send --azurite` with an existing `search-index` queue.

## 8. Deploy and Verify

1. Run [release dev](.github/workflows/release-dev.yaml) manually from GitHub Actions, or push to `main` to deploy `dev` automatically.
2. Verify OIDC login, infrastructure deployment, Functions deployment, website deployment and runner-endpoint cleanup.
3. Obtain the `review_static_web_app_url` Terraform output and add it as a **Single-page application** redirect URI in the review registration.
4. Open that URL, sign in as an assigned moderator and verify API access. Check that unauthenticated review API requests are rejected.
5. Send a message to the development Telegram group and verify webhook delivery.
6. Run [release prod](.github/workflows/release-prod.yaml) manually, explicitly selecting `prod`. Repeat the sign-in and webhook checks for production.

The website deploys to Azure Static Web Apps Free. Its build settings are supplied by Terraform and the build job; changing them requires another deployment. Function CORS includes the deployed website origin automatically.

Entra redirect URIs are configured manually; the deployment workflows do not register them. The generated `azurestaticapps.net` address stays unchanged during normal deployments, but deleting and recreating the Static Web App can change it. After recreation, register the new `review_static_web_app_url` in the matching environment's review application before signing in.

If sign-in fails with **AADSTS50011**, open **Microsoft Entra ID > App registrations**, find the application by the client ID shown in the error (matching `review_client_id`), then open **Authentication**. Add the exact redirect URI from the error under **Single-page application**, not **Web**, and save. Preserve other required redirect URIs, including `http://localhost:5173` for local development. Allow a few minutes for the change to propagate, then retry sign-in. A custom domain is not required.

After development tests, run [destroy-ci](.github/workflows/destroy-ci.yaml) with environment `dev` to remove its infrastructure and data. Do not select `prod` for routine cleanup.

## 9. Run Locally

1. Sign in with `az login`. Ensure your account can access the AI deployments and application Table Storage. For private resources, configure working network access and DNS.
2. Configure the ignored [local.settings.json](src/bot/TelegramAntiSpamBot.Functions/local.settings.json) with the Telegram bot token, webhook header, bot username, Azure AI endpoint and deployment names, and Table Storage URL. Use Azurite if `AzureWebJobsStorage` is `UseDevelopmentStorage=true`. For local ingestion with Azurite, also set `SearchQueueStorage` and `AzureTablesConfiguration__StorageAccountUrl` to `UseDevelopmentStorage=true`. Create the `search-index`, `search-index-poison`, `reset-snapshot` and `reset-snapshot-poison` queues, the `Vectors` table, and the private `search-snapshots` container before testing ingestion (create `Chats` for webhook chat registration). Keep real credentials outside version control.

   ```json
   {
     "IsEncrypted": false,
     "Values": {
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "SearchQueueStorage": "UseDevelopmentStorage=true",
       "SnapshotStorageConfiguration__ServiceUrl": "UseDevelopmentStorage=true",
       "SnapshotStorageConfiguration__ContainerName": "search-snapshots",
       "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
       "TelegramBotConfiguration__Token": "<dev-bot-token>",
       "TelegramBotConfiguration__SecretHeader": "<webhook-header>",
       "TelegramBotConfiguration__BotName": "@YourDevBotName",
       "OpenAiServicesConfiguration__ServiceUrl": "https://<ai-service-endpoint>/",
       "OpenAiServicesConfiguration__SpamRecognitionDeployment": "antispam",
       "OpenAiServicesConfiguration__ImageRecognitionDeployment": "images",
       "OpenAiServicesConfiguration__MentionDeployment": "mention",
       "OpenAiServicesConfiguration__SearchEmbeddingDeployment": "text-embedding-3-large",
       "AzureTablesConfiguration__StorageAccountUrl": "https://<storage-account>.table.core.windows.net/"
     }
   }
   ```

3. Build and start Functions:

   ```powershell
   dotnet build AntiSpamTelegramBot.sln --configuration Release
   Set-Location src\bot\TelegramAntiSpamBot.Functions
   func start --cors http://localhost:5173
   ```

   Keep the local Functions host bound to loopback: Easy Auth runs only in Azure.

4. Create `src/web/.env` with the public configuration:

   ```dotenv
   API_URL=http://localhost:7071
   TENANT_ID=<Directory tenant ID>
   CLIENT_ID=<Review registration Application client ID>
   API_SCOPE=api://<Review registration Application client ID>/Review.Access
   REDIRECT_URI=http://localhost:5173
   ```

5. In another terminal, from the repository root:

   ```powershell
   Set-Location src\web
   npm ci
   npm run dev
   ```

   Open `http://localhost:5173`. Restart Vite after changing `.env`. To verify platform authentication, set `API_URL` to the deployed Function App's HTTPS origin.

6. For local Telegram delivery, expose the development webhook through a public HTTPS tunnel and register it from the repository root:

   ```powershell
   dotnet run --project src\bot\TelegramAntiSpamBot.Initializer -- "<dev-bot-token>" "https://<public-host>/api/AntiSpamBotWebHook" "<webhook-header>"
   ```

   Registration replaces the bot's current webhook; use only the development bot. Deployment workflows register the Azure webhook automatically.

See [the web setup guide](src/web/README.md) for additional frontend configuration.

## 10. Mention Agent and Web Search

Messages starting with `@BotName` invoke a Microsoft Agent Framework agent backed by Azure OpenAI Responses. Existing slash commands, spam detection, reply context, Telegram Markdown formatting and the chat-level command cooldown are unchanged. A new agent run is created for each mention; conversations are not shared between chats or requests, and Responses storage is disabled (`store: false`).

The agent has hosted web search backed by Bing and, for Telegram mentions only, a local `get_message_history` tool. Ordinary questions can be answered without either tool. Initial mention payloads contain `Question`, `From`, explicit `ReplyTo` and `Conversation` metadata (chat/message IDs, UTC mention date and optional reply date), not surrounding messages. Web previews keep their existing request/response shape and have no history tool.

`get_message_history(from, to, continuationToken = null)` accepts ISO 8601 timestamps with a UTC offset, with inclusive `from` and exclusive `to`. It is bound to the current chat and rejects intervals ending after the mention's timestamp. Each call returns at most 100 messages, oldest first, with ISO 8601 `Date`, author/reply/payload/update metadata and an opaque `ContinuationToken`. Reuse the same interval and token for the next page; stop only when the token is null, not merely when a page is empty. The prompt requests narrow relevant intervals and no history retrieval when the question and explicit reply suffice. Errors are logged and reported as tool errors rather than empty history.

Azure Tables retrieval uses equality on `PartitionKey` (chat ID) plus a range on the existing time-prefixed `RowKey`, not an unindexed timestamp filter or a full-chat scan. Both query page size and SDK page size are 100, only required properties are selected, and each call consumes one server page. No schema migration or secondary index is needed. The 90-second mention deadline also covers history retrieval. Retention remains the latest 1000 entries per chat after cleanup; historical coverage is not guaranteed.

Search results and retrieved history are untrusted evidence, and source URLs from response annotations are included in Telegram answers. The instructions prohibit sending private chat history, identities or secrets in search queries; this is a model instruction, not a deterministic data-loss-prevention filter.

Use the existing `OpenAiServicesConfiguration__ServiceUrl` as the Azure account's base URL (without `/openai/v1`), and `OpenAiServicesConfiguration__MentionDeployment` for a deployment supporting Responses and hosted web search (required; typically `mention`). Mention requests append `/openai/v1/responses` to that base URL. Authentication uses the existing optional API key or Azure credential/managed identity; no separate Bing API key is required. Entra authentication requests the `https://ai.azure.com/.default` scope and requires inference access to the resource.

Each mention has a 90-second deadline, a hard 2000-output-token generation limit and a provider-side limit of three hosted tool calls. Output tokens include reasoning tokens for reasoning models; this limit is separate from Telegram's character limit. The tool-call limit is not a strict cap on billable Bing requests; Azure reports those separately in `tool_usage.web_search.num_requests`. Existing retry behavior is retained (up to five retries for transient failures). Incomplete and empty responses are rejected and errors are logged by the mention command.

The prompt asks the agent to synthesize concise prose under 2000 characters using at most three sources, without inventing reference numbers or appending a bibliography. This source limit is a model instruction, not a guaranteed restriction on provider annotations or URL length. The application renders citation annotations inline as `Fact. [Source title](https://example.com/source)`, preserving the provider's title and original URL. If no title is provided, the URL is used as the link label. Provider citation markers are replaced; annotated factual prose is preserved. Telegram MarkdownV2 renders the title as a clickable link and escapes reserved characters in labels and link targets without changing the destination.

Telegram sends exactly one successful answer message, with inline references, replying to the mention. There is no separate sources message. If necessary, the formatted answer is truncated to 4096 characters after MarkdownV2 escaping, keeping formatted spans closed and Unicode pairs intact. Inline references that fit in a single message are kept whole rather than cut at the truncation boundary. Unexpectedly long responses can therefore lose trailing content; reducing the generation-token budget alone cannot guarantee Telegram's character limit. The web preview preserves its existing `{answer}` JSON shape and displays the full answer with inline references, without Telegram-specific truncation or a duplicate source list.

Web search is billed separately from model tokens. Review [Bing Grounding pricing](https://www.microsoft.com/en-us/bing/apis) and [Azure OpenAI pricing](https://azure.microsoft.com/en-us/pricing/details/azure-openai/). Bing grounding data flows outside Azure's compliance and geographic boundary; the Microsoft Data Protection Addendum does not apply to that data. See [Azure web search documentation](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/web-search) for terms and availability. The .NET Responses API currently carries the `OPENAI001` experimental diagnostic, suppressed only in the files that use it.

Review the [Bing use and display requirements](https://www.microsoft.com/en-us/bing/apis/grounding-legal-enterprise), which require retaining references and attribution. Clickable titles preserve the supplied title and destination, but Markdown formatting alone does not certify full compliance; truncation can still omit trailing references.

Focused offline validation (mocked Azure and Telegram HTTP responses; no paid requests):

```powershell
dotnet test tests\TelegramAntiSpamBot.IntegrationTests\TelegramAntiSpamBot.IntegrationTests.csproj --filter FullyQualifiedName~MentionCommandTests
```
