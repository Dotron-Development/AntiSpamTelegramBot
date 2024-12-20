locals {
  appName = "ai-tg-antispam-bot"
  tags = {
    environment = var.environment_name
    appName     = local.appName
  }
}
