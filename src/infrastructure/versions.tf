terraform {
  required_version = ">= 1.17.0, < 2.0"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 5.11.0, < 6.0"
    }
    time = {
      source  = "hashicorp/time"
      version = ">= 0.14.2, < 0.15.0"
    }
    null = {
      source  = "hashicorp/null"
      version = ">= 3.3.2, < 4.0"
    }
  }

  backend "azurerm" {
  }
}

provider "azurerm" {
  storage_use_azuread             = true
  resource_provider_registrations = "legacy"
  features {
    enhanced_validation {
      locations          = true
      resource_providers = true
    }
    resource_group {
      prevent_deletion_if_contains_resources = false
    }
    key_vault {
      purge_soft_delete_on_destroy    = true
      recover_soft_deleted_key_vaults = true
    }
    cognitive_account {
      purge_soft_delete_on_destroy = true
    }
  }
}


