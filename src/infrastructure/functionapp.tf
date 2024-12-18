resource "azurerm_storage_account" "function_storage" {
  name                     = "safunctionapp${var.environment_prefix}"
  resource_group_name      = azurerm_resource_group.rg.name
  location                 = var.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  tags = {
    environment = var.environment_name
  }
}

resource "azurerm_service_plan" "function_sp" {
  name                = "sp_ai_assistant_function${var.environment_prefix}"
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name

  os_type  = "Windows"
  sku_name = "Y1"

  tags = {
    environment = var.environment_name
  }
}

resource "azurerm_function_app" "function_app" {
  name                       = "test-azure-functions"
  location                   = var.location
  resource_group_name        = azurerm_resource_group.rg.name
  app_service_plan_id        = azurerm_service_plan.function_sp.id
  storage_account_name       = azurerm_storage_account.function_storage.name
  storage_account_access_key = azurerm_storage_account.function_storage.primary_access_key
}
