variable "environment_name" {
  type = string
}

variable "environment_prefix" {
  type = string
}

variable "location" {
  type = string
}

variable "application_owner_object_id" {
  type = string
}

variable "openai_state_resource_group_name" {
  description = "The name of the terraform backend resource group name."
  type        = string
}

variable "openai_state_storage_account_name" {
  description = "The name of the terraform backend storage account."
  type        = string
}

variable "openai_state_container_name" {
  description = "The name of the terraform backend container."
  type        = string
}

variable "openai_state_key" {
  description = "The main state file key (path)."
  type        = string
}

variable "telegram_bot_token" {
  description = "The token for the telegram bot."
  type        = string
  sensitive   = true
}

variable "telegram_bot_secret_header" {
  description = "The secret header for the telegram bot."
  type        = string
  sensitive   = true
}


variable "github_runners_vnet_name" {
  description = "The name of the virtual network for the github runners."
  type        = string
  sensitive   = true
}

variable "github_runners_vnet_resource_group" {
  description = "The resource group for the virtual network for the github runners."
  type        = string
  sensitive   = true
}

variable "github_runners_vnet_subnet_name" {
  description = "The name of the subnet for the virtual network for the github runners."
  type        = string
  sensitive   = true
}
