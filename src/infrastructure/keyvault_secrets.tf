resource "time_sleep" "kv_dns_propagation" {
  count           = var.disable_public_access ? 1 : 0
  create_duration = "300s"

  depends_on = [
    azurerm_private_endpoint.kv_runner_pe,
    azurerm_private_dns_a_record.keyvault_a_record,
    azurerm_private_dns_zone_virtual_network_link.keyvault_runner_vnet_link,
  ]
}

resource "azurerm_key_vault_secret" "telegram_bot_token" {
  name         = "telegram-bot-token"
  value        = var.telegram_bot_token
  key_vault_id = azurerm_key_vault.kv.id

  depends_on = [time_sleep.kv_dns_propagation]
}

resource "azurerm_key_vault_secret" "telegram_bot_secret_header" {
  name         = "telegram-bot-secret-header"
  value        = var.telegram_bot_secret_header
  key_vault_id = azurerm_key_vault.kv.id

  depends_on = [time_sleep.kv_dns_propagation]
}

resource "azurerm_key_vault_secret" "ai_services_api_key" {
  name         = "ai-services-api-key"
  value        = azurerm_ai_services.ai_services.primary_access_key
  key_vault_id = azurerm_key_vault.kv.id

  depends_on = [time_sleep.kv_dns_propagation]
}
