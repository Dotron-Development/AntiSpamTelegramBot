#!/usr/bin/env bash
set -euo pipefail

infrastructure=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
workdir=$(mktemp -d)
trap 'rm -rf -- "$workdir"' EXIT
cd "$workdir"
mkdir environments
printf 'disable_public_access = false # service endpoints\n' > environments/dev.tfvars
printf 'disable_public_access = true\n' > environments/ppr.tfvars
export GITHUB_OUTPUT="$workdir/output"

az() {
  case "$1 $2" in
    'group exists') echo "${GROUP_EXISTS:-true}" ;;
    'keyvault list') printf '%s\n' "${KEY_VAULT_ACCESS-}" ;;
    'storage account') printf '%s\n' "${STORAGE_ACCESS-}" ;;
    'network vnet') printf '[\n  "Microsoft.KeyVault",\n  "Microsoft.Storage"\n]\n' ;;
    *) return 98 ;;
  esac
}

terraform() {
  [[ "$*" == 'state list' ]] || return 98
  printf '%s\n' "${STATE_ADDRESSES-}"
  return "${STATE_EXIT_CODE:-0}"
}
export -f az terraform

check() {
  local mode=$1 environment=$2 expected=$3 actual
  : > "$GITHUB_OUTPUT"
  bash "$infrastructure/runner-connectivity.sh" "$mode" "$environment"
  actual=$(< "$GITHUB_OUTPUT")
  if [[ "$actual" != "$expected" ]]; then
    printf 'Expected:\n%s\nGot:\n%s\n' "$expected" "$actual" >&2
    exit 1
  fi
}

KEY_VAULT_ACCESS=$'kv-ai-tg-bot-dev\tEnabled' STORAGE_ACCESS=$'satgbotdatadev\tEnabled' \
  check setup dev $'required=false\nkey_vault=false\nstorage=false'
KEY_VAULT_ACCESS=$'kv-ai-tg-bot-dev\tEnabled' STORAGE_ACCESS=$'satgbotdatadev\tDisabled' \
  check setup dev $'functions_service_endpoints=[  "Microsoft.KeyVault",  "Microsoft.Storage"]\nrequired=true\nkey_vault=false\nstorage=true'
KEY_VAULT_ACCESS=$'kv-ai-tg-bot-dev\tDisabled' STORAGE_ACCESS=$'satgbotdatadev\tEnabled' \
  check setup dev $'functions_service_endpoints=[  "Microsoft.KeyVault",  "Microsoft.Storage"]\nrequired=true\nkey_vault=true\nstorage=false'
GROUP_EXISTS=false check setup dev $'required=false\nkey_vault=false\nstorage=false'
check setup ppr $'required=true\nkey_vault=true\nstorage=true'
STATE_ADDRESSES='azurerm_private_dns_zone.private_dns[0]' check cleanup dev 'required=false'
STATE_ADDRESSES='azurerm_private_dns_a_record.keyvault_runner[0]' check cleanup dev 'required=true'
STATE_ADDRESSES='No state file was found!' STATE_EXIT_CODE=1 check cleanup dev 'required=false'

if KEY_VAULT_ACCESS=$'kv-ai-tg-bot-dev\tUnknown' bash "$infrastructure/runner-connectivity.sh" setup dev 2>/dev/null; then
  echo 'Unrecognized Azure network access mode must fail detection' >&2
  exit 1
fi

echo 'Runner connectivity tests passed'
