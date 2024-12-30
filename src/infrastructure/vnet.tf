resource "azurerm_virtual_network" "vnet" {
  name                = "vnet-${local.appName}-${var.environment_prefix}"
  address_space       = ["10.0.0.0/16"]
  location            = var.location
  resource_group_name = terraform_remote_state.openai_data.resource_group_name
}

resource "azurerm_subnet" "subnet_for_vm" {
  name                 = "subnet-for-vm-runners-${var.environment_prefix}"
  resource_group_name  = terraform_remote_state.openai_data.resource_group_name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefixes     = ["10.0.1.0/24"]
}
