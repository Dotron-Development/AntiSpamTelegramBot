module "openai" {
  source              = "./openai"
  location            = var.location
  account_name        = "open-ai-services-account-${var.environment_prefix}"
  resource_group_name = azurerm_resource_group.rg.name
}
