# access from github runner subnet to key vault

resource "azurerm_private_endpoint" "kv_runner_pe" {
  name                          = "pe-runner-${local.kv_name}-${var.environment_prefix}"
  subnet_id                     = data.azurerm_subnet.github_runner_vnet_subnet.id
  location                      = var.location
  resource_group_name           = data.terraform_remote_state.openai_data.outputs.resource_group_name
  custom_network_interface_name = "nic-pe-runner-${local.kv_name}-${var.environment_prefix}"

  private_service_connection {
    name                           = "sc-runner-${local.kv_name}-${var.environment_prefix}"
    private_connection_resource_id = azurerm_key_vault.kv.id
    is_manual_connection           = false
    subresource_names              = ["vault"]
  }

  tags = local.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "keyvault_runner_vnet_link" {
  name                  = "vnl-runner-${local.kv_name}-${var.environment_prefix}"
  virtual_network_id    = data.azurerm_virtual_network.github_runner_vnet.id
  private_dns_zone_name = azurerm_private_dns_zone.private_dns.name
  resource_group_name   = data.terraform_remote_state.openai_data.outputs.resource_group_name

  tags = local.tags
}
