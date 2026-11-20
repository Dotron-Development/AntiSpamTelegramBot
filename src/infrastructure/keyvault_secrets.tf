resource "time_sleep" "kv_dns_propagation" {
  count           = var.disable_public_access && var.enable_runner_endpoints ? 1 : 0
  create_duration = "300s"

  triggers = {
    record_id = azurerm_private_dns_a_record.keyvault_runner[0].id
    address   = one(azurerm_private_dns_a_record.keyvault_runner[0].records)
  }
}

resource "azurerm_key_vault_secret" "telegram_bot_token" {
  name         = "telegram-bot-token"
  value        = var.telegram_bot_token
  key_vault_id = azurerm_key_vault.kv.id

  depends_on = [azurerm_role_assignment.runner_kv_administrator]
}

resource "azurerm_key_vault_secret" "telegram_bot_secret_header" {
  name         = "telegram-bot-secret-header"
  value        = var.telegram_bot_secret_header
  key_vault_id = azurerm_key_vault.kv.id

  depends_on = [azurerm_role_assignment.runner_kv_administrator]
}

resource "azurerm_key_vault_secret" "ai_services_api_key" {
  name         = "ai-services-api-key"
  value        = azurerm_cognitive_account.ai_services.primary_access_key
  key_vault_id = azurerm_key_vault.kv.id

  depends_on = [azurerm_role_assignment.runner_kv_administrator]
}
