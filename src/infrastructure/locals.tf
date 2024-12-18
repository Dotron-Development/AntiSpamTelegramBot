locals {
  appName = "ai-antispam-bot"
  tags = {
    environment = var.environment_name
    appName     = local.appName
  }
}
