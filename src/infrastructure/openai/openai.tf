module "avm-res-cognitiveservices-account" {
  source              = "Azure/avm-res-cognitiveservices-account/azurerm"
  kind                = "OpenAI"
  location            = var.location
  name                = "open-ai-services-account-${var.environment_prefix}"
  resource_group_name = var.resource_group_name
  sku_name            = "S0"

  cognitive_deployments = {
    "gpt-4-32k" = {
      name = "gpt-4-32k"
      model = {
        format  = "OpenAI"
        name    = "gpt-4-32k"
        version = "0613"
      }
      scale = {
        type = "Standard"
      }
    }
  }
}
