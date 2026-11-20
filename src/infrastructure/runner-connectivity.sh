#!/usr/bin/env bash
set -euo pipefail

mode=$1
environment=$2

if [[ "$mode" == "cleanup" ]]; then
  if ! addresses=$(terraform state list 2>&1); then
    if [[ "$addresses" == "No state file was found!"* ]]; then
      echo 'required=false' >> "$GITHUB_OUTPUT"
      exit 0
    fi
    printf '%s\n' "$addresses" >&2
    exit 1
  fi

  required=false
  if grep -Eq '^((time_sleep\.(kv|data_storage)_dns_propagation)|(azurerm_private_endpoint\.(kv|data_storage)_runner_pe)|(azurerm_private_dns_a_record\.(keyvault|data_storage)_runner))\[0\]$' <<< "$addresses"; then
    required=true
  fi
  echo "required=$required" >> "$GITHUB_OUTPUT"
  exit 0
fi

config="environments/$environment.tfvars"
if [[ ! -r "$config" ]]; then
  echo "Cannot read $config" >&2
  exit 1
fi

key_vault=true
storage=true
if grep -Eq '^[[:space:]]*disable_public_access[[:space:]]*=[[:space:]]*false[[:space:]]*((#|//).*)?$' "$config"; then
  # The two services can switch independently in a partially applied plan.
  group="rg-ai-tg-bot-$environment"
  exists=$(az group exists --name "$group" --only-show-errors)
  case "$exists" in
    true)
      key_vault_access=$(az keyvault list --resource-group "$group" --only-show-errors --output tsv \
        --query "[?name=='kv-ai-tg-bot-$environment'].[name, properties.publicNetworkAccess]")
      storage_access=$(az storage account list --resource-group "$group" --only-show-errors --output tsv \
        --query "[?name=='satgbotdata$environment'].[name, publicNetworkAccess]")
      ;;
    false) key_vault_access=; storage_access= ;;
    *) echo "Could not determine whether $group exists" >&2; exit 1 ;;
  esac

  requires_private_link() {
    case "$1" in
      *$'\tDisabled') echo true ;;
      *$'\tEnabled'|'') echo false ;;
      *) echo "Could not determine public network access for $2" >&2; return 1 ;;
    esac
  }
  key_vault=$(requires_private_link "$key_vault_access" 'Key Vault')
  storage=$(requires_private_link "$storage_access" 'Table Storage')
  if [[ "$key_vault" == true || "$storage" == true ]]; then
    # A targeted plan can still include the Functions subnet via the service firewall.
    endpoints=$(az network vnet list --resource-group "$group" --only-show-errors --output json \
      --query "[?name=='vnet-ai-tg-bot-$environment'].subnets[] | [?name=='subnet1-function-$environment'].serviceEndpoints[].service")
    printf 'functions_service_endpoints=%s\n' "$(tr -d '\r\n' <<< "$endpoints")" >> "$GITHUB_OUTPUT"
  fi
fi

required=false
if [[ "$key_vault" == true || "$storage" == true ]]; then
  required=true
fi
printf 'required=%s\nkey_vault=%s\nstorage=%s\n' "$required" "$key_vault" "$storage" >> "$GITHUB_OUTPUT"
