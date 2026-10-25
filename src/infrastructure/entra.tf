resource "azuread_application" "permission_check" {
  display_name     = "${local.appName}-${var.environment_prefix}-entra-permission-check"
  sign_in_audience = "AzureADMyOrg"
}