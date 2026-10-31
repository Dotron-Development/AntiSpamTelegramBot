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
  description = "Create temporary runner private endpoints during deployment; disable them during cleanup."
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

variable "chronicle" {
  description = "The configuration of the separate chat-summary deployment."
  type = object({
    name     = string
    version  = string
    capacity = number
  })
  default = {
    name     = "gpt-6-luna"
    version  = "2026-09-22"
    capacity = 10
  }
}

variable "debug_ai_response" {
  description = "Enable debugging of AI responses."
  type        = bool
  nullable    = true
}

variable "review_client_id" {
  description = "Client ID of the existing Entra registration used by the review SPA and API."
  type        = string
  nullable    = false

  validation {
    condition     = can(regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", var.review_client_id))
    error_message = "review_client_id must be an application client GUID."
  }
}

variable "review_user_object_ids" {
  description = "Explicit allowlist of Entra user object IDs permitted to call the review API."
  type        = list(string)
  nullable    = false

  validation {
    condition = (
      length(var.review_user_object_ids) > 0 &&
      length(join("", var.review_user_object_ids)) <= 500 &&
      alltrue([for identity in var.review_user_object_ids : can(regex(
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        identity
      ))])
    )
    error_message = "Supply at least one user object GUID; the combined IDs must not exceed 500 characters."
  }
}

variable "review_frontend_origins" {
  description = "Additional exact SPA origins allowed by Function App CORS; the deployed Web App is always included."
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

variable "review_web_app_sku" {
  description = "Paid App Service plan SKU for the review SPA; separate from the Flex Consumption plan."
  type        = string
  default     = "B1"
  nullable    = false
}
