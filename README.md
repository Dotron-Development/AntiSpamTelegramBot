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

Protect `prod` with deployment branch restrictions and approval rules as appropriate.

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
- AI model names, versions and capacities for `spam_recognition`, `image_text_extractor`, `chronicle` and `mention`.
- `disable_public_access`, according to the environment's network requirements.
- Optional `function_alert_emails`, `command_cooldown` and `spam_restriction_timeout`.

## 8. Deploy and Verify

1. Run [release dev](.github/workflows/release-dev.yaml) manually from GitHub Actions, or push to `main` to deploy `dev` automatically.
2. Verify OIDC login, infrastructure deployment, Functions deployment, website deployment and runner-endpoint cleanup.
3. Obtain the `review_static_web_app_url` Terraform output and add it as a **Single-page application** redirect URI in the review registration.
4. Open that URL, sign in as an assigned moderator and verify API access. Check that unauthenticated review API requests are rejected.
5. Send a message to the development Telegram group and verify webhook delivery.
6. Run [release prod](.github/workflows/release-prod.yaml) manually, explicitly selecting `prod`. Repeat the sign-in and webhook checks for production.

The website deploys to Azure Static Web Apps Free. Its build settings are supplied by Terraform and the build job; changing them requires another deployment. Function CORS includes the deployed website origin automatically.

After development tests, run [destroy-ci](.github/workflows/destroy-ci.yaml) with environment `dev` to remove its infrastructure and data. Do not select `prod` for routine cleanup.

## 9. Run Locally

1. Sign in with `az login`. Ensure your account can access the AI deployments and application Table Storage. For private resources, configure working network access and DNS.
2. Configure the ignored [local.settings.json](src/bot/TelegramAntiSpamBot.Functions/local.settings.json) with the Telegram bot token, webhook header, bot username, Azure AI endpoint and deployment names, and Table Storage URL. Use Azurite if `AzureWebJobsStorage` is `UseDevelopmentStorage=true`. Keep real credentials outside version control.

   ```json
   {
     "IsEncrypted": false,
     "Values": {
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
       "TelegramBotConfiguration__Token": "<dev-bot-token>",
       "TelegramBotConfiguration__SecretHeader": "<webhook-header>",
       "TelegramBotConfiguration__BotName": "@YourDevBotName",
       "OpenAiServicesConfiguration__ServiceUrl": "https://<ai-service-endpoint>/",
       "OpenAiServicesConfiguration__SpamRecognitionDeployment": "antispam",
       "OpenAiServicesConfiguration__ImageRecognitionDeployment": "images",
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
