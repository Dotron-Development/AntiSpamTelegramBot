# access from github runner subnet to data storage (required for null_resource table provisioning when disable_public_access = true)

resource "azurerm_private_endpoint" "data_storage_runner_pe" {
  count                         = var.disable_public_access ? 1 : 0
  name                          = "pe-runner-data-storage-${var.environment_prefix}"
  subnet_id                     = data.azurerm_subnet.github_runner_vnet_subnet.id
  location                      = var.location
  resource_group_name           = azurerm_resource_group.rg.name
  custom_network_interface_name = "nic-pe-runner-data-storage-${var.environment_prefix}"

  private_service_connection {
    name                           = "sc-runner-data-storage-${var.environment_prefix}"
    private_connection_resource_id = azurerm_storage_account.data_storage.id
    is_manual_connection           = false
    subresource_names              = ["table"]
  }

  tags = local.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "data_storage_runner_vnet_link" {
  count                 = var.disable_public_access ? 1 : 0
  name                  = "vnl-runner-data-storage-${var.environment_prefix}"
  virtual_network_id    = data.azurerm_virtual_network.github_runner_vnet.id
  private_dns_zone_name = azurerm_private_dns_zone.data_storage_private_dns[0].name
  resource_group_name   = azurerm_resource_group.rg.name

  tags = local.tags
}
