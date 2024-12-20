resource "azurerm_key_vault" "kv" {
  name                        = "kv-assistant-${var.environment_prefix}"
  location                    = var.location
  resource_group_name         = azurerm_resource_group.rg.name
  enabled_for_disk_encryption = true
  tenant_id                   = data.azurerm_client_config.current.tenant_id
  soft_delete_retention_days  = 7
  purge_protection_enabled    = false
  sku_name                    = "standard"

  ## network rules
  public_network_access_enabled = false
  network_acls {
    bypass         = "AzureServices"
    default_action = "Allow"
  }

  ## access for the owner of the application
  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = var.application_owner_object_id

    key_permissions = [
      "All"
    ]

    secret_permissions = ["Get", "List", "Set", "Delete", "Recover", "Backup", "Restore", "Purge"]
    storage_permissions = [
      "Backup",
      "Delete",
      "DeleteSAS",
      "Get",
      "GetSAS",
      "List",
      "ListSAS",
      "Purge",
      "Recover",
      "RegenerateKey",
      "Restore",
      "Set",
      "SetSAS",
      "Update"
    ]
  }

  ## access for the function's user assigned identity
  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = azurerm_user_assigned_identity.functionapp_identity.principal_id

    secret_permissions = ["Get"]
  }

  ## access for terraform to manage the key vault
  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = data.azurerm_client_config.current.object_id

    key_permissions     = ["Get", "List"]
    secret_permissions  = ["Get", "List", "Set", "Delete", "Purge"]
    storage_permissions = ["Get"]
  }
}


