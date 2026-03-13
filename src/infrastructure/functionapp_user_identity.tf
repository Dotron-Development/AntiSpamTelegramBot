resource "azurerm_user_assigned_identity" "functionapp_identity" {
  location            = var.location
  name                = "ua-mi-${local.appName}-fn-${var.environment_prefix}"
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.tags
}

## Grant the identity access to the data storage account
resource "azurerm_role_assignment" "functionapp_identity_data_storage_access" {
  scope                = azurerm_storage_account.data_storage.id
  role_definition_name = "Storage Table Data Contributor"
  principal_id         = azurerm_user_assigned_identity.functionapp_identity.principal_id
}

## Grant Read access for the function app to the Open AI services in the resource group
resource "azurerm_role_assignment" "function_app_to_openai" {
  scope                = azurerm_resource_group.rg.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = azurerm_user_assigned_identity.functionapp_identity.principal_id
}

## Grant the identity access to the application storage account
resource "azurerm_role_assignment" "functionapp_identity_storage_access_app_blob" {
  scope                = azurerm_storage_account.function_storage.id
  role_definition_name = "Storage Blob Data Owner"
  principal_id         = azurerm_user_assigned_identity.functionapp_identity.principal_id
}

resource "azurerm_role_assignment" "functionapp_identity_storage_access_app_queue" {
  scope                = azurerm_storage_account.function_storage.id
  role_definition_name = "Storage Queue Data Contributor"
  principal_id         = azurerm_user_assigned_identity.functionapp_identity.principal_id
}

resource "azurerm_role_assignment" "functionapp_identity_storage_access_app_table" {
  scope                = azurerm_storage_account.function_storage.id
  role_definition_name = "Storage Table Data Contributor"
  principal_id         = azurerm_user_assigned_identity.functionapp_identity.principal_id
}

