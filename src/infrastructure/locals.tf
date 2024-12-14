locals {
  appName = "ai-tg-bot"
  kv_name = "kv-${local.appName}"
  tags = {
    environment = var.environment_name
    appName     = local.appName
  }
}
