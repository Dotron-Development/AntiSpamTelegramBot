# Azure Functions Easy Auth

This setup protects the review API at the Azure platform boundary. The SPA keeps
MSAL and sends access tokens in the Authorization header. Functions contains no
Entra JWT validator or role middleware. Static frontend assets are still public;
the review API and its data are protected separately.

## Required Values and Permissions

- Client ID of the existing single-tenant registration. Its tenant must match
  the existing Azure deployment tenant (`ARM_TENANT_ID`); no separate review
  tenant setting is required.
- Object IDs of permitted users: Entra ID > Users > user > Overview > Object ID.
  Do not use an email address, app client ID, or Enterprise application object ID.
- Azure permission to update the Function App authentication configuration,
  such as an appropriately scoped Contributor role. Graph application-registration
  permissions are not required to configure an existing registration here.

Complete the [Entra registration setup](entra-id-setup.md) for the SPA first.
Use access-token version 2 and scope `api://<CLIENT_ID>/Review.Access`.

## Authorization Model

Easy Auth checks token validity, tenant-specific issuer, audience, caller client
ID, and an explicit list of allowed user object IDs. Configure BOTH the client
and user lists. An empty list is not a deny-all policy; it removes that restriction.

Easy Auth does not automatically require `roles=Moderator` or the exact
`scp=Review.Access` value. The frontend role check is only a UI check. Removing
a Moderator assignment alone is not an immediate API-access revocation: remove
the user from the Easy Auth allowlist as well. Token/session lifetime and platform
propagation still apply. If role/scope claims must be enforced directly, use an
API gateway policy or application authorization instead of this allowlist model.

## Option A. Terraform

AzureRM 5.11.0 supports `auth_settings_v2` directly on the existing
`azurerm_function_app_flex_consumption.function_app` resource. No AzureAD provider
or separate state is needed. This is implemented in
[functionapp.tf](../src/infrastructure/functionapp.tf), with the tenant obtained
from the existing AzureRM client configuration. It takes effect on apply.

The existing registration's `review_client_id` is stored in the environment
variable files under `src/infrastructure/environments` (`dev.tfvars`,
`ppr.tfvars`, `prod.tfvars`). All three currently use the same registration.
No `REVIEW_CLIENT_ID` GitHub variable is required.

For each GitHub Environment (`dev`, `ppr`, `prod`), configure these variables:

| Variable | Value |
| --- | --- |
| `REVIEW_USER_OBJECT_IDS` | JSON array, for example `["<USER_OBJECT_ID>"]` |
| `REVIEW_FRONTEND_ORIGINS` | Optional JSON array of additional origins; defaults to `["http://localhost:5173"]`; use `[]` to allow only the deployed SPA |

All workflows that plan the shared Terraform root load the selected environment
variable file and pass the remaining values as `TF_VAR_review_user_object_ids`
and `TF_VAR_review_frontend_origins`. For local Terraform commands, use
`-var-file=environments/<ENVIRONMENT>.tfvars` and supply the remaining inputs
through `TF_VAR_*` environment variables or a private variable file.
The inputs are not read from the frontend `.env`. Empty user lists, malformed
GUIDs, and user lists exceeding the platform's 500-character limit are rejected.

The Function policy requires HTTPS, returns 401 without authentication, restricts
both caller client and user identities, and excludes only
`/api/AntiSpamBotWebHook`. Do not manage the same settings through both Terraform
and an independent ARM resource.

This is bearer-token validation only: MSAL performs interactive sign-in in the
SPA. No Easy Auth client secret, token store, Web callback registration, or
server-side login flow is needed for this scenario. Do not enable implicit
grants to accommodate a portal sign-in wizard.

Function CORS is managed alongside authentication. The deployed SPA origin is
always included, in addition to `review_frontend_origins`; cookie credentials
are disabled. Wildcards are rejected. The host must accept preflight requests
and allow Authorization and Content-Type headers. Review the full plan before
apply, including the new paid SPA hosting resources.

### SPA Hosting and Build

[webapp.tf](../src/infrastructure/webapp.tf) creates a separate Linux App Service
plan and Web App. The plan defaults to the paid `B1` SKU; change
`review_web_app_sku` in the environment's Terraform variable file if needed.
It cannot share the Function App's Flex Consumption plan. Node.js 24 runs PM2's
static server with SPA fallback; the published package contains only `dist`.

Terraform sets these public Web App application settings:

| Setting | Source |
| --- | --- |
| `API_URL` | Function App's actual default hostname, over HTTPS |
| `TENANT_ID` | Existing Azure deployment tenant |
| `CLIENT_ID` | `review_client_id` |
| `API_SCOPE` | `api://<CLIENT_ID>/Review.Access` |
| `REDIRECT_URI` | Deployed SPA's HTTPS origin |

[webapp.yaml](../.github/workflows/webapp.yaml) runs after infrastructure and
Functions deployment in both release workflows. It reads and validates only
these five settings for the frontend build, runs `npm ci`, lint and build,
uploads the static artifact, then deploys it to the Web App. No Function key or
client secret is embedded. ZIP deployment uses the existing Azure login, not a
publish profile or Basic authentication. Vite uses build-time settings: changing Web App
settings requires another SPA build/deployment, not just a restart.

After infrastructure deployment, obtain `review_web_app_url` from Terraform
outputs and add that exact URL as a **Single-page application** redirect URI
in the existing Entra registration before signing in. Keep the local redirect
URI if local development is still needed. No Graph registration changes are
performed by Terraform.

