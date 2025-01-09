resource "azurerm_key_vault" "kv" {
  name                        = "${local.kv_name}-${var.environment_prefix}"
  location                    = var.location
  resource_group_name         = data.terraform_remote_state.openai_data.outputs.resource_group_name
  enabled_for_disk_encryption = true
  tenant_id                   = data.azurerm_client_config.current.tenant_id
  soft_delete_retention_days  = 7
  purge_protection_enabled    = false
  sku_name                    = "standard"
  enable_rbac_authorization   = true

  ## network rules
  public_network_access_enabled = false

  network_acls {
    bypass         = "AzureServices"
    default_action = "Allow"

    # if public access is enabled connectivity is done through the service endpoint
    # if public access is disabled, we need to create a private endpoint
    virtual_network_subnet_ids = !var.disable_public_access ? [
      azurerm_subnet.subnet1.id,
      data.azurerm_subnet.github_runner_vnet_subnet.id
    ] : []
  }

  tags = local.tags
}

resource "azurerm_private_dns_zone" "private_dns" {
  count               = var.disable_public_access ? 1 : 0
  name                = "${local.kv_name}-${var.environment_prefix}.privatelink.vaultcore.azure.net"
  resource_group_name = data.terraform_remote_state.openai_data.outputs.resource_group_name
  tags                = local.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "keyvault_vnet_link" {
  count                 = var.disable_public_access ? 1 : 0
  name                  = "vnl-${local.kv_name}-${var.environment_prefix}"
  virtual_network_id    = azurerm_virtual_network.vnet.id
  private_dns_zone_name = azurerm_private_dns_zone.private_dns[0].name
  resource_group_name   = data.terraform_remote_state.openai_data.outputs.resource_group_name
  tags                  = local.tags
}

resource "azurerm_private_endpoint" "kv_pe" {
  count                         = var.disable_public_access ? 1 : 0
  name                          = "pe-${local.kv_name}-${var.environment_prefix}"
  subnet_id                     = azurerm_subnet.subnet1.id
  location                      = var.location
  resource_group_name           = data.terraform_remote_state.openai_data.outputs.resource_group_name
  custom_network_interface_name = "nic-${local.kv_name}-pe-${var.environment_prefix}"

  private_service_connection {
    name                           = "sc-${local.kv_name}-${var.environment_prefix}"
    private_connection_resource_id = azurerm_key_vault.kv.id
    is_manual_connection           = false
    subresource_names              = ["vault"]
  }

  ip_configuration {
    name               = "ip-${local.kv_name}-${var.environment_prefix}"
    private_ip_address = "10.0.1.33"
    subresource_name   = "vault"
    member_name        = "default"
  }

  tags = local.tags
}

resource "azurerm_private_dns_a_record" "keyvault_a_record" {
  count               = var.disable_public_access ? 1 : 0
  name                = "@"
  zone_name           = azurerm_private_dns_zone.private_dns[0].name
  resource_group_name = data.terraform_remote_state.openai_data.outputs.resource_group_name
  ttl                 = 300
  records = [
    azurerm_private_endpoint.kv_pe[0].ip_configuration[0].private_ip_address,
    azurerm_private_endpoint.kv_runner_pe[0].private_service_connection[0].private_ip_address
  ]

  tags = local.tags
}
