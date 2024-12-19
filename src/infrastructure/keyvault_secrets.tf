resource "azurerm_key_vault_secret" "telegram_bot_token" {
  name         = "telegram-bot-token"
  value        = "szechuan"
  key_vault_id = azurerm_key_vault.kv.id
}
