data "azurerm_client_config" "current" {}

data "terraform_remote_state" "openai_data" {
  backend = "azurerm"
  config = {
    resource_group_name  = var.openai_state_resource_group_name
    storage_account_name = var.openai_state_storage_account_name
    container_name       = var.openai_state_container_name
    key                  = var.openai_state_key
  }
}
