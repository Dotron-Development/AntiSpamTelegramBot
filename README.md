# Setup Guide

## 1. Prerequisites

- An Azure subscription and permission to manage resources and role assignments.
- A GitHub account with administration access to this repository.
- .NET 11 SDK, Node.js 24 LTS, Azure CLI and Azure Functions Core Tools v4 with .NET 11 isolated-worker support for local development.
- Terraform 1.17.x for local infrastructure operations.
- A separate Telegram bot for each environment, created through BotFather.

Use `dev` for testing and `prod` for production. Keep their Telegram bots, data and configuration separate.

## 2. Configure Telegram

1. Create the bots through BotFather and record their tokens and usernames.
2. Add each bot to its target groups and grant permission to receive messages, delete messages and restrict members.
3. Configure Telegram privacy mode as needed for the bot to receive group messages.
4. If using a moderation archive chat, add the bot there and set `TelegramBotConfiguration__ForwardSpamToChatId` to its ID. The bot forwards spam there but ignores incoming updates from that chat, including commands, edits, and channel posts. They are not recorded in history or indexed.
5. Generate a separate webhook header value for each environment and store it as `TELEGRAM_BOT_SECRET_HEADER` in GitHub.

For spam detected by the model, the bot applies moderation before saving the review record. If review storage fails, the message can already be deleted and the sender restricted.

## 3. Prepare Azure and the Runner

Create these resources before running the deployment workflows:

- A storage account for the Terraform backend, with `dev` and `prod` blob containers. Each environment uses `terraform.tfstate` in its own container.
- The GitHub runner VNet and subnet referenced by the environment configuration.
- A self-hosted Linux x86-64 GitHub Actions runner with Bash, Azure CLI and access to the Terraform backend. Use runner version 2.327.1 or later.
- A deployment App registration and service principal for each environment, or one shared deployment application with access to both.

Grant the deployment identities permission to manage the configured Azure resources, create role assignments, access Terraform state, manage Key Vault data and provision storage tables. Preserve the runner's network access to the backend and private resources. Restrict access to Terraform state and saved plans.

Check Azure AI model availability and quota in the configured region before deployment.

## 4. Configure GitHub Environments

In repository **Settings > Environments**, create `dev`, `ppr`, and `prod`. For each environment, configure:

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
| `REVIEW_FRONTEND_ORIGINS` | Optional JSON array of additional exact frontend origins. Defaults to `["http://localhost:5173"]`; use `[]` for only the deployed website. |

On `ppr` and `prod`, restrict deployments to `main`, add **Required reviewers**, and disable administrator bypass before running release or destroy. Keep Azure credentials in these same environments. GitHub approval applies to jobs that reference the protected environment, including runner connectivity changes, Terraform plan and apply, destroy, Key Vault configuration, Functions, the review SPA, and runner endpoint cleanup. Expect multiple prompts as jobs become ready. Leave `dev` without required reviewers for automatic deployments.

Once the updated workflows are on `main`, start a new run rather than rerunning an older one, which uses the original workflow revision. The old `<env>-connectivity` and `<env>-apply` environments and their `TERRAFORM_APPROVAL_CONFIGURED` variables are no longer used by new runs.

Review the Terraform plan in the Plan job log before approving Apply. Plan and Apply use separate runner temp directories named by workflow run ID and attempt. Apply downloads the plain Terraform plan by artifact ID, not by runner path. The plan artifact is not encrypted and may contain sensitive values; restrict access to workflow runs and artifacts accordingly. The artifact expires after two days, so restart the workflow if it expires. If a release stops after establishing temporary runner connectivity, verify and clean up the temporary endpoints before leaving the environment idle.

When updating Terraform providers, run `terraform providers lock -platform=linux_amd64 -platform=windows_amd64` from `src/infrastructure` and commit the resulting lock file. CI initializes Terraform with a read-only lock file for Plan and Apply.

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

## 6. Configure the Review Mini App

The review UI is hosted on the existing Azure Static Web App. Set `review_telegram_moderator_ids` in the [environment Terraform file](src/infrastructure/environments/) using Telegram **user IDs**, not chat IDs or usernames. No new token, secret, or review-specific Entra registration is needed. Azure deployment access still uses the separate OIDC registration described above.

