vnet_address_prefix                = "10.0"
location                           = "Sweden Central"
environment_name                   = "development"
environment_prefix                 = "dev"
github_runners_vnet_name           = "vnet-github-runners"
github_runners_vnet_resource_group = "rg-github-vm-runners"
github_runners_vnet_subnet_name    = "subnet-github-runners"
disable_public_access              = false
bot_name                           = "@DevTgAIAntiSpamBot"
image_text_extractor = {
  name     = "gpt-4.1-mini"
  version  = "2025-04-14"
  capacity = 10
}
spam_recognition = {
  name     = "gpt-5-mini"
  version  = "2025-08-07"
  capacity = 10
}
forwardSpamToChatId = "-4835847055"
debug_ai_response   = true
