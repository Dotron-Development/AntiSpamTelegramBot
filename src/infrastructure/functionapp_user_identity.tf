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

## Grant Read access for the function app to the Open AI services in the resource group
resource "azurerm_role_assignment" "function_app_to_openai" {
  scope                = azurerm_resource_group.rg.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = data.terraform_remote_state.data.outputs.functionapp_identity_principal_id
}
