<#
.SYNOPSIS
  Send a push notification to your iPhone via the relay.
.DESCRIPTION
  Config via env vars:
    NTFY_API_URL  e.g. https://ntfy-func-abcde.azurewebsites.net/api
    NTFY_API_KEY  a CLI token created in the app (ntfy_...)
.EXAMPLE
  ./notify.ps1 -Title "Build done" -Body "Deploy succeeded" -Priority high
.EXAMPLE
  ./notify.ps1 -Title "Alert" -Data @{ url = "https://x" }
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$Title,
  [string]$Body = "",
  [ValidateSet("normal", "high")][string]$Priority = "normal",
  [hashtable]$Data,
  [string]$IdempotencyKey
)

$apiUrl = $env:NTFY_API_URL
$apiKey = $env:NTFY_API_KEY
if (-not $apiUrl) { throw "Set NTFY_API_URL" }
if (-not $apiKey) { throw "Set NTFY_API_KEY" }

$payload = @{ title = $Title; body = $Body; priority = $Priority }
if ($Data) { $payload.data = $Data }
if ($IdempotencyKey) { $payload.idempotencyKey = $IdempotencyKey }

$json = $payload | ConvertTo-Json -Depth 10 -Compress

Invoke-RestMethod -Method Post -Uri "$apiUrl/notifications/send" `
  -Headers @{ "x-api-key" = $apiKey } `
  -ContentType "application/json" `
  -Body $json
