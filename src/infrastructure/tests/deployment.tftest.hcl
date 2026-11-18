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

run "planning_search_foundation" {
  command = plan

  variables {
    disable_public_access = true
  }

  override_resource {
    target          = azurerm_storage_account.data_storage
    override_during = plan
    values = {
      id                     = "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-test/providers/Microsoft.Storage/storageAccounts/satgbotdatatest"
      primary_queue_endpoint = "https://satgbotdatatest.queue.core.windows.net/"
    }
  }

  assert {
    condition = (
      alltrue([for queue in azurerm_storage_queue.search_index : queue.storage_account_id == azurerm_storage_account.data_storage.id]) &&
      azurerm_function_app_flex_consumption.function_app.app_settings["SearchQueueStorage__queueServiceUri"] == azurerm_storage_account.data_storage.primary_queue_endpoint &&
      azurerm_function_app_flex_consumption.function_app.app_settings["AzureWebJobsStorage__accountName"] == azurerm_storage_account.function_storage.name &&
      azurerm_role_assignment.functionapp_identity_data_storage_queue.scope == azurerm_storage_account.data_storage.id &&
      azurerm_role_assignment.functionapp_identity_data_storage_queue.role_definition_name == "Storage Queue Data Contributor"
    )
    error_message = "Search queues and their managed identity access must use data storage without moving the Functions host storage."
  }

  assert {
    condition = (
      one(azurerm_private_endpoint.data_storage_queue).private_service_connection[0].private_connection_resource_id == azurerm_storage_account.data_storage.id &&
      toset(one(azurerm_private_endpoint.data_storage_queue).private_service_connection[0].subresource_names) == toset(["queue"]) &&
      one(azurerm_private_dns_zone.data_storage_queue).name == "privatelink.queue.core.windows.net" &&
      length(azurerm_private_dns_zone_virtual_network_link.data_storage_queue) == 1
    )
    error_message = "Private data storage must expose the queue service through a private endpoint and a linked queue DNS zone."
  }

  assert {
    condition = (
      toset(keys(azurerm_storage_queue.reset_snapshot)) == toset(["reset-snapshot", "reset-snapshot-poison"]) &&
      alltrue([for queue in azurerm_storage_queue.reset_snapshot : queue.storage_account_id == azurerm_storage_account.data_storage.id]) &&
      azurerm_storage_container.search_snapshots.storage_account_id == azurerm_storage_account.data_storage.id &&
      azurerm_storage_container.search_snapshots.container_access_type == "private" &&
      azurerm_function_app_flex_consumption.function_app.app_settings["SnapshotStorageConfiguration__ContainerName"] == "search-snapshots" &&
      azurerm_role_assignment.functionapp_identity_search_snapshots.role_definition_name == "Storage Blob Data Contributor" &&
      toset(one(azurerm_private_endpoint.data_storage_blob).private_service_connection[0].subresource_names) == toset(["blob"]) &&
      one(azurerm_private_dns_zone.data_storage_blob).name == "privatelink.blob.core.windows.net"
    )
    error_message = "Snapshot resets require private queue and blob access through the data storage account."
  }

  assert {
    condition = (
      contains(keys(null_resource.data_storage_tables), "Vectors") &&
      toset(keys(azurerm_storage_queue.search_index)) == toset(["search-index", "search-index-poison"]) &&
      azurerm_function_app_flex_consumption.function_app.app_settings["SearchQueueStorage__credential"] == "managedidentity" &&
      azurerm_function_app_flex_consumption.function_app.app_settings["OpenAiServicesConfiguration__SearchEmbeddingDeployment"] == azurerm_cognitive_deployment.search_embeddings.name &&
      azurerm_cognitive_deployment.search_embeddings.model[0].name == "text-embedding-3-large"
    )
    error_message = "Search must provision the Vectors table and use its embedding deployment via the shared OpenAI configuration."
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
