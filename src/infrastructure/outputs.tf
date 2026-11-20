output "resource_group_name" {
  value = azurerm_resource_group.rg.name
}

output "functionapp_name" {
  value = azurerm_function_app_flex_consumption.function_app.name
}

output "function_app_identity_name" {
  value = azurerm_user_assigned_identity.functionapp_identity.name
}

output "review_static_web_app_name" {
  value = azurerm_static_web_app.review.name
}

output "review_static_web_app_url" {
  value = local.review_static_web_app_origin
}

output "review_api_url" {
  description = "Public Functions origin used to build the Telegram Mini App."
  value       = "https://${azurerm_function_app_flex_consumption.function_app.default_hostname}"
}
