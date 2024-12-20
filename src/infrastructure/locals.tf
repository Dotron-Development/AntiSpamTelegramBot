locals {
  appName = "ai-tg-antispam-bot"
  kv_name = "kv-${local.appName}"
  tags = {
    environment = var.environment_name
    appName     = local.appName
  }
}