## Option B. Azure Portal and ARM Configuration

1. Open Function App > Settings > Authentication > Add identity provider.
2. Choose Microsoft and provide the details of the existing registration.
3. Client ID: the same `CLIENT_ID` used by the SPA.
4. Issuer: `https://login.microsoftonline.com/<TENANT_ID>/v2.0`.
5. Require authentication; unauthenticated requests: HTTP 401, not a redirect.
6. Disable token store. Do not create a new registration or a client secret for
   bearer-only validation.
7. Under additional checks, restrict client applications to this client ID and
   identities to the permitted user object IDs, where those controls are exposed.
8. Configure the exact webhook exclusion before considering setup complete.

Some portal versions do not expose all authorization/exclusion properties. Use
ARM Resource Explorer or the authentication V2 REST API for those fields. Read
the complete existing object first, preserve unrelated settings, and edit:

```text
properties.platform.enabled = true
properties.globalValidation.requireAuthentication = true
properties.globalValidation.unauthenticatedClientAction = Return401
properties.globalValidation.excludedPaths = ["/api/AntiSpamBotWebHook"]
properties.httpSettings.requireHttps = true
properties.identityProviders.azureActiveDirectory.enabled = true
properties.identityProviders.azureActiveDirectory.registration.clientId = <CLIENT_ID>
properties.identityProviders.azureActiveDirectory.registration.openIdIssuer = https://login.microsoftonline.com/<TENANT_ID>/v2.0
properties.identityProviders.azureActiveDirectory.validation.allowedAudiences = ["<CLIENT_ID>"]
properties.identityProviders.azureActiveDirectory.validation.defaultAuthorizationPolicy.allowedApplications = ["<CLIENT_ID>"]
properties.identityProviders.azureActiveDirectory.validation.defaultAuthorizationPolicy.allowedPrincipals.identities = ["<USER_OBJECT_ID>"]
properties.login.tokenStore.enabled = false
```

Use the current Azure CLI subscription and the intended Function App. In
PowerShell, substitute the resource group and Function App name:

```powershell
$appId = az functionapp show --resource-group "<RESOURCE_GROUP>" --name "<FUNCTION_APP>" --query id --output tsv
$url = "https://management.azure.com${appId}/config/authsettingsV2?api-version=2023-12-01"
az rest --method get --url $url --output json > authsettingsV2.json
```

Edit the saved JSON's `properties`, preserving the rest of the configuration.
Then apply and re-read it:

```powershell
az rest --method put --url $url --body '@authsettingsV2.json' --output none
az rest --method get --url $url --query properties --output json
```

The webhook exclusion and allowlists must be present in the same final policy.
Do not exclude `/api/*` or `/api/review/*`. Do not leave only tenant-wide login
enabled: that alone allows more users than the intended moderators.
Portal/REST changes should be reconciled with Terraform before future applies.

## Safe Deployment Order and Function Keys

The current review triggers use `AuthorizationLevel.Function` as a safe default.
Function keys and Easy Auth are independent gates. There is no Easy Auth setting
that removes the Functions host's key requirement.

1. Enable Easy Auth with the user/client restrictions and exact webhook exclusion.
2. Verify its saved configuration and verify the webhook is not redirected.
3. Change ONLY the four review HttpTrigger attributes from
   `AuthorizationLevel.Function` to `AuthorizationLevel.Anonymous`, update their
   trigger-level test accordingly, and deploy. Keep business logic and the
   Telegram authorization middleware unchanged. No JWT middleware is needed.
4. Test Bearer-only calls without a Function key. Do not publish Anonymous review
   endpoints to an environment where Easy Auth is disabled.

Until step 3, the MSAL frontend cannot call these key-protected routes with only
a Bearer token. If absolutely no trigger-code changes are acceptable, retain
Function authorization and put a trusted server/gateway in front that supplies
the key; do not embed a shared Function key in the SPA.

## Verification

- A GET to `/api/review/chats` with no token returns 401, not a Microsoft redirect.
- An invalid/expired access token returns 401.
- A valid token for this API from a user outside the allowlist returns 403.
- A valid token from an allowed moderator and allowed client succeeds after
  review triggers are deployed as Anonymous, without `x-functions-key`.
- An ID token is not a supported API credential; confirm it cannot substitute
  for an access token under the configured client policy.
- POST `/api/AntiSpamBotWebHook` without the Telegram secret returns the worker's
  403, not an Easy Auth 401/redirect. Actual Telegram delivery still succeeds.
- Browser preflight works from the registered SPA origin. CORS is not an access
  control substitute.

Easy Auth protects the whole Function App except excluded paths, not just one
function. The route above follows the current default `/api` prefix and function
name; revisit it if host routing changes.

Easy Auth is not emulated by `func start`. After changing triggers to Anonymous,
the local review API has no Entra gate. Bind it only to loopback, do not expose
it through a tunnel, and use the deployed Azure API for authentication tests.

## References

- [Microsoft Entra provider and built-in authorization policies](https://learn.microsoft.com/en-us/azure/app-service/configure-authentication-provider-aad)
- [AzureRM 5.11.0 Flex Consumption resource](https://github.com/hashicorp/terraform-provider-azurerm/blob/v5.11.0/website/docs/r/function_app_flex_consumption.html.markdown)