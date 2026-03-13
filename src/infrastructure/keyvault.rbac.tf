# allows the application owner to fully manage the key vault
resource "azurerm_role_assignment" "owner_allow_all" {
  scope                = azurerm_key_vault.kv.id
  role_definition_name = "Key Vault Administrator"
  principal_id         = var.keyvault_administrator_object_id
}

resource "azurerm_role_assignment" "runner_kv_data_access_admin" {
  scope                = azurerm_key_vault.kv.id
  role_definition_name = "Key Vault Data Access Administrator"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "runner_kv_administrator" {
  scope                = azurerm_key_vault.kv.id
  role_definition_name = "Key Vault Administrator"
  principal_id         = data.azurerm_client_config.current.object_id
}

# allows the azure functions to read secrets
resource "azurerm_role_assignment" "azure_functions_read_secrets" {
  scope                = azurerm_key_vault.kv.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.functionapp_identity.principal_id
}

