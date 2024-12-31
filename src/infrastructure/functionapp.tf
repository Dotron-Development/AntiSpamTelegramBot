resource "azurerm_storage_account" "function_storage" {
  name                     = "satgbotfnapp${var.environment_prefix}"
  resource_group_name      = data.terraform_remote_state.openai_data.outputs.resource_group_name
  location                 = var.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  tags                     = local.tags
}

resource "azurerm_service_plan" "function_sp" {
  name                = "sp-${local.appName}-fn-${var.environment_prefix}"
  location            = var.location
  resource_group_name = data.terraform_remote_state.openai_data.outputs.resource_group_name

  os_type  = "Linux"
  sku_name = "Y1"

  tags = local.tags
}

resource "azurerm_linux_function_app" "function_app" {
  name                            = "fn-${local.appName}-${var.environment_prefix}"
  location                        = var.location
  resource_group_name             = data.terraform_remote_state.openai_data.outputs.resource_group_name
  service_plan_id                 = azurerm_service_plan.function_sp.id
  storage_account_name            = azurerm_storage_account.function_storage.name
  storage_account_access_key      = azurerm_storage_account.function_storage.primary_access_key
  key_vault_reference_identity_id = azurerm_user_assigned_identity.functionapp_identity.id

  site_config {
    always_on = false

    application_stack {
      dotnet_version              = "9.0"
      use_dotnet_isolated_runtime = true
    }
  }

  app_settings = {
    "AzureWebJobsStorage"                 = azurerm_storage_account.function_storage.primary_connection_string
    "FUNCTIONS_EXTENSION_VERSION"         = "~4"
    "WEBSITE_ENABLE_SYNC_UPDATE_SITE"     = "true"
    "WEBSITES_ENABLE_APP_SERVICE_STORAGE" = "false"
    "WEBSITE_RUN_FROM_PACKAGE"            = "1"
    "FUNCTIONS_WORKER_RUNTIME"            = "dotnet-isolated"

    "APPINSIGHTS_INSTRUMENTATIONKEY" = azurerm_application_insights.appinsights.instrumentation_key

    "OpenAiServicesConfiguration__ImageRecognitionDeployment" = module.global_constants.image_text_extraction_model_name
    "OpenAiServicesConfiguration__SpamRecognitionDeployment"  = module.global_constants.spam_recognition_model_name
    "OpenAiServicesConfiguration__ServiceUrl"                 = data.terraform_remote_state.openai_data.outputs.openai_service_url

    "AzureTablesConfiguration__StorageAccountUrl" = azurerm_storage_account.main_storage.primary_web_endpoint

    "TelegramBotConfiguration__DebugAiResponse"     = "false"
    "TelegramBotConfiguration__ForwardSpamToChatId" = "-1002395980780"
    "TelegramBotConfiguration__SecretHeader"        = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.telegram_bot_secret_header.id})"
    "TelegramBotConfiguration__Token"               = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.telegram_bot_token.id})"
  }


  identity {
    type = "UserAssigned"
    identity_ids = [
      azurerm_user_assigned_identity.functionapp_identity.id
    ]
  }

  tags = local.tags
}
