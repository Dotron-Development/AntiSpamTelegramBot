locals {
  kv_name = "kv-ai-tg-assistant"
  appName = "ai-tg-antispam-bot"
  tags = {
    environment = var.environment_name
    appName     = local.appName
  }
}
