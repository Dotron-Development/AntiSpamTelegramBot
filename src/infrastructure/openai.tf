module "openai" {
  source                        = "Azure/openai/azurerm"
  version                       = "0.1.5"
  location                      = var.location
  resource_group_name           = azurerm_resource_group.rg.name
  account_name                  = "open-ai-account-${var.environment_prefix}"
  sku_name                      = "S0"
  public_network_access_enabled = true
  deployment = {
    "gpt-4-turbo" = {
      name         = "gpt-4-turbo"
      model_format = "OpenAI"
      model_name   = "gpt-4o"
      scale_type   = "Standard"
      capacity     = 1
    }
  }
}
