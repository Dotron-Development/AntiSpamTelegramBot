# access from github runner subnet to key vault

resource "azurerm_private_endpoint" "kv_runner_pe" {
  count                         = var.disable_public_access && var.enable_runner_endpoints ? 1 : 0
  name                          = "pe-runner-${local.kv_name}-${var.environment_prefix}"
  subnet_id                     = data.azurerm_subnet.github_runner_vnet_subnet.id
  location                      = var.location
  resource_group_name           = azurerm_resource_group.rg.name
  custom_network_interface_name = "nic-pe-runner-${local.kv_name}-${var.environment_prefix}"

  private_service_connection {
    name                           = "sc-runner-${local.kv_name}-${var.environment_prefix}"
    private_connection_resource_id = azurerm_key_vault.kv.id
    is_manual_connection           = false
    subresource_names              = ["vault"]
  }

  tags = local.tags
}

resource "azurerm_private_dns_a_record" "keyvault_runner" {
  count               = var.disable_public_access && var.enable_runner_endpoints ? 1 : 0
  name                = azurerm_key_vault.kv.name
  private_dns_zone_id = data.azurerm_private_dns_zone.runner["privatelink.vaultcore.azure.net"].id
  ttl                 = 300
  records             = [azurerm_private_endpoint.kv_runner_pe[0].private_service_connection[0].private_ip_address]
  tags                = local.tags

  depends_on = [data.azurerm_private_dns_zone_virtual_network_link.runner]
}
