locals {
  review_web_app_name   = "web-${local.appName}-${var.environment_prefix}"
  review_web_app_origin = "https://${local.review_web_app_name}.azurewebsites.net"
}

resource "azurerm_service_plan" "review" {
  name                = "asp-review-${local.appName}-${var.environment_prefix}"
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name
  os_type             = "Linux"
  sku_name            = var.review_web_app_sku
  tags                = local.tags
}

resource "azurerm_linux_web_app" "review" {
  name                                           = local.review_web_app_name
  location                                       = var.location
  resource_group_name                            = azurerm_resource_group.rg.name
  service_plan_id                                = azurerm_service_plan.review.id
  https_only                                     = true
  ftp_publish_basic_authentication_enabled       = false
  webdeploy_publish_basic_authentication_enabled = false

  site_config {
    always_on           = true
    ftps_state          = "Disabled"
    minimum_tls_version = "1.2"
    app_command_line    = "pm2 serve /home/site/wwwroot --no-daemon --spa --port 8080"

    application_stack {
      node_version = "24-lts"
    }
  }

  app_settings = {
    API_URL                        = "https://${azurerm_function_app_flex_consumption.function_app.default_hostname}"
    TENANT_ID                      = data.azurerm_client_config.current.tenant_id
    CLIENT_ID                      = var.review_client_id
    API_SCOPE                      = "api://${var.review_client_id}/Review.Access"
    REDIRECT_URI                   = local.review_web_app_origin
    SCM_DO_BUILD_DURING_DEPLOYMENT = "false"
    WEBSITE_RUN_FROM_PACKAGE       = "1"
  }

  tags = local.tags
}
