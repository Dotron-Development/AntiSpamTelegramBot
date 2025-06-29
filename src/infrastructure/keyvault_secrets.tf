resource "azurerm_key_vault_secret" "telegram_bot_token" {
  name         = "telegram-bot-token"
  value        = var.telegram_bot_token
  key_vault_id = azurerm_key_vault.kv.id

  # to ensure that the private DNS zone is created before the secret
  # and the runner can access the key vault
  depends_on = [azurerm_private_dns_zone_virtual_network_link.keyvault_vnet_link]
}

resource "azurerm_key_vault_secret" "telegram_bot_secret_header" {
  name         = "telegram-bot-secret-header"
  value        = var.telegram_bot_secret_header
  key_vault_id = azurerm_key_vault.kv.id

  # to ensure that the private DNS zone is created before the secret
  # and the runner can access the key vault
  depends_on = [azurerm_private_dns_zone_virtual_network_link.keyvault_vnet_link]
}
