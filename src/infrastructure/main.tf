data "azurerm_client_config" "current" {}

data "terraform_remote_state" "openai_data" {
  backend = "azurerm"
  config = {
    resource_group_name  = var.openai_state_resource_group_name
    storage_account_name = var.openai_state_storage_account_name
    container_name       = var.openai_state_container_name
    key                  = var.openai_state_key
  }
}

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
