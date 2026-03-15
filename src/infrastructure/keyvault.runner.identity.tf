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

# RBAC assignments are accepted immediately but take time to propagate across
# Azure's authorization service. Without this sleep, secret operations can fail
# with 403 ForbiddenByRbac even though the assignment was just created.
resource "time_sleep" "kv_rbac_propagation" {
  create_duration = "90s"

  depends_on = [
    azurerm_role_assignment.runner_kv_administrator,
    azurerm_role_assignment.runner_kv_data_access_admin,
  ]
}

