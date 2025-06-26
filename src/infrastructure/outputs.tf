output "resource_group_name" {
  value = azurerm_resource_group.rg.name
}

output "functionapp_name" {
  value = azurerm_function_app_flex_consumption.function_app.name
}

output "function_app_identity_id" {
  value = azurerm_user_assigned_identity.functionapp_identity.principal_id
}
