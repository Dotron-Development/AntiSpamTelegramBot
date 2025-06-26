data "azurerm_client_config" "current" {}

module "global_constants" {
  source = "./global_constants"
}

data "azurerm_virtual_network" "github_runner_vnet" {
  name                = var.github_runners_vnet_name
  resource_group_name = var.github_runners_vnet_resource_group
}

data "azurerm_subnet" "github_runner_vnet_subnet" {
  name                 = var.github_runners_vnet_subnet_name
  virtual_network_name = var.github_runners_vnet_name
  resource_group_name  = var.github_runners_vnet_resource_group
}
