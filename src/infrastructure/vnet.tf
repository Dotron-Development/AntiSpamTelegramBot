resource "azurerm_virtual_network" "vnet" {
  name                = "vnet-${var.environment_prefix}"
  address_space       = ["10.0.0.0/16"]
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name
}

resource "azurerm_subnet" "vnet_for_vm" {
  name                 = "vnet-vm-runners-${var.environment_prefix}"
  resource_group_name  = var.location
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefixes     = ["10.0.1.0/24"]
}
