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
  address_prefixes     = ["10.0.1.0/28"] # 16 IPs, 11 usable. range - 10.0.1.0 - 10.0.1.15

  delegation {
    name = "delegation"

    service_delegation {
      name = "Microsoft.App/environments"
    }
  }

  service_endpoints = ["Microsoft.KeyVault", "Microsoft.Storage"]

  depends_on = [azurerm_resource_provider_registration.microsoft_app]
}

resource "azurerm_subnet" "subnet2_kv" {
  count                = var.disable_public_access ? 1 : 0
  name                 = "subnet2-kv-${var.environment_prefix}"
  resource_group_name  = azurerm_resource_group.rg.name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefixes     = ["10.0.1.16/28"] # 16 IPs, 11 usable. range - range - 10.0.1.16 - 10.0.1.31
}
