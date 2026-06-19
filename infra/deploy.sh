#!/usr/bin/env bash
# Deploy the relay infrastructure and publish the Function app.
# Prereqs: az CLI logged in (az login), .NET 8 SDK, Azure Functions Core Tools v4.
set -euo pipefail

RG="${RG:-iphone-notifier-rg}"
LOCATION="${LOCATION:-australiaeast}"
NAME_PREFIX="${NAME_PREFIX:-ntfy}"
APPLE_BUNDLE_ID="${APPLE_BUNDLE_ID:?set APPLE_BUNDLE_ID}"
APNS_KEY_ID="${APNS_KEY_ID:?set APNS_KEY_ID}"
APNS_TEAM_ID="${APNS_TEAM_ID:?set APNS_TEAM_ID}"
APNS_ENV="${APNS_ENV:-Sandbox}"
APNS_KEY_FILE="${APNS_KEY_FILE:?set APNS_KEY_FILE to your AuthKey_XXXX.p8 path}"
JWT_SIGNING_KEY="${JWT_SIGNING_KEY:-$(openssl rand -base64 48)}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "==> Creating resource group $RG ($LOCATION)"
az group create -n "$RG" -l "$LOCATION" 1>/dev/null

echo "==> what-if"
az deployment group what-if -g "$RG" -f "$SCRIPT_DIR/main.bicep" \
  -p namePrefix="$NAME_PREFIX" appleBundleId="$APPLE_BUNDLE_ID" \
     apnsKeyId="$APNS_KEY_ID" apnsTeamId="$APNS_TEAM_ID" apnsEnvironment="$APNS_ENV" \
     apnsKey="$(cat "$APNS_KEY_FILE")" jwtSigningKey="$JWT_SIGNING_KEY"

echo "==> Deploying infrastructure"
OUTPUTS=$(az deployment group create -g "$RG" -f "$SCRIPT_DIR/main.bicep" \
  -p namePrefix="$NAME_PREFIX" appleBundleId="$APPLE_BUNDLE_ID" \
     apnsKeyId="$APNS_KEY_ID" apnsTeamId="$APNS_TEAM_ID" apnsEnvironment="$APNS_ENV" \
     apnsKey="$(cat "$APNS_KEY_FILE")" jwtSigningKey="$JWT_SIGNING_KEY" \
  --query properties.outputs -o json)

FUNC_NAME=$(echo "$OUTPUTS" | python3 -c 'import sys,json;print(json.load(sys.stdin)["functionAppName"]["value"])')
BASE_URL=$(echo "$OUTPUTS" | python3 -c 'import sys,json;print(json.load(sys.stdin)["functionBaseUrl"]["value"])')

echo "==> Publishing Function app to $FUNC_NAME"
( cd "$SCRIPT_DIR/../server" && func azure functionapp publish "$FUNC_NAME" --dotnet-isolated )

echo "==> Done. API base URL: $BASE_URL"
