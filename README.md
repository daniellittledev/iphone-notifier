# iPhone Notifier

Push notifications to your iPhone by sending a simple JSON HTTP request. A
serverless relay receives the request and delivers it as a native push; the app
keeps full history with read/unread tracking. Multi-user, multi-device.

```
CLI ──(x-api-key, JSON)──► Azure Function (F#) ──► Azure Notification Hubs ──► APNs ──► iPhone(s)
                                  │
                                  └─ Table Storage (durable history + read state)
App ──(Sign in with Apple → JWT)──► register device / sync history / mark read
```

## Components

| Path | What |
|------|------|
| `server/` | .NET 8 isolated **F# Azure Functions** HTTP API (auth, devices, send, history). |
| `server.tests/` | xUnit tests for auth/token/priority logic. |
| `infra/` | **Bicep** for Storage, Functions, Notification Hub, Key Vault, App Insights. |
| `ios/` | **SwiftUI** app: Sign in with Apple, push registration, history UI, token management. |
| `cli/` | `notify.sh` / `notify.ps1` example senders. |
| `docs/` | API contract and setup runbook. |

## Quick start

See **[docs/setup.md](docs/setup.md)**. In short: deploy `infra/`, point the app
and CLI at the resulting API URL, sign in, create a CLI token, send.

## Design decisions

- **History source of truth** is server-side (Table Storage) so it is complete
  even if a push is dropped and survives reinstall; the app caches the last
  fetch locally for instant/offline display.
- **Delivery** via Azure Notification Hubs with a `user:{userId}` tag per device,
  so one send fans out to all of a user's devices.
- **App auth**: Sign in with Apple → backend-issued session JWT.
- **CLI auth**: per-user API tokens (`ntfy_...`), stored only as salted hashes.

## Build & test the backend

```bash
cd server && dotnet build
cd ../server.tests && dotnet test
```

## CI/CD

- **CI** (`.github/workflows/ci.yml`): on PRs/pushes, builds + tests the F#
  backend and validates the Bicep.
- **CD** (`.github/workflows/deploy.yml`): on push to `main`, deploys the infra
  and publishes the Function app via Azure OIDC login. Configure these first:
  - Secrets: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`,
    `APNS_KEY` (the `.p8` contents), `JWT_SIGNING_KEY`.
  - Variables: `AZURE_RESOURCE_GROUP`, `AZURE_LOCATION`, `NAME_PREFIX`,
    `APPLE_BUNDLE_ID`, `APNS_KEY_ID`, `APNS_TEAM_ID`, `APNS_ENV`.
  - The OIDC service principal needs Contributor on the resource group (it
    creates a role assignment, so User Access Administrator / Owner is simplest).

## Status / not yet done

- End-to-end push requires an Apple Developer account + real device (see setup).
- Optional **Notification Service Extension** (offline persistence / rich pushes)
  is described in the plan but not yet implemented.
