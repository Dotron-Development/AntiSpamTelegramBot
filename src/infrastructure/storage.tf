resource "azurerm_storage_account" "data_storage" {
  name                          = "satgbotdata${var.environment_prefix}"
  resource_group_name           = azurerm_resource_group.rg.name
  location                      = var.location
  account_tier                  = "Standard"
  account_replication_type      = "GRS"
  min_tls_version               = "TLS1_2"
  shared_access_key_enabled     = false
  public_network_access_enabled = !var.disable_public_access
  tags                          = local.tags

  network_rules {
    default_action             = "Deny"
    virtual_network_subnet_ids = !var.disable_public_access ? [azurerm_subnet.subnet1_functions.id] : []
  }
}

resource "azurerm_storage_table" "spam_stats" {
  name                 = "SpamStats"
  storage_account_name = azurerm_storage_account.data_storage.name
}

resource "azurerm_storage_table" "spam_history" {
  name                 = "SpamHistory"
  storage_account_name = azurerm_storage_account.data_storage.name
}

resource "azurerm_storage_table" "spam_hash" {
  name                 = "SpamHash"
  storage_account_name = azurerm_storage_account.data_storage.name
}

resource "azurerm_storage_table" "message_count" {
  name                 = "MessageCount"
  storage_account_name = azurerm_storage_account.data_storage.name
}
