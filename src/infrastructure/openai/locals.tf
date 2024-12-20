locals {
  appName = "ai-tg-bot"
  tags = {
    environment = var.environment_name
    appName     = local.appName
  }
}
