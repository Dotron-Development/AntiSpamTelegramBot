resource "azurerm_ai_services" "ai_services" {
  name                = "${local.appName}-ai-services-${var.environment_prefix}"
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name
  sku_name            = "S0"
  tags                = local.tags
}

resource "azurerm_cognitive_deployment" "spam_recognition" {
  name                 = var.spam_recognition.name
  cognitive_account_id = azurerm_ai_services.ai_services.id
  rai_policy_name      = azurerm_cognitive_account_rai_policy.rai_policy_high.name

  model {
    format  = "OpenAI"
    name    = var.spam_recognition.name
    version = var.spam_recognition.version
  }

  sku {
    name     = "GlobalStandard"
    capacity = var.spam_recognition.capacity
  }
}

resource "azurerm_cognitive_deployment" "image_text_extraction" {
  name                 = var.image_text_extractor.name
  cognitive_account_id = azurerm_ai_services.ai_services.id
  rai_policy_name      = azurerm_cognitive_account_rai_policy.rai_policy_high.name

  model {
    format  = "OpenAI"
    name    = var.image_text_extractor.name
    version = var.image_text_extractor.version
  }

  sku {
    name     = "GlobalStandard"
    capacity = var.image_text_extractor.capacity
  }
}

resource "azurerm_cognitive_account_rai_policy" "rai_policy_high" {
  name                 = "rai-policy-high"
  cognitive_account_id = azurerm_ai_services.ai_services.id
  base_policy_name     = "Microsoft.Default"

  content_filter {
    name               = "Hate"
    filter_enabled     = true
    block_enabled      = true
    severity_threshold = "High"
    source             = "Prompt"
  }

  content_filter {
    name               = "Sexual"
    filter_enabled     = true
    block_enabled      = true
    severity_threshold = "High"
    source             = "Prompt"
  }

  content_filter {
    name               = "Violence"
    filter_enabled     = true
    block_enabled      = true
    severity_threshold = "High"
    source             = "Prompt"
  }

  content_filter {
    name               = "SelfHarm"
    filter_enabled     = true
    block_enabled      = true
    severity_threshold = "High"
    source             = "Prompt"
  }

  mode = "Asynchronous_filter"
}
