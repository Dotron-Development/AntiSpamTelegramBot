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