Every review API request sends `Authorization: tma <raw Telegram initData>`. Functions verifies the Telegram signature and moderator ID on each request, accepting launch data at most 30 minutes old with up to 30 seconds of future clock skew. There is no token exchange or refresh; close and reopen the Mini App from Telegram when the launch data expires.

After deployment, set the bot’s menu button to the HTTPS `review_static_web_app_url` in BotFather. See the [Telegram Mini App guide](docs/telegram-mini-app.md) for the required cutover sequence for existing deployments, BotFather setup, and authentication checks.

## 7. Configure Environment Parameters

Edit [dev.tfvars](src/infrastructure/environments/dev.tfvars) and [prod.tfvars](src/infrastructure/environments/prod.tfvars):

- `location`, environment names and prefixes.
- Non-overlapping `vnet_address_prefix` values.
- Existing GitHub runner VNet, resource group and subnet names.
- `bot_name`, including `@`, and `forwardSpamToChatId`.
- AI model names, versions and capacities for `spam_recognition`, `image_text_extractor` and `mention`.
- `disable_public_access`, according to the environment's network requirements.
- Optional `function_alert_emails`, `command_cooldown` and `spam_restriction_timeout`.

In `prod`, Key Vault and the data storage account use Azure service endpoints instead of Private Link. The public endpoints are enabled with the default network action set to deny; the Functions subnet and the GitHub runner subnet are allowlisted. Ensure both `Microsoft.KeyVault` and `Microsoft.Storage` service endpoints are enabled on the existing runner subnet before switching an existing deployment. Review the `prod` plan for private endpoint and DNS removal, and schedule the cutover in a maintenance window.

### Shared runner DNS for Private Link

For environments using Private Link, keep the Functions VNet linked to its own private DNS zones. All GitHub runners on the same VNet share one `privatelink.vaultcore.azure.net` zone and one `privatelink.table.core.windows.net` zone in the runner VNet's resource group. Environment Terraform states only manage their own temporary runner A records and private endpoints, not the shared zones or links. Grant deployment identities read access to both zones and links and permission to manage A records in the zones.

Provision the shared zones and links separately from application releases. If migrating from per-environment runner links, pause releases and destroys on that runner VNet and wait for active jobs to finish. In Azure, note the existing links to the runner VNet (including their resolution policy) so they can be restored if needed. Remove **only** links to the runner VNet. Keep the Functions VNet links, application DNS zones and private endpoints. Azure cannot link both the old and new zones of the same name to the runner VNet, so do this in a maintenance window; runner access to private environments resumes when their connectivity setup creates the A records. If the link cutover fails, remove any newly created shared VNet links before restoring the old ones.

Run the following in Bash with an Azure identity that manages the runner network. Set these values to match the runner VNet resource group and name in the environment configuration:

```bash
set -euo pipefail
runner_rg=rg-github-vm-runners
runner_vnet=vnet-github-runners
runner_vnet_id=$(az network vnet show -g "$runner_rg" -n "$runner_vnet" --query id -o tsv)
for zone in privatelink.vaultcore.azure.net privatelink.table.core.windows.net; do
  az network private-dns zone create -g "$runner_rg" -n "$zone"
  az network private-dns link vnet create -g "$runner_rg" -z "$zone" \
    -n "vnl-$runner_vnet" -v "$runner_vnet_id" -e false --resolution-policy NxDomainRedirect
done
```

`NxDomainRedirect` lets the runner resolve accounts that use service endpoints when they have no record in the shared zones. It does not grant network access through a firewall. Start a **new** release with the updated configuration, not a retry of the old revision. Before approving apply, check that the runner resolves the target vault and Table Storage names to their respective runner endpoint IPs and that the plan preserves the Functions VNet links where Private Link remains enabled. After cleanup, check that the shared links still exist and only the released environment's temporary records and runner endpoints are gone.

When switching back to service endpoints (`disable_public_access = false`), setup reads the current Azure network access setting for Key Vault and Storage separately. It restores runner private connectivity only for services that still have public network access disabled. Shared runner VNet links are permanent and do not change in either mode.

The environment's Key Vault and Table private DNS zones also stay in place across both modes. In service-endpoint mode, Terraform removes the Functions VNet links and private A records, so these zones do not affect DNS resolution. Existing zones stay under Terraform management and are reused when Private Link is enabled again. A full environment destroy still attempts to delete them. If Azure returns `409 CannotDeleteResource` even though listing links returns none and fetching the named links returns `NotFound`, contact Azure support with the failed delete's Activity Log correlation ID; do not remove the shared runner links.

