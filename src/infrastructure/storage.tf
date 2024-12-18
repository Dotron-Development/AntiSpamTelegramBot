resource "azurerm_storage_account" "main_storage" {
  name                     = "saaiassistantdata${var.environment_prefix}"
  resource_group_name      = azurerm_resource_group.rg.name
  location                 = var.location
  account_tier             = "Standard"
  account_replication_type = "GRS"
  tags                     = local.tags
}
