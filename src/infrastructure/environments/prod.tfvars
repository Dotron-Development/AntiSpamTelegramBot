vnet_address_prefix                = "10.1"
location                           = "Sweden Central"
environment_name                   = "production"
environment_prefix                 = "prod"
github_runners_vnet_name           = "vnet-github-runners"
github_runners_vnet_resource_group = "rg-github-vm-runners"
github_runners_vnet_subnet_name    = "subnet-github-runners"
disable_public_access              = true
bot_name                           = "@TgAIAntiSpamBot"
image_text_extractor = {
  name     = "gpt-4.1-mini"
  version  = "2025-04-14"
  capacity = 50
}
spam_recognition = {
  name     = "gpt-5-mini"
  version  = "2025-08-07"
  capacity = 50
}
forwardSpamToChatId = "-1002395980780"
debug_ai_response   = true
