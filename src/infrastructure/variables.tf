variable "vnet_address_prefix" {
  description = "First two octets of the VNet address space (e.g. \"10.0\" for dev, \"10.1\" for prod)."
  type        = string
}

variable "environment_name" {
  type = string
}

variable "environment_prefix" {
  type = string
}

variable "location" {
  type = string
}

variable "function_alert_emails" {
  description = "Email recipients for failed function execution alerts."
  type        = list(string)
  default     = ["serii_trofimov@epam.com"]
  nullable    = false

  validation {
    condition     = length(var.function_alert_emails) > 0
    error_message = "Supply at least one email recipient for function failure alerts."
  }
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

variable "enable_runner_endpoints" {
  description = "Create temporary runner private endpoints and shared DNS records during deployment; disable them during cleanup."
  type        = bool
  default     = true
}

variable "bot_name" {
  description = "The name of the bot. Example: @someBotName"
  type        = string
}

variable "spam_restriction_timeout" {
  description = "Spam restriction and not-spam republishing timeout in .NET TimeSpan format (e.g. 03:00:00)."
  type        = string
  default     = "03:00:00"
  nullable    = false
}

variable "image_text_extractor" {
  description = "The configuration of the image text extractor deployment."
  type = object({
    name     = string
    version  = string
    capacity = number
  })
}

variable "command_cooldown" {
  description = "Minimum interval between calls of the same command in a chat, in .NET TimeSpan format."
  type        = string
  default     = "00:05:00"
  nullable    = false
}

variable "spam_recognition" {
  description = "The configuration of the spam recognition deployment."
  type = object({
    name     = string
    version  = string
    capacity = number
  })
}

variable "mention" {
  description = "The configuration of the separate bot-mention reply deployment."
  type = object({
    name     = string
    version  = string
    capacity = number
  })
  default = {
    name     = "gpt-6.1-terra"
    version  = "2026-10-26"
    capacity = 10
  }
}

variable "debug_ai_response" {
  description = "Enable debugging of AI responses."
  type        = bool
  nullable    = true
}

variable "review_telegram_moderator_ids" {
  description = "Comma-separated positive Telegram user IDs for review access; empty denies everyone."
  type        = string
  default     = ""
  nullable    = false

  validation {
    condition = var.review_telegram_moderator_ids == "" || alltrue([
      for id in split(",", var.review_telegram_moderator_ids) : try(
        can(regex("^[0-9]+$", id)) && tonumber(id) > 0 && tonumber(id) <= 9223372036854775807,
        false
      )
    ])
    error_message = "Use comma-separated positive decimal Telegram user IDs without spaces (each must fit a signed 64-bit integer), or leave empty."
  }
}

variable "review_frontend_origins" {
  description = "Additional exact SPA origins allowed by Function App CORS; the deployed Static Web App is always included."
  type        = list(string)
  default     = ["http://localhost:5173"]
  nullable    = false

  validation {
    condition = alltrue([
      for origin in var.review_frontend_origins : can(regex(
        "^(https://[a-zA-Z0-9.-]+(:[0-9]+)?|http://(localhost|127\\.0\\.0\\.1)(:[0-9]+)?)$",
        origin
      ))
    ])
    error_message = "Supply exact HTTPS origins, or HTTP loopback origins, without paths, trailing slashes or wildcards."
  }
}

variable "search_embedding_capacity" {
  description = "Capacity in thousands of tokens per minute for the search embedding deployment."
  type        = number
  default     = 10
}
