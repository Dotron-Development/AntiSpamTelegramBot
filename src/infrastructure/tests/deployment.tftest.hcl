mock_provider "azurerm" {
  mock_data "azurerm_client_config" {
    defaults = {
      tenant_id       = "11111111-1111-1111-1111-111111111111"
      subscription_id = "22222222-2222-2222-2222-222222222222"
    }
  }

  mock_data "azurerm_virtual_network" {
    defaults = {
      id = "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-runners/providers/Microsoft.Network/virtualNetworks/vnet-runners"
    }
  }

  mock_data "azurerm_subnet" {
    defaults = {
      id = "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-runners/providers/Microsoft.Network/virtualNetworks/vnet-runners/subnets/runner"
    }
  }
}

mock_provider "time" {}
mock_provider "null" {}

variables {
  keyvault_administrator_object_id = "33333333-3333-3333-3333-333333333333"
  telegram_bot_token               = "test-token"
  telegram_bot_secret_header       = "test-header"
  review_user_object_ids           = ["44444444-4444-4444-4444-444444444444"]
}

run "planning_with_unknown_spa_origin" {
  command = plan

  assert {
    condition     = contains(azurerm_function_app_flex_consumption.function_app.site_config[0].cors[0].allowed_origins, "http://localhost:5173")
    error_message = "Known additional CORS origins must remain visible when the SPA hostname is unknown."
  }

  assert {
    condition     = azurerm_monitor_scheduled_query_rules_alert_v2.function_failures.evaluation_frequency == "PT5M" && azurerm_monitor_scheduled_query_rules_alert_v2.function_failures.window_duration == "PT5M"
    error_message = "The failure query must use a supported five-minute frequency and window."
  }
}

run "planning_versionless_key_vault_references" {
  command = plan

  assert {
    condition = alltrue([
      for setting, secret in {
        OpenAiServicesConfiguration__ApiKey    = "ai-services-api-key"
        TelegramBotConfiguration__SecretHeader = "telegram-bot-secret-header"
        TelegramBotConfiguration__Token        = "telegram-bot-token"
      } : azurerm_function_app_flex_consumption.function_app.app_settings[setting] == "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.kv.name};SecretName=${secret})"
    ])
    error_message = "Key Vault references must be versionless and known at plan time, even if the vault URI and secret versions are unknown."
  }
}

run "planning_without_additional_origins" {
  command = plan

  variables {
    review_frontend_origins = []
  }

  assert {
    condition     = length(azurerm_function_app_flex_consumption.function_app.site_config[0].cors) == 1 && !azurerm_function_app_flex_consumption.function_app.site_config[0].cors[0].support_credentials
    error_message = "CORS must remain configured without allowing cookie credentials when no additional origins are supplied."
  }
}
