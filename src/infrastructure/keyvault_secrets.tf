resource "azurerm_key_vault_secret" "telegram_bot_token" {
  name         = "telegram-bot-token"
  value        = var.telegram_bot_token
  key_vault_id = azurerm_key_vault.kv.id
}

resource "azurerm_key_vault_secret" "telegram_bot_secret_header" {
  name         = "telegram-bot-secret-header"
  value        = var.telegram_bot_secret_header
  key_vault_id = azurerm_key_vault.kv.id
}
