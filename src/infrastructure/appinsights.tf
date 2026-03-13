resource "azurerm_log_analytics_workspace" "analytics_workspace" {
  name                = "logs-workspace-${local.appName}-${var.environment_prefix}"
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

resource "azurerm_application_insights" "appinsights" {
  name                = "app-insights-${local.appName}-${var.environment_prefix}"
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name
  workspace_id        = azurerm_log_analytics_workspace.analytics_workspace.id
  application_type    = "web"
}
