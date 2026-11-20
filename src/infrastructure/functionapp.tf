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
  https_only          = true

  storage_container_type                         = "blobContainer"
  storage_container_endpoint                     = "${azurerm_storage_account.function_storage.primary_blob_endpoint}${azurerm_storage_container.function_storage_container.name}"
  storage_authentication_type                    = "UserAssignedIdentity"
  storage_user_assigned_identity_id              = azurerm_user_assigned_identity.functionapp_identity.id
  runtime_name                                   = "dotnet-isolated"
  runtime_version                                = "10.0"
  instance_memory_in_mb                          = 512
  maximum_instance_count                         = 1
  virtual_network_subnet_id                      = azurerm_subnet.subnet1_functions.id
  webdeploy_publish_basic_authentication_enabled = false

  site_config {
    application_insights_connection_string = azurerm_application_insights.appinsights.connection_string
    application_insights_key               = azurerm_application_insights.appinsights.instrumentation_key

    cors {
      # AzureRM suppresses CORS when distinct() makes the entire set unknown during SPA creation.
      allowed_origins     = concat([local.review_static_web_app_origin], var.review_frontend_origins)
      support_credentials = false
    }
  }

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.functionapp_identity.id]
  }

  # Use configured names so secret rotation cannot change a reference during apply.
  app_settings = merge({
    "AzureWebJobsStorage"              = "" //workaround until https://github.com/hashicorp/terraform-provider-azurerm/pull/29099 gets released
    "AzureWebJobsStorage__accountName" = azurerm_storage_account.function_storage.name
    "AzureWebJobsStorage__clientId"    = azurerm_user_assigned_identity.functionapp_identity.client_id

    "SearchQueueStorage__queueServiceUri" = azurerm_storage_account.data_storage.primary_queue_endpoint
    "SearchQueueStorage__credential"      = "managedidentity"
    "SearchQueueStorage__clientId"        = azurerm_user_assigned_identity.functionapp_identity.client_id

    "SnapshotStorageConfiguration__ServiceUrl"       = azurerm_storage_account.data_storage.primary_blob_endpoint
    "SnapshotStorageConfiguration__ContainerName"    = azurerm_storage_container.search_snapshots.name
    "SnapshotStorageConfiguration__IdentityClientId" = azurerm_user_assigned_identity.functionapp_identity.client_id

    "OpenAiServicesConfiguration__ImageRecognitionDeployment" = azurerm_cognitive_deployment.image_text_extraction.name
    "OpenAiServicesConfiguration__SpamRecognitionDeployment"  = azurerm_cognitive_deployment.spam_recognition.name
    "OpenAiServicesConfiguration__MentionDeployment"          = azurerm_cognitive_deployment.mention.name
    "OpenAiServicesConfiguration__SearchEmbeddingDeployment"  = azurerm_cognitive_deployment.search_embeddings.name
    "OpenAiServicesConfiguration__ServiceUrl"                 = azurerm_cognitive_account.ai_services.endpoint
    "OpenAiServicesConfiguration__ApiKey"                     = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.kv.name};SecretName=${azurerm_key_vault_secret.ai_services_api_key.name})"

    "AzureTablesConfiguration__StorageAccountUrl"     = azurerm_storage_account.data_storage.primary_table_endpoint
    "AzureTablesConfiguration__TableIdentityClientId" = azurerm_user_assigned_identity.functionapp_identity.client_id

    "ReviewTelegram__ModeratorIds" = var.review_telegram_moderator_ids

    "TelegramBotConfiguration__ForwardSpamToChatId"    = var.forwardSpamToChatId,
    "TelegramBotConfiguration__BotName"                = var.bot_name,
    "TelegramBotConfiguration__SpamRestrictionTimeout" = var.spam_restriction_timeout
    "TelegramBotConfiguration__CommandCooldown"        = var.command_cooldown
    "TelegramBotConfiguration__SecretHeader"           = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.kv.name};SecretName=${azurerm_key_vault_secret.telegram_bot_secret_header.name})"
    "TelegramBotConfiguration__Token"                  = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.kv.name};SecretName=${azurerm_key_vault_secret.telegram_bot_token.name})"
    }, var.debug_ai_response != null ? {
    "TelegramBotConfiguration__DebugAiResponse" = var.debug_ai_response
  } : {})

  tags = local.tags

  depends_on = [
    azurerm_storage_queue.search_index,
    azurerm_storage_queue.reset_snapshot,
    azurerm_role_assignment.functionapp_identity_search_snapshots,
    azurerm_private_endpoint.data_storage_blob,
    azurerm_private_dns_zone_virtual_network_link.data_storage_blob,
    azurerm_role_assignment.functionapp_identity_data_storage_queue,
    azurerm_private_endpoint.data_storage_queue,
    azurerm_private_dns_zone_virtual_network_link.data_storage_queue,
  ]

  lifecycle {
    ignore_changes = [tags["hidden-link: /app-insights-resource-id"]]
  }
}


