variable "main_state_resource_group_name" {
  description = "The name of the terraform backend resource group."
  type        = string
}

variable "main_state_storage_account_name" {
  description = "The name of the terraform backend storage account."
  type        = string
}

variable "main_state_container_name" {
  description = "The name of the terraform backend container."
  type        = string
}

variable "main_key" {
  description = "The main state file key (path)."
  type        = string
}

variable "environment" {
  description = "The environment name."
  type        = string
}

variable "location" {
  description = "The location of the Open AI services account."
  type        = string
}

variable "environment_prefix" {
  description = "The prefix to use for all resources."
  type        = string
}
