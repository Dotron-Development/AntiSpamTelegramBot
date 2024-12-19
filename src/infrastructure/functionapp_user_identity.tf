resource "azurerm_user_assigned_identity" "functionapp_identity" {
  location            = var.location
  name                = "ua-ma-for-assistant-function-${var.environment_prefix}"
  resource_group_name = azurerm_resource_group.rg.name
}

## Grant the identity access to the storage account
resource "azurerm_role_assignment" "functionapp_identity_storage_access" {
  scope                = azurerm_storage_account.main_storage.id
  role_definition_name = "Storage Table Data Contributor"
  principal_id         = azurerm_user_assigned_identity.functionapp_identity.principal_id
}
