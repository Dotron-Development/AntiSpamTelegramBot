resource "azurerm_storage_account" "main_storage" {
  name                     = "satgbotdata${var.environment_prefix}"
  resource_group_name      = azurerm_resource_group.rg.name
  location                 = var.location
  account_tier             = "Standard"
  account_replication_type = "GRS"
  tags                     = local.tags
}

resource "azurerm_storage_table" "spam_stats" {
  name                 = "spam_stats"
  storage_account_name = azurerm_storage_account.main_storage.name
}

resource "azurerm_storage_table" "spam_history" {
  name                 = "spam_history"
  storage_account_name = azurerm_storage_account.main_storage.name
}

resource "azurerm_storage_table" "spam_hash" {
  name                 = "spam_hash"
  storage_account_name = azurerm_storage_account.main_storage.name
}

resource "azurerm_storage_table" "message_count" {
  name                 = "message_count"
  storage_account_name = azurerm_storage_account.main_storage.name
}
