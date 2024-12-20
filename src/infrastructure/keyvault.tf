resource "azurerm_key_vault" "kv" {
  name                        = "${local.kv_name}-${var.environment_prefix}"
  location                    = var.location
  resource_group_name         = azurerm_resource_group.rg.name
  enabled_for_disk_encryption = true
  tenant_id                   = data.azurerm_client_config.current.tenant_id
  soft_delete_retention_days  = 7
  purge_protection_enabled    = false
  sku_name                    = "standard"

  ## network rules
  public_network_access_enabled = true
  network_acls {
    bypass         = "AzureServices"
    default_action = "Allow"
  }

  ## access for the owner of the application
  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = var.application_owner_object_id

    key_permissions = [
      "Get",
      "List",
      "Update",
      "Create",
      "Import",
      "Delete",
      "Recover",
      "Backup",
      "Restore",
      "Decrypt",
      "Encrypt",
      "UnwrapKey",
      "WrapKey",
      "Verify",
      "Sign",
      "Purge",
      "Release",
      "Rotate",
      "GetRotationPolicy",
      "SetRotationPolicy"
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

resource "azurerm_private_dns_zone" "private_dns" {
  name                = "${local.kv_name}.privatelink.vaultcore.azure.net"
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "keyvault_vnet_link" {
  name                  = "$vnl-${local.kv_name}-${var.environment_prefix}"
  virtual_network_id    = azurerm_virtual_network.vnet.id
  private_dns_zone_name = azurerm_private_dns_zone.private_dns.name
  resource_group_name   = azurerm_resource_group.rg.name
  tags                  = local.tags
}

resource "azurerm_private_endpoint" "kv_pe" {
  name                = "pe-${local.kv_name}-${var.environment_prefix}"
  subnet_id           = azurerm_subnet.subnet_for_vm.id
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.tags

  private_service_connection {
    name                           = "sc-${local.kv_name}-${var.environment_prefix}"
    private_connection_resource_id = azurerm_key_vault.kv.id
    is_manual_connection           = false
    subresource_names              = ["vault"]
  }

  ip_configuration {
    name               = "ip-${local.kv_name}-${var.environment_prefix}"
    private_ip_address = "10.0.1.33"
    subresource_name   = "vault"
    member_name        = "default"
  }
}
