data "terraform_remote_state" "data" {
  backend = "azurerm"
  config = {
    resource_group_name  = var.main_state_resource_group_name
    storage_account_name = var.main_state_storage_account_name
    container_name       = var.main_state_container_name
    key                  = var.main_state_key
  }
}
