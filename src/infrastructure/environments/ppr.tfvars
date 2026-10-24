vnet_address_prefix                = "10.3"
location                           = "Sweden Central"
environment_name                   = "pre-production"
environment_prefix                 = "ppr"
github_runners_vnet_name           = "vnet-github-runners"
github_runners_vnet_resource_group = "rg-github-vm-runners"
github_runners_vnet_subnet_name    = "subnet-github-runners"
disable_public_access              = true
bot_name                           = "@PprTgAIAntiSpamBot"
image_text_extractor = {
  name     = "gpt-6-luna"
  version  = "2026-09-22"
  capacity = 50
}
spam_recognition = {
  name     = "gpt-6-luna"
  version  = "2026-09-22"
  capacity = 50
}
forwardSpamToChatId = "-1002395980780"
debug_ai_response   = true
