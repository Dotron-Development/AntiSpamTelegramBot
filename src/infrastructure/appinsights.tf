resource "azurerm_application_insights" "appinsights" {
  name                = "ai-${local.appName}-${var.environment_prefix}"
  location            = var.location
  resource_group_name = data.terraform_remote_state.openai_data.outputs.resource_group_name
  application_type    = "web"
}
