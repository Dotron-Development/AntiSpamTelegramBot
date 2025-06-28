resource "azurerm_storage_account" "main_storage" {
  name                     = "satgbotdata${var.environment_prefix}"
  resource_group_name      = azurerm_resource_group.rg.name
  location                 = var.location
  account_tier             = "Standard"
  account_replication_type = "GRS"
  tags                     = local.tags
}

resource "azurerm_storage_table" "spam_stats" {
  name                 = "SpamStats"
  storage_account_name = azurerm_storage_account.main_storage.name
}

resource "azurerm_storage_table" "spam_history" {
  name                 = "SpamHistory"
  storage_account_name = azurerm_storage_account.main_storage.name
}

resource "azurerm_storage_table" "spam_hash" {
  name                 = "SpamHash"
  storage_account_name = azurerm_storage_account.main_storage.name
}

resource "azurerm_storage_table" "message_count" {
  name                 = "MessageCount"
  storage_account_name = azurerm_storage_account.main_storage.name
}
