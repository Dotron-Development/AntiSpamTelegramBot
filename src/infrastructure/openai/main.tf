data "terraform_remote_state" "storage" {
  backend = "azurerm"
  config = {
    resource_group_name  = "bg-terraform-backend"
    storage_account_name = "bgterraformbackendsa"
    container_name       = "terraform-state-${var.environment}-container"
    key                  = "storage/terraform.tfstate"
  }
}
