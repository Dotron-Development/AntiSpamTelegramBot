resource "azurerm_storage_account" "function_storage" {
  name                     = "saaiassistantfnapp${var.environment_prefix}"
  resource_group_name      = azurerm_resource_group.rg.name
  location                 = var.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  tags                     = local.tags
}

resource "azurerm_service_plan" "function_sp" {
  name                = "sp-ai-assistant-function-${var.environment_prefix}"
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name

  os_type  = "Linux"
  sku_name = "Y1"

  tags = local.tags
}

resource "azurerm_linux_function_app" "function_app" {
  name                            = "fn-ai-assistant-function-${var.environment_prefix}"
  location                        = var.location
  resource_group_name             = azurerm_resource_group.rg.name
  service_plan_id                 = azurerm_service_plan.function_sp.id
  storage_account_name            = azurerm_storage_account.function_storage.name
  storage_account_access_key      = azurerm_storage_account.function_storage.primary_access_key
  key_vault_reference_identity_id = azurerm_user_assigned_identity.functionapp_identity.principal_id

  site_config {
    always_on = false
  }

  app_settings = {
    "AzureWebJobsStorage"         = azurerm_storage_account.function_storage.primary_connection_string
    "FUNCTIONS_EXTENSION_VERSION" = "~4"
    "KEY_VAULT_SECRET"            = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.telegram_bot_token.id})"
  }


  identity {
    type = "UserAssigned"
    identity_ids = [
      azurerm_user_assigned_identity.functionapp_identity.id
    ]
  }

  tags = local.tags
}
