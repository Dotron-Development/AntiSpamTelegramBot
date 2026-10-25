output "resource_group_name" {
  value = azurerm_resource_group.rg.name
}

output "functionapp_name" {
  value = azurerm_function_app_flex_consumption.function_app.name
}

output "function_app_identity_name" {
  value = azurerm_user_assigned_identity.functionapp_identity.name
}

output "entra_application_client_id" {
  value = azuread_application.permission_check.client_id
}

output "entra_application_object_id" {
  value = azuread_application.permission_check.object_id
}
