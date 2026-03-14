resource "azurerm_storage_account" "data_storage" {
  name                          = "satgbotdata${var.environment_prefix}"
  resource_group_name           = azurerm_resource_group.rg.name
  location                      = var.location
  account_tier                  = "Standard"
  account_replication_type      = "GRS"
  min_tls_version               = "TLS1_2"
  shared_access_key_enabled     = false
  public_network_access_enabled = !var.disable_public_access
  tags                          = local.tags

  network_rules {
    default_action             = "Deny"
    virtual_network_subnet_ids = !var.disable_public_access ? [azurerm_subnet.subnet1_functions.id, data.azurerm_subnet.github_runner_vnet_subnet.id] : []
  }
}

# azurerm_storage_table uses SharedKey auth for GetTableACL even when storage_use_azuread = true,
# which is blocked by shared_access_key_enabled = false. Using null_resource + Azure CLI instead.
resource "null_resource" "data_storage_tables" {
  for_each = toset(["SpamStats", "SpamHistory", "SpamHash", "MessageCount"])

  triggers = {
    storage_account = azurerm_storage_account.data_storage.name
    table_name      = each.key
  }

  provisioner "local-exec" {
    command = <<-EOT
      az login --service-principal \
        -u $ARM_CLIENT_ID \
        -p $ARM_CLIENT_SECRET \
        --tenant $ARM_TENANT_ID && \
      az storage table create \
        --name ${each.key} \
        --account-name ${azurerm_storage_account.data_storage.name} \
        --auth-mode login
    EOT
  }
}
