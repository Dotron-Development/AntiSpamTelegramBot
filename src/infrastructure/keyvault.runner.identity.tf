# Role assignments for the Terraform runner identity (data.azurerm_client_config.current)

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

