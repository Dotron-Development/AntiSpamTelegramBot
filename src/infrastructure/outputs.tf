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

output "review_spa_build_settings" {
  description = "Public build-time settings for the review SPA; tenant is supplied within the build job."
  value = jsonencode({
    API_URL      = "https://${azurerm_function_app_flex_consumption.function_app.default_hostname}"
    CLIENT_ID    = var.review_client_id
    API_SCOPE    = "api://${var.review_client_id}/Review.Access"
    REDIRECT_URI = local.review_static_web_app_origin
  })
}
