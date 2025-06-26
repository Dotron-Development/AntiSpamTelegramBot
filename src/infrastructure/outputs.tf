output "resource_group_name" {
  value = azurerm_resource_group.rg.name
}

output "functionapp_name" {
  value = azurerm_function_app_flex_consumption.function_app.name
}

