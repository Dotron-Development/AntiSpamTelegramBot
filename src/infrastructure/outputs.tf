output "resource_group_name" {
  value = data.terraform_remote_state.openai_data.outputs.resource_group_name
}

output "functionapp_name" {
  value = azurerm_windows_function_app.function_app.name
}

