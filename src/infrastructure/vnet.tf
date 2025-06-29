resource "azurerm_virtual_network" "vnet" {
  name                = "vnet-${local.appName}-${var.environment_prefix}"
  address_space       = ["10.0.0.0/16"]
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name

  tags = local.tags
}

# Create a subnet for the function apps
resource "azurerm_subnet" "subnet1_functions" {
  name                 = "subnet1-function-${var.environment_prefix}"
  resource_group_name  = azurerm_resource_group.rg.name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefixes     = ["10.0.1.0/26"]

  delegation {
    name = "delegation"

    service_delegation {
      name = "Microsoft.App/environments"
    }
  }
}

resource "azurerm_subnet" "subnet2_kv" {
  name                 = "subnet2-kv-${var.environment_prefix}"
  resource_group_name  = azurerm_resource_group.rg.name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefixes     = ["10.0.2.0/26"]
}
