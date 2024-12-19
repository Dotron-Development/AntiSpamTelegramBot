module "avm-res-cognitiveservices-account" {
  source              = "Azure/avm-res-cognitiveservices-account/azurerm"
  kind                = "OpenAI"
  location            = var.location
  name                = "open-ai-services-account-${var.environment_prefix}"
  resource_group_name = data.terraform_remote_state.data.outputs.resource_group_name
  sku_name            = "S0"

  cognitive_deployments = {
    "gpt-4o" = {
      name = "gpt-4o"
      model = {
        format = "OpenAI"
        name   = "gpt-4o"
      }
      scale = {
        type     = "Standard"
        capacity = 100
      }
    }
  }
}

## Grant access for the function app to the Open AI services account
resource "azurerm_role_assignment" "function_app_to_openai" {
  scope                = module.avm-res-cognitiveservices-account.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = data.terraform_remote_state.data.outputs.function_app_identity_principal_id
}
