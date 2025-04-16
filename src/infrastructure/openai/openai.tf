module "avm-res-cognitiveservices-account" {
  source              = "Azure/avm-res-cognitiveservices-account/azurerm"
  kind                = "OpenAI"
  location            = module.global_constants.location
  name                = "${local.appName}-ai-services-account-${var.environment_prefix}"
  resource_group_name = "rg-${local.appName}-${var.environment_prefix}"
  sku_name            = "S0"

  cognitive_deployments = {
    "spam_recofnition" = {
      name            = module.global_constants.spam_recognition_model_name
      rai_policy_name = "all_high"
      model = {
        format  = "OpenAI"
        name    = module.global_constants.spam_recognition_model_name
        version = module.global_constants.spam_recognition_model_version
      }
      scale = {
        type     = "Standard"
        capacity = var.spam_recognition_capacity
      }
    },
    "image_text_extractor" = {
      name            = module.global_constants.image_text_extraction_model_name
      rai_policy_name = "all_high"
      model = {
        format  = "OpenAI"
        name    = module.global_constants.image_text_extraction_model_name
        version = module.global_constants.image_text_extraction_model_version
      }
      scale = {
        type     = "Standard"
        capacity = var.image_text_extractor_capacity
      }
    }

    depends_on = [rai_policies.policy1]
  }

  rai_policies = {
    policy1 = {
      name             = "all_high"
      base_policy_name = "Microsoft.Default"
      mode             = "Asynchronous_filter"
      content_filters = [
        {
          name               = "Hate"
          blocking           = true
          enabled            = true
          severity_threshold = "High"
          source             = "Prompt"
        },
        {
          name               = "Sexual"
          blocking           = true
          enabled            = true
          severity_threshold = "High"
          source             = "Prompt"
        },
        {
          name               = "Violence"
          blocking           = true
          enabled            = true
          severity_threshold = "High"
          source             = "Prompt"
        },
        {
          name               = "Selfharm"
          blocking           = true
          enabled            = true
          severity_threshold = "High"
          source             = "Prompt"
        },
        {
          name               = "Hate"
          blocking           = true
          enabled            = true
          severity_threshold = "High"
          source             = "Completion"
        },
        {
          name               = "Sexual"
          blocking           = true
          enabled            = true
          severity_threshold = "High"
          source             = "Completion"
        },
        {
          name               = "Violence"
          blocking           = true
          enabled            = true
          severity_threshold = "High"
          source             = "Completion"
        },
        {
          name               = "Selfharm"
          blocking           = true
          enabled            = true
          severity_threshold = "High"
          source             = "Completion"
        }
      ]
    }
  }

  tags = local.tags

  depends_on = [azurerm_resource_group.rg]
}
