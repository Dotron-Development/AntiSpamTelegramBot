resource "azurerm_resource_group" "rg" {
  name     = "rg-ai-antispam-bot-${var.environment_prefix}"
  location = var.location
  tags     = local.tags
}
