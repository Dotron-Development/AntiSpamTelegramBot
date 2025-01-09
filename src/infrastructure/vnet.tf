resource "azurerm_virtual_network" "vnet" {
  name                = "vnet-${local.appName}-${var.environment_prefix}"
  address_space       = ["10.0.0.0/16"]
  location            = var.location
  resource_group_name = data.terraform_remote_state.openai_data.outputs.resource_group_name

  tags = local.tags
}

resource "azurerm_subnet" "subnet1" {
  name                 = "subnet1-${var.environment_prefix}"
  resource_group_name  = data.terraform_remote_state.openai_data.outputs.resource_group_name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefixes     = ["10.0.1.0/24"]

  # if public access is enabled, we need to create a service endpoint
  # if public access is disabled then connectivity will be done through the private endpoint
  service_endpoints = !var.disable_public_access ? ["Microsoft.KeyVault"] : []
}
