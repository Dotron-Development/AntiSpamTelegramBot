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

  mock_data "azurerm_private_dns_zone_virtual_network_link" {
    defaults = {
      virtual_network_id   = "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-runners/providers/Microsoft.Network/virtualNetworks/vnet-runners"
      registration_enabled = false
      resolution_policy    = "NxDomainRedirect"
    }
  }
}

override_data {
  target          = data.azurerm_private_dns_zone.runner["privatelink.vaultcore.azure.net"]
  override_during = plan
  values = {
    id = "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-runners/providers/Microsoft.Network/privateDnsZones/privatelink.vaultcore.azure.net"
  }
}

override_data {
  target          = data.azurerm_private_dns_zone.runner["privatelink.table.core.windows.net"]
  override_during = plan
  values = {
    id = "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-runners/providers/Microsoft.Network/privateDnsZones/privatelink.table.core.windows.net"
  }
}

mock_provider "time" {}
mock_provider "null" {}

variables {
  keyvault_administrator_object_id = "33333333-3333-3333-3333-333333333333"
  telegram_bot_token               = "test-token"
  telegram_bot_secret_header       = "test-header"
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

run "planning_shared_runner_dns" {
  command = plan

  variables {
    disable_public_access   = true
    enable_runner_endpoints = true
  }

  override_resource {
    target          = azurerm_private_endpoint.kv_runner_pe[0]
    override_during = plan
    values = {
      private_service_connection = { private_ip_address = "10.20.1.4" }
    }
  }

  override_resource {
    target          = azurerm_private_endpoint.data_storage_runner_pe[0]
    override_during = plan
    values = {
      private_service_connection = { private_ip_address = "10.20.1.5" }
    }
  }

  assert {
    condition = (
      one(azurerm_private_dns_a_record.keyvault_runner).private_dns_zone_id == data.azurerm_private_dns_zone.runner["privatelink.vaultcore.azure.net"].id &&
      one(azurerm_private_dns_a_record.data_storage_runner).private_dns_zone_id == data.azurerm_private_dns_zone.runner["privatelink.table.core.windows.net"].id &&
      one(one(azurerm_private_dns_a_record.keyvault_runner).records) == "10.20.1.4" &&
      one(one(azurerm_private_dns_a_record.data_storage_runner).records) == "10.20.1.5" &&
      one(one(azurerm_private_dns_a_record.keyvault_a_record).records) == "${var.vnet_address_prefix}.2.33" &&
      one(one(azurerm_private_dns_a_record.data_storage_a_record).records) == "${var.vnet_address_prefix}.2.34"
    )
    error_message = "Runner DNS must publish only runner endpoint IPs in the shared zones, and Functions DNS must publish only Functions endpoint IPs."
  }
}

run "rejecting_runner_dns_without_public_fallback" {
  command = plan

  variables {
    disable_public_access   = true
    enable_runner_endpoints = true
  }

  override_data {
    target = data.azurerm_private_dns_zone_virtual_network_link.runner["privatelink.vaultcore.azure.net"]
    values = {
      virtual_network_id = "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-runners/providers/Microsoft.Network/virtualNetworks/vnet-runners"
      resolution_policy  = "Default"
    }
  }

  expect_failures = [data.azurerm_private_dns_zone_virtual_network_link.runner["privatelink.vaultcore.azure.net"]]
}

run "planning_runner_dns_cleanup" {
  command = plan

  variables {
    disable_public_access   = true
    enable_runner_endpoints = false
  }

  assert {
    condition = (
      length(azurerm_private_endpoint.kv_runner_pe) == 0 &&
      length(azurerm_private_endpoint.data_storage_runner_pe) == 0 &&
      length(azurerm_private_dns_a_record.keyvault_runner) == 0 &&
      length(azurerm_private_dns_a_record.data_storage_runner) == 0 &&
      length(azurerm_private_dns_zone_virtual_network_link.keyvault_vnet_link) == 1 &&
      length(azurerm_private_dns_zone_virtual_network_link.data_storage_vnet_link) == 1
    )
    error_message = "Cleanup must remove temporary runner records and endpoints without removing Functions DNS links."
  }
}

run "planning_prod_service_endpoints" {
  command = plan

  override_resource {
    target          = azurerm_subnet.subnet1_functions
    override_during = plan
    values = {
      id = "/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/rg-test/providers/Microsoft.Network/virtualNetworks/vnet-test/subnets/functions"
    }
  }

  assert {
    condition = var.environment_prefix != "prod" || (
      azurerm_key_vault.kv.public_network_access_enabled &&
      azurerm_storage_account.data_storage.public_network_access == "Enabled" &&
      one(azurerm_key_vault.kv.network_acls).default_action == "Deny" &&
      one(azurerm_storage_account.data_storage.network_rules).default_action == "Deny" &&
      toset(one(azurerm_key_vault.kv.network_acls).virtual_network_subnet_ids) == toset([azurerm_subnet.subnet1_functions.id, data.azurerm_subnet.github_runner_vnet_subnet.id]) &&
      toset(one(azurerm_storage_account.data_storage.network_rules).virtual_network_subnet_ids) == toset([azurerm_subnet.subnet1_functions.id, data.azurerm_subnet.github_runner_vnet_subnet.id]) &&
      toset([for endpoint in azurerm_subnet.subnet1_functions.service_endpoint : endpoint.service]) == toset(["Microsoft.KeyVault", "Microsoft.Storage"])
    )
    error_message = "Production must use service endpoints with default-deny firewalls and allowlist the Functions and runner subnets."
  }

  assert {
    condition = var.environment_prefix != "prod" || (
      length(azurerm_subnet.subnet2_private_endpoints) == 0 &&
      length(azurerm_private_dns_zone.private_dns) == 0 &&
      length(azurerm_private_dns_zone.data_storage_private_dns) == 0 &&
      length(azurerm_private_dns_zone.data_storage_queue) == 0 &&
      length(azurerm_private_dns_zone.data_storage_blob) == 0 &&
      length(azurerm_private_endpoint.kv_pe) == 0 &&
      length(azurerm_private_endpoint.data_storage_pe) == 0 &&
      length(azurerm_private_endpoint.data_storage_queue) == 0 &&
      length(azurerm_private_endpoint.data_storage_blob) == 0 &&
      length(azurerm_private_endpoint.kv_runner_pe) == 0 &&
      length(azurerm_private_endpoint.data_storage_runner_pe) == 0 &&
      length(data.azurerm_private_dns_zone.runner) == 0 &&
      length(data.azurerm_private_dns_zone_virtual_network_link.runner) == 0
    )
    error_message = "Production must not provision Private Link networking or depend on shared runner DNS when using service endpoints."
  }
}

run "planning_without_additional_origins" {
  command = plan

  variables {
    review_frontend_origins = []
  }

  assert {
    condition = (
      azurerm_function_app_flex_consumption.function_app.app_settings["ReviewTelegram__ModeratorIds"] == "" &&
      azurerm_function_app_flex_consumption.function_app.https_only &&
      length(azurerm_function_app_flex_consumption.function_app.auth_settings_v2) == 0
    )
    error_message = "Review must default to an empty allowlist, require HTTPS and have no platform authentication."
  }

  assert {
    condition     = length(azurerm_function_app_flex_consumption.function_app.site_config[0].cors) == 1 && !azurerm_function_app_flex_consumption.function_app.site_config[0].cors[0].support_credentials
    error_message = "CORS must remain configured without allowing cookie credentials when no additional origins are supplied."
  }
}

run "planning_with_telegram_moderators" {
  command = plan

  variables {
    review_telegram_moderator_ids = "123456789,4503599627370495"
  }

  assert {
    condition     = azurerm_function_app_flex_consumption.function_app.app_settings["ReviewTelegram__ModeratorIds"] == "123456789,4503599627370495"
    error_message = "Forward the exact Telegram allowlist to the Functions environment."
  }
}

run "rejecting_invalid_telegram_moderators" {
  command = plan

  variables {
    review_telegram_moderator_ids = "123456789,0"
  }

  expect_failures = [var.review_telegram_moderator_ids]
}
