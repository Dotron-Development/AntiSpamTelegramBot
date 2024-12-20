resource "azurerm_virtual_network" "vnet" {
  name                = "vnet-${var.environment_prefix}"
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name
  address_space       = ["10.0.0.0/16"]
  dns_servers         = ["10.0.0.4", "10.0.0.5"]

  subnet {
    name           = "vmrunners-${var.environment_prefix}"
    address_prefix = "10.0.1.0/24"
  }

  subnet {
    name           = "subnet1-${var.environment_prefix}"
    address_prefix = "10.0.2.0/24"
  }

  tags = local.tags
}
