resource "azurerm_private_dns_zone" "data_storage_private_dns" {
  count               = var.disable_public_access ? 1 : 0
  name                = "privatelink.table.core.windows.net"
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "data_storage_vnet_link" {
  count                 = var.disable_public_access ? 1 : 0
  name                  = "vnl-data-storage-${var.environment_prefix}"
  virtual_network_id    = azurerm_virtual_network.vnet.id
  private_dns_zone_name = azurerm_private_dns_zone.data_storage_private_dns[0].name
  resource_group_name   = azurerm_resource_group.rg.name
  tags                  = local.tags
}

resource "azurerm_private_endpoint" "data_storage_pe" {
  count                         = var.disable_public_access ? 1 : 0
  name                          = "pe-data-storage-${var.environment_prefix}"
  subnet_id                     = azurerm_subnet.subnet2_private_endpoints[0].id
  location                      = var.location
  resource_group_name           = azurerm_resource_group.rg.name
  custom_network_interface_name = "nic-data-storage-pe-${var.environment_prefix}"

  private_service_connection {
    name                           = "sc-data-storage-${var.environment_prefix}"
    private_connection_resource_id = azurerm_storage_account.data_storage.id
    is_manual_connection           = false
    subresource_names              = ["table"]
  }

  ip_configuration {
    name               = "ip-data-storage-${var.environment_prefix}"
    private_ip_address = "${var.vnet_address_prefix}.2.34"
    subresource_name   = "table"
    member_name        = "table"
  }

  tags = local.tags
}

resource "azurerm_private_dns_a_record" "data_storage_a_record" {
  count               = var.disable_public_access ? 1 : 0
  name                = azurerm_storage_account.data_storage.name
  zone_name           = azurerm_private_dns_zone.data_storage_private_dns[0].name
  resource_group_name = azurerm_resource_group.rg.name
  ttl                 = 300
  records = [
    azurerm_private_endpoint.data_storage_pe[0].ip_configuration[0].private_ip_address,
    azurerm_private_endpoint.data_storage_runner_pe[0].private_service_connection[0].private_ip_address
  ]

  tags = local.tags
}
