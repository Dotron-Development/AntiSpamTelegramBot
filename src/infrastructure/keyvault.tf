resource "azurerm_key_vault" "kv" {
  name                        = "kv-assistant-${var.environment_prefix}"
  location                    = var.location
  resource_group_name         = azurerm_resource_group.rg.name
  enabled_for_disk_encryption = true
  tenant_id                   = data.azurerm_client_config.current.tenant_id
  soft_delete_retention_days  = 7
  purge_protection_enabled    = false
  sku_name                    = "standard"

  ## access for the owner of the application
  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = var.application_owner_object_id

    key_permissions = [
      "get",
      "list",
      "set",
      "delete",
      "backup",
      "restore",
      "recover",
      "purge"
    ]

    secret_permissions = [
      "get",
      "list",
      "update",
      "create",
      "import",
      "delete",
      "backup",
      "restore",
      "recover",
      "purge",
      "sign",
      "verify",
      "wrapKey",
      "unwrapKey",
      "encrypt",
      "decrypt"
    ]

    storage_permissions = [
      "get",
      "list",
      "update",
      "create",
      "import",
      "delete",
      "managecontacts",
      "getissuers",
      "listissuers",
      "setissuers",
      "deleteissuers",
      "manageissuers",
      "recover",
      "purge"
    ]
  }

  ## access for the function's user assigned identity
  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = azurerm_user_assigned_identity.functionapp_identity.principal_id

    secret_permissions = ["get"]
  }
}


