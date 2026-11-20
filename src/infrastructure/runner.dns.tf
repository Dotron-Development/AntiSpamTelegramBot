data "azurerm_private_dns_zone" "runner" {
  for_each = var.disable_public_access && var.enable_runner_endpoints ? toset([
    "privatelink.vaultcore.azure.net",
    "privatelink.table.core.windows.net",
  ]) : toset([])

  name                = each.key
  resource_group_name = var.github_runners_vnet_resource_group
}

data "azurerm_private_dns_zone_virtual_network_link" "runner" {
  for_each = data.azurerm_private_dns_zone.runner

  name                  = "vnl-${var.github_runners_vnet_name}"
  private_dns_zone_name = each.value.name
  resource_group_name   = var.github_runners_vnet_resource_group

  lifecycle {
    postcondition {
      condition = (
        lower(self.virtual_network_id) == lower(data.azurerm_virtual_network.github_runner_vnet.id) &&
        self.resolution_policy == "NxDomainRedirect" &&
        !self.registration_enabled
      )
      error_message = "The shared ${each.key} zone needs a resolution-only link to the GitHub runner VNet with NxDomainRedirect enabled."
    }
  }
}