The data storage private endpoint NICs use `nic-data-storage-pe-<environment>-tables`,
`nic-data-storage-pe-<environment>-queue`, and `nic-data-storage-pe-<environment>-blob`.
Applying a NIC name change replaces the associated private endpoint. Review the
Terraform plan and schedule a maintenance window if existing endpoints are affected.

Function failure alerts evaluate failed Application Insights requests every five
minutes over a five-minute window. This query does not support Azure Monitor's
one-minute evaluation frequency.

## Search storage and embeddings

[Channel search: from Telegram posts to search results](docs/channel-search.md).

Slash commands also accept an explicit bot name (for example, `/stat@BotName`). In a channel's linked discussion group, use `/search <query>` (or `/search@BotName <query>`). The bot resolves the linked channel and replies in the same chat with up to five original post links. It ranks posts by their best matching vector fragment, boosts posts whose titles contain the full query, and excludes posts below the relevance threshold. Results link to the channel and require normal Telegram access to it. The bot replies when the index is not ready or no results are found. For other cases where search cannot run, it deletes the command without replying when Telegram permits deletion. If the snapshot is missing, build it from the **Search snapshots** review tab.

Search caches one channel snapshot per worker, checks its ETag at most once every 30 seconds while serving that channel, and never downloads an unchanged snapshot again. The in-memory reader supports snapshots up to 160 MiB.

The `Vectors` repository in Persistence stores post titles and binary vector chunks in the bot's data storage account. The Search project reuses `OpenAiServicesConfiguration` for the endpoint and credentials; `OpenAiServicesConfiguration__SearchEmbeddingDeployment` selects the deployment. The index uses `text-embedding-3-large` version 1 (3,072 dimensions).

Automatic copies of channel posts in discussion groups (including edits) bypass commands, spam checks, photo analysis, and message history. The webhook still queues them for indexing when they have a channel `forward_origin` and nonblank text or a caption, and waits for the write before acknowledging Telegram. Index keys use the original channel and post IDs, not the discussion copy's IDs. Telegram's service sender (`777000`) also bypasses moderation, but its ID alone does not qualify a message for indexing. User forwards and comments follow the normal moderation path; direct channel posts still go through the usual history processing.

`IndexChannelPost` reads `search-index`, batches paragraph-based fragments for embeddings, and atomically replaces the post vectors. Duplicate or stale jobs do not rewrite vectors. Failed deliveries are retried by the Functions host and move to `search-index-poison` after the retry limit; jobs, including poison messages, carry temporary source text. After indexing, the worker schedules a delayed `reset-snapshot` message. `RebuildSearchSnapshot` assembles the channel's `Vectors` partition into a checksummed binary snapshot and publishes it as `<channel-id>` (no extension) in the private `search-snapshots` container. Pending requests covered by an already-built snapshot are skipped; exhausted retries go to `reset-snapshot-poison`.

In **Snapshots**, select a discussion group from the shared chat catalog and tap the upload icon. The bot resolves the linked source channel and displays its snapshot status. You can queue a rebuild even if no snapshot has been published yet. Tap the same upload icon after the worker finishes. A channel with no indexed posts does not produce a blob.

The indexing queues use the data storage account; the Functions host and deployment container keep their separate Function App storage account. If the queues were previously deployed in Function App storage, pause producers and drain or back up both queues before applying the change. Terraform replaces the queues and does not migrate messages.

### One-off Telegram archive import

[ChannelArchiveImport](tools/ChannelArchiveImport/ChannelArchiveImport.csproj) reads `messages.html`, `messages2.html`, etc. from a Telegram Desktop **channel HTML export**. Pass the channel's numeric Bot API ID (not the discussion group ID). Dry-run is the default and does not contact Azure:

```powershell
$archive = 'C:\path\to\ChatExport'
$channelId = '-1001234567890' # Replace with the export's channel ID.
dotnet run --project .\tools\ChannelArchiveImport -- $archive $channelId
```

Once the indexing worker is deployed, sign in with `az login` using an identity with **Storage Queue Data Message Sender** on the destination queue. Run the importer from a network allowed by the storage firewall. For `prod`, use the allowlisted runner subnet with the `Microsoft.Storage` service endpoint enabled; for `ppr`, use a network with access to the queue's private endpoint and DNS. Use the data storage account for your environment (`satgbotdataprod` in production) and send a small test batch:

