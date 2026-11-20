# Telegram Review Mini App

The existing Azure Static Web App still serves the review UI. Its only build setting is the public Functions origin (`API_URL`, from Terraform output `review_api_url`). Functions CORS includes the deployed Static Web App and configured additional origins, without browser credentials. CORS is not an access control.

Set `review_telegram_moderator_ids` in the corresponding Terraform file: [dev.tfvars](../src/infrastructure/environments/dev.tfvars), [ppr.tfvars](../src/infrastructure/environments/ppr.tfvars), or [prod.tfvars](../src/infrastructure/environments/prod.tfvars). Use comma-separated positive decimal Telegram **user IDs** with no spaces (for example `123456789,987654321`). Terraform supplies `ReviewTelegram__ModeratorIds` to Functions. An empty or unset value denies all moderators. The backend uses the environment's existing bot token; do not add it to the frontend or introduce a new secret.

## Review API authentication

Every review API request sends `Authorization: tma <raw Telegram initData>` using the exact signed launch string from Telegram, without decoding or re-encoding it. On each request, Functions verifies the signature using the environment's bot token, rejects launch data older than 30 minutes or more than 30 seconds in the future, and checks the Telegram user ID against the moderator allowlist. There is no token exchange or refresh. When launch data expires, close and reopen the Mini App from Telegram. Treat launch data as a credential: do not log or share it.

## One-time cutover for existing deployments

**Deploy the new backend before running the first release that removes Easy Auth.** The legacy Functions package relies on Easy Auth to protect anonymous review triggers. The release applies Terraform before deploying Functions and does not check which package is currently deployed. If a release starts before the new backend is in place (including a push to `main` for `dev`), do not approve Apply.

Do this separately in `dev`, then `ppr`, then `prod`:

1. Schedule a short review UI outage. Cancel any queued/running releases from before the migration and do not rerun old deployment jobs. Check out the migration revision approved for the environment. Verify before deploying that the access endpoint and every other review route are covered by the Telegram middleware. Set the environment's moderator IDs.
2. With the existing Easy Auth configuration and webhook untouched, publish and deploy **only** the Functions package built from that same revision. Use the existing approved Functions deployment access. For a manual deployment, run the following from the repository root after authenticating to the correct subscription with the existing deployment access; replace `<environment>` with `dev`, `ppr`, or `prod`.

   ```powershell
   $environment = '<environment>'
   $package = 'src\bot\TelegramAntiSpamBot.Functions\published'
   $zip = 'guarded-functions.zip'
   dotnet publish .\src\bot\TelegramAntiSpamBot.Functions --configuration Release --os linux --arch x64 --output $package
   if ($LASTEXITCODE -ne 0) { throw 'Functions publish failed' }
   Compress-Archive -Path "$package\*" -DestinationPath $zip
   az functionapp deployment source config-zip --resource-group "rg-ai-tg-bot-$environment" --name "fn-ai-tg-bot-$environment" --src $zip
   if ($LASTEXITCODE -ne 0) { throw 'Functions deployment failed' }
   ```

   Start with clean package and ZIP paths, and remove both afterward. Confirm Azure lists `CheckTelegramReviewAccess` for the target Function App. Stop if deployment or verification fails. Easy Auth and the Telegram middleware are both active during this interval, so the old Entra UI will not work.
3. Run the normal release workflow for that same revision and environment. Review the plan before approving Apply; the Function App must update in place, removing the Easy Auth block and enabling HTTPS independently. The workflow redeploys Functions from the release revision, then deploys the Mini App. Runner-endpoint cleanup still runs after a failed release; if the run is cancelled or cleanup fails, verify and remove any remaining temporary endpoints before leaving the environment idle.
4. Perform the checks below. Remove obsolete `REVIEW_USER_OBJECT_IDS` variables and retire the old review-only Entra registration once no environment uses it; do not change the Azure deployment OIDC registration.

**Rollback:** While Easy Auth is disabled, redeploy only a revision that enforces per-request Telegram authorization on all review routes. Before restoring the legacy backend, restore the original Easy Auth rules, including the review identity allowlist, and verify the platform rejects unauthenticated requests.

For a new environment, use the normal release; there is no legacy package to expose.

## Configure BotFather and verify

1. Get `review_static_web_app_url` from Terraform. In **@BotFather**, send `/setmenubutton`, select the matching environment's bot, and enter a label such as `Review` and that exact HTTPS URL. Update the menu if the Static Web App hostname ever changes.
2. Open the bot's private chat in Telegram and select the menu button. Verify that an allowlisted moderator can load the review queue and an unlisted user is denied. Opening the HTTPS URL directly in a browser does not authenticate.
3. Verify `GET /api/review/access` returns `204` for fresh, signed launch data from an allowlisted moderator, `401` for missing, invalid, or expired launch data, and `403` for fresh, signed launch data from an unlisted user. Verify that `GET /api/review/chats` without authorization also returns `401`, not a Microsoft sign-in redirect. If those checks fail, stop and restore Easy Auth protection before proceeding. Verify webhook delivery with a test message.
