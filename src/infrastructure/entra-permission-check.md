# Entra Permission Check

The main Terraform root now manages `azuread_application.permission_check` in
the existing Azure Storage backend, alongside the bot infrastructure. There is
no separate workflow or local state. Run the normal infrastructure pipeline.

The AzureAD provider uses the same tenant as the AzureRM provider and the
pipeline's existing `ARM_TENANT_ID`, `ARM_CLIENT_ID`, and `ARM_CLIENT_SECRET`.
The check therefore tests the CI service principal, not your personal account.
Azure subscription RBAC alone does not grant application registration rights
in Entra.

The application name includes the environment prefix. It has no secrets, API
permissions, or service principal. Outputs expose its client ID and object ID.
The application remains managed after apply; there is no automatic cleanup.

A successful apply confirms application creation permissions. Plan alone does
not. A Graph `403` / `Authorization_RequestDenied` usually indicates insufficient
permissions or tenant policy; authentication or network failures do not prove
such a restriction. This check does not establish permission to grant admin
consent, assign users, or configure Easy Auth, and does not enable website login.

If the previous standalone workflow was already run and left an application
behind, recover its state or locate the application before applying this root.
Import the application into the main state or remove that old test application
first. This change does not migrate any previously created standalone state.