locals {
  kv_name = "kv-ai-assistant"
  appName = "ai-antispam-bot"
  tags = {
    environment = var.environment_name
    appName     = local.appName
  }
}
