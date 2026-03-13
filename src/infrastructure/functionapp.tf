locals {
  function_app_name = "fn-${local.appName}-${var.environment_prefix}"
}
resource "azurerm_storage_account" "function_storage" {
  name                      = "satgbotfnapp${var.environment_prefix}"
  resource_group_name       = azurerm_resource_group.rg.name
  location                  = var.location
  account_tier              = "Standard"
  account_replication_type  = "LRS"
  shared_access_key_enabled = false
  tags                      = local.tags
}

resource "azurerm_storage_container" "function_storage_container" {
  name                  = "${local.function_app_name}-flexcontainer"
  storage_account_id    = azurerm_storage_account.function_storage.id
  container_access_type = "private"
}

resource "azurerm_service_plan" "function_sp" {
  name                = "sp-${local.appName}-fn-${var.environment_prefix}"
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name

  os_type  = "Linux"
  sku_name = "FC1"

  tags = local.tags
}

resource "azurerm_function_app_flex_consumption" "function_app" {
  name                = local.function_app_name
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name
  service_plan_id     = azurerm_service_plan.function_sp.id

  storage_container_type                         = "blobContainer"
  storage_container_endpoint                     = "${azurerm_storage_account.function_storage.primary_blob_endpoint}${azurerm_storage_container.function_storage_container.name}"
  storage_authentication_type                    = "UserAssignedIdentity"
  storage_user_assigned_identity_id              = azurerm_user_assigned_identity.functionapp_identity.id
  runtime_name                                   = "dotnet-isolated"
  runtime_version                                = "10.0"
  instance_memory_in_mb                          = 2048
  virtual_network_subnet_id                      = azurerm_subnet.subnet1_functions.id
  webdeploy_publish_basic_authentication_enabled = false

  site_config {
    application_insights_connection_string = azurerm_application_insights.appinsights.connection_string
    application_insights_key               = azurerm_application_insights.appinsights.instrumentation_key
  }

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.functionapp_identity.id]
  }

  app_settings = merge({
    "AzureWebJobsStorage"              = "" //workaround until https://github.com/hashicorp/terraform-provider-azurerm/pull/29099 gets released
    "AzureWebJobsStorage__accountName" = azurerm_storage_account.function_storage.name
    "AzureWebJobsStorage__clientId"    = azurerm_user_assigned_identity.functionapp_identity.client_id

    "OpenAiServicesConfiguration__ImageRecognitionDeployment" = azurerm_cognitive_deployment.image_text_extraction.name
    "OpenAiServicesConfiguration__SpamRecognitionDeployment"  = azurerm_cognitive_deployment.spam_recognition.name
    "OpenAiServicesConfiguration__ServiceUrl"                 = azurerm_ai_services.ai_services.endpoint
    "OpenAiServicesConfiguration__ApiKey"                     = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.ai_services_api_key.id})"

    "AzureTablesConfiguration__StorageAccountUrl"     = azurerm_storage_account.data_storage.primary_table_endpoint
    "AzureTablesConfiguration__TableIdentityClientId" = azurerm_user_assigned_identity.functionapp_identity.client_id

    "TelegramBotConfiguration__ForwardSpamToChatId" = var.forwardSpamToChatId,
    "TelegramBotConfiguration__BotName"             = var.bot_name,
    "TelegramBotConfiguration__SecretHeader"        = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.telegram_bot_secret_header.id})"
    "TelegramBotConfiguration__Token"               = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.telegram_bot_token.id})"
    }, var.debug_ai_response != null ? {
    "TelegramBotConfiguration__DebugAiResponse" = var.debug_ai_response
  } : {})

  tags = local.tags
}


