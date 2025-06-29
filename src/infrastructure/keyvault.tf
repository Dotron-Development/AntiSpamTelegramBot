resource "azurerm_key_vault" "kv" {
  name                        = "${local.kv_name}-${var.environment_prefix}"
  location                    = var.location
  resource_group_name         = azurerm_resource_group.rg.name
  enabled_for_disk_encryption = false
  tenant_id                   = data.azurerm_client_config.current.tenant_id
  soft_delete_retention_days  = 7
  purge_protection_enabled    = false
  sku_name                    = "standard"
  enable_rbac_authorization   = true


  ## network rules
  public_network_access_enabled = false

  tags = local.tags
}

resource "azurerm_private_dns_zone" "private_dns" {
  name                = "${local.kv_name}-${var.environment_prefix}.privatelink.vaultcore.azure.net"
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "keyvault_vnet_link" {
  name                  = "vnl-${local.kv_name}-${var.environment_prefix}"
  virtual_network_id    = azurerm_virtual_network.vnet.id
  private_dns_zone_name = azurerm_private_dns_zone.private_dns.name
  resource_group_name   = azurerm_resource_group.rg.name
  tags                  = local.tags
}

resource "azurerm_private_endpoint" "kv_pe" {
  name                          = "pe-${local.kv_name}-${var.environment_prefix}"
  subnet_id                     = azurerm_subnet.subnet2_kv.id
  location                      = var.location
  resource_group_name           = azurerm_resource_group.rg.name
  custom_network_interface_name = "nic-${local.kv_name}-pe-${var.environment_prefix}"

  private_service_connection {
    name                           = "sc-${local.kv_name}-${var.environment_prefix}"
    private_connection_resource_id = azurerm_key_vault.kv.id
    is_manual_connection           = false
    subresource_names              = ["vault"]
  }

  ip_configuration {
    name               = "ip-${local.kv_name}-${var.environment_prefix}"
    private_ip_address = "10.0.2.33"
    subresource_name   = "vault"
    member_name        = "default"
  }

  tags = local.tags
}

resource "azurerm_private_dns_a_record" "keyvault_a_record" {
  name                = "@"
  zone_name           = azurerm_private_dns_zone.private_dns.name
  resource_group_name = azurerm_resource_group.rg.name
  ttl                 = 300
  records = [
    azurerm_private_endpoint.kv_pe.ip_configuration[0].private_ip_address,
    azurerm_private_endpoint.kv_runner_pe.private_service_connection.private_ip_address
  ]

  tags = local.tags
}
