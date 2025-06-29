variable "environment_name" {
  type = string
}

variable "environment_prefix" {
  type = string
}

variable "location" {
  type = string
}

variable "keyvault_administrator_object_id" {
  description = "The id of the KV administrator."
  type        = string
}

variable "telegram_bot_token" {
  description = "The token for the telegram bot."
  type        = string
  sensitive   = true
}

variable "forwardSpamToChatId" {
  description = "Forward spam messages to group Id."
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

variable "disable_public_access" {
  description = "Disable public access to all resources. If true then private endpoints will be created."
  type        = bool
}

variable "bot_name" {
  description = "The name of the bot. Example: @someBotName"
  type        = string
}

variable "image_text_extractor_capacity" {
  description = "The capacity of the image text extractor deployment."
  type        = number
}

variable "spam_recognition_capacity" {
  description = "The capacity of the spam recognition deployment."
  type        = number
}

variable "debug_ai_response" {
  description = "Enable debugging of AI responses."
  type        = bool
  nullable    = true
}