```powershell
$queue = 'https://<data-storage-account>.queue.core.windows.net/search-index'
dotnet run --project .\tools\ChannelArchiveImport -- $archive $channelId --send --queue-url $queue --limit 5
```

Remove `--limit` for the full archive. The worker skips repeated archive jobs and does not overwrite newer webhook revisions. Posts without text or captions and oversized queue payloads are skipped and counted; oversize skips produce a nonzero exit code. For a local send, use `--send --azurite` with an existing `search-index` queue.

## 8. Deploy and Verify

**Existing deployments:** Complete the [one-time Easy Auth cutover](docs/telegram-mini-app.md#one-time-cutover-for-existing-deployments) before applying the new Terraform plan. Do not opt into removal until the guarded backend is deployed.

1. Run [release dev](.github/workflows/release-dev.yaml) manually, or push to `main` to deploy `dev` automatically. Leave the cutover input off for new environments and routine releases.
2. Verify OIDC login, Terraform, Functions, website deployment, and runner-endpoint cleanup.
3. Obtain `review_static_web_app_url` from Terraform and set it as the bot menu button URL via BotFather. If the Static Web App is recreated and its hostname changes, update the menu URL.
4. Open the Mini App from inside the bot's Telegram chat. Verify that an allowlisted moderator can use it, an unlisted user cannot, and review API calls without valid Telegram launch data fail.
5. Send a message to the development Telegram group and verify webhook delivery.
6. Run [release prod](.github/workflows/release-prod.yaml) manually, explicitly selecting `ppr` or `prod`, and repeat the same checks.

The Functions origin is supplied at build time as `API_URL`. Changing it requires rebuilding the Mini App. Functions CORS automatically includes the deployed Mini App origin and does not allow browser credentials.

After development tests, run [destroy-ci](.github/workflows/destroy-ci.yaml) with environment `dev` to remove its infrastructure and data. Do not select `prod` for routine cleanup.

## 9. Run Locally

See the [local development guide](docs/local-development.md) for the complete Windows setup with Azurite, Vite, and real Telegram sign-in.

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
       "ReviewTelegram__ModeratorIds": "123456789",
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

   Keep the local Functions host bound to loopback. Telegram review authentication is also enforced locally.

4. Create `src/web/.env` with the public configuration:

   ```dotenv
   API_URL=http://localhost:7071
   ```

5. In another terminal, from the repository root:

   ```powershell
   Set-Location src\web
   npm ci
   npm run dev
   ```

   Open `http://localhost:5173` to check the signed-out screen, and restart Vite after changing `.env`. A regular browser tab does not have a signed Telegram launch. Test sign-in with the deployed development bot and its HTTPS Mini App URL.

6. For local Telegram delivery, expose the development webhook through a public HTTPS tunnel and register it from the repository root:

   ```powershell
   dotnet run --project src\bot\TelegramAntiSpamBot.Initializer -- "<dev-bot-token>" "https://<public-host>/api/AntiSpamBotWebHook" "<webhook-header>"
   ```

   Registration replaces the bot's current webhook; use only the development bot. Deployment workflows register the Azure webhook automatically.

See [the web setup guide](src/web/README.md) for additional frontend configuration.

## 10. Mention Agent and Web Search

Messages starting with `@BotName` invoke a Microsoft Agent Framework agent backed by Azure OpenAI Responses. The chat-level command cooldown applies to mentions and `/report`, but not to `/search` or `/stat`. A new agent run is created for each mention; conversations are not shared between chats or requests, and Responses storage is disabled (`store: false`).

The agent has hosted web search backed by Bing and, for Telegram mentions, local `get_message_history` and `search_channel_posts` tools. Ordinary questions can be answered without tools. Initial mention payloads contain `Question`, `From`, explicit `ReplyTo` and `Conversation` metadata (chat/message IDs, UTC mention date and optional reply date), not surrounding messages. Requests without Telegram conversation context do not expose either local tool.

To find a channel post, ask `@BotName найди пост про async/await` in the linked discussion group. The agent uses the same index as `/search` and receives up to five candidate titles and original post links. It selects only the best matches and can return just one link, even when the index returns five.

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
