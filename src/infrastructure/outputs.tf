output "resource_group_name" {
  value = azurerm_resource_group.rg.name
}

output "functionapp_identity_principal_id" {
  value = azurerm_user_assigned_identity.functionapp_identity.principal_id
}
