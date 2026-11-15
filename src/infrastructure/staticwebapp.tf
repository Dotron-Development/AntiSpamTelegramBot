locals {
  review_static_web_app_origin = "https://${azurerm_static_web_app.review.default_host_name}"
}

resource "azurerm_static_web_app" "review" {
  name                = "swa-${local.appName}-${var.environment_prefix}"
  location            = "West Europe"
  resource_group_name = azurerm_resource_group.rg.name
  sku_tier            = "Free"
  sku_size            = "Free"
  tags                = local.tags

  lifecycle {
    ignore_changes = [repository_branch, repository_url]
  }
}
