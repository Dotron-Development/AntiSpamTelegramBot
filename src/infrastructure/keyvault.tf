resource "azurerm_key_vault" "kv" {
  name                        = "${local.kv_name}-${var.environment_prefix}"
  location                    = var.location
  resource_group_name         = azurerm_resource_group.rg.name
  enabled_for_disk_encryption = false
  tenant_id                   = data.azurerm_client_config.current.tenant_id
  soft_delete_retention_days  = 7
  purge_protection_enabled    = false
  sku_name                    = "standard"
  rbac_authorization_enabled  = true


  ## network rules
  public_network_access_enabled = !var.disable_public_access

  network_acls {
    bypass         = "None"
    default_action = "Deny"

    # only if public access is enabled
    virtual_network_subnet_ids = !var.disable_public_access ? [
      data.azurerm_subnet.github_runner_vnet_subnet.id,
      azurerm_subnet.subnet1_functions.id
    ] : []
  }

  tags = local.tags
}
