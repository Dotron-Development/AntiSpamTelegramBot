resource "azurerm_resource_group" "rg" {
  name     = "rg-${local.appName}-${var.environment_prefix}"
  location = var.location
  tags     = local.tags
}
