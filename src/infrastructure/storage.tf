resource "azurerm_storage_account" "main_storage" {
  name                     = "satgbotdata${var.environment_prefix}"
  resource_group_name      = data.terraform_remote_state.openai_data.resource_group_name
  location                 = var.location
  account_tier             = "Standard"
  account_replication_type = "GRS"
  tags                     = local.tags
}
