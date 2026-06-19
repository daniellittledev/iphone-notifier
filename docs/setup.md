# Setup guide

End-to-end setup for the iPhone push-notification relay: Apple, Azure, the
backend, the app, the CLI, and CI/CD. Work through the sections in order.

- [1. Prerequisites](#1-prerequisites)
- [2. Apple Developer setup](#2-apple-developer-setup)
- [3. Deploy the backend (manual)](#3-deploy-the-backend-manual)
- [4. Configure & run the app](#4-configure--run-the-app)
- [5. Send from the CLI](#5-send-from-the-cli)
- [6. CI/CD (deploy from GitHub)](#6-cicd-deploy-from-github)
- [7. Local backend development](#7-local-backend-development)
- [8. Troubleshooting](#8-troubleshooting)

---

## 1. Prerequisites

| Tool | Used for | Install |
|------|----------|---------|
| Apple Developer Program | APNs + Sign in with Apple ($99/yr) | <https://developer.apple.com/programs/> |
| Azure subscription | hosting the relay | <https://azure.microsoft.com> |
| .NET 8 SDK | build the F# backend | `apt install dotnet-sdk-8.0` / `brew install dotnet` |
| Azure CLI (`az`) | deploy infra | <https://learn.microsoft.com/cli/azure/install-azure-cli> |
| Azure Functions Core Tools v4 | publish the Function app | `npm i -g azure-functions-core-tools@4` |
| Xcode 15+ | build the iOS app | Mac App Store |
| XcodeGen (optional) | generate the Xcode project | `brew install xcodegen` |

---

## 2. Apple Developer setup

These steps are manual and cannot be automated. Note the values in **bold** —
you'll feed them into the backend deployment.

1. **Enroll** in the Apple Developer Program.
2. **App ID** — Certificates, Identifiers & Profiles → Identifiers → `+` →
   App IDs → App. Use an explicit bundle id, e.g. `com.example.iphonenotifier`
   (**APPLE_BUNDLE_ID**). Enable capabilities **Push Notifications** and
   **Sign in with Apple**.
3. **APNs auth key** — Keys → `+` → enable **Apple Push Notifications service
   (APNs)** → Register → **Download the `.p8` file** (only offered once).
   Record the **Key ID** (**APNS_KEY_ID**, 10 chars) shown on the key, and your
   **Team ID** (**APNS_TEAM_ID**, 10 chars, top-right of the portal).
4. Keep the `.p8` safe; it is the **APNS_KEY** input (its full text contents).

> Token-based APNs (one `.p8`) works for both sandbox and production. Choose the
> APNs **environment** at deploy time: `Sandbox` for Xcode/dev builds,
> `Production` for TestFlight/App Store builds.

---

## 3. Deploy the backend (manual)

```bash
export APPLE_BUNDLE_ID=com.example.iphonenotifier
export APNS_KEY_ID=XXXXXXXXXX
export APNS_TEAM_ID=YYYYYYYYYY
export APNS_ENV=Sandbox                       # or Production
export APNS_KEY_FILE=~/Downloads/AuthKey_XXXXXXXXXX.p8
# JWT_SIGNING_KEY is auto-generated if unset:
# export JWT_SIGNING_KEY="$(openssl rand -base64 48)"

az login
./infra/deploy.sh
```

`deploy.sh` creates the resource group, runs `what-if`, deploys `infra/main.bicep`,
then publishes the Function app. It prints the **API base URL**
(`https://<func>.azurewebsites.net/api`) — save it for the app and CLI.

Resources created: Storage (Functions + Tables), Consumption Function App
(.NET 8 isolated), Notification Hub (APNs token auth), Key Vault (+ managed
identity access), Application Insights + Log Analytics.

---

## 4. Configure & run the app

1. `cd ios && xcodegen generate` (or create the Xcode project from `Sources/`).
2. Open `IphoneNotifier.xcodeproj`. In the target's **Build Settings**, add a
   User-Defined setting `NTFY_API_BASE_URL` = the API base URL from step 3.
3. Set **Signing** → your Team, and the bundle id `com.example.iphonenotifier`.
   Confirm the **Push Notifications** and **Sign in with Apple** capabilities are
   present (they come from `Sources/IphoneNotifier.entitlements`).
4. Build & run on a **real device** — the simulator cannot obtain an APNs token.
5. Sign in with Apple, then grant notification permission. The device registers
   itself with the relay automatically.

---

## 5. Send from the CLI

Create a token in the app: **Settings → New token label → Create** (copy it; it's
shown only once).

```bash
export NTFY_API_URL=https://<func>.azurewebsites.net/api
export NTFY_API_KEY=ntfy_...

./cli/notify.sh "Hello" "from my laptop"
./cli/notify.sh -p high -d '{"url":"https://example.com"}' "Deploy" "succeeded"
echo "piped body" | ./cli/notify.sh "Title only"
```

PowerShell:

```powershell
$env:NTFY_API_URL = "https://<func>.azurewebsites.net/api"
$env:NTFY_API_KEY = "ntfy_..."
./cli/notify.ps1 -Title "Build done" -Body "Deploy succeeded" -Priority high
```

See [payload-contract.md](payload-contract.md) for the full JSON schema.

---

## 6. CI/CD (deploy from GitHub)

`.github/workflows/deploy.yml` deploys infra + code on push to `main` using
Azure **OIDC** (no stored passwords).

**a. Create an app registration / service principal** and grant it access to the
resource group. Because the Bicep creates a role assignment (Key Vault access for
the Function identity), the principal needs **Owner** or **User Access
Administrator** on the RG:

```bash
az ad app create --display-name iphone-notifier-cicd
APP_ID=$(az ad app list --display-name iphone-notifier-cicd --query "[0].appId" -o tsv)
az ad sp create --id "$APP_ID"
az role assignment create --assignee "$APP_ID" --role Owner \
  --scope /subscriptions/<SUB_ID>/resourceGroups/<RG>
```

**b. Add a federated credential** so GitHub can log in as that app:

```bash
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:daniellittledev/iphone-notifier:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

**c. Configure the repo** (GitHub → Settings):

- **Secrets:** `AZURE_CLIENT_ID` (= `$APP_ID`), `AZURE_TENANT_ID`,
  `AZURE_SUBSCRIPTION_ID`, `APNS_KEY` (the `.p8` contents), `JWT_SIGNING_KEY`.
- **Variables:** `AZURE_RESOURCE_GROUP`, `AZURE_LOCATION`, `NAME_PREFIX`,
  `APPLE_BUNDLE_ID`, `APNS_KEY_ID`, `APNS_TEAM_ID`, `APNS_ENV`.

Push to `main` (or run the **Deploy** workflow manually) and it will deploy. CI
(`ci.yml`) builds/tests the backend and validates the Bicep on every PR.

---

## 7. Local backend development

```bash
# Table emulator
npm i -g azurite && azurite &

cd server
cp local.settings.json.example local.settings.json   # fill in NH + secrets
func start                                            # http://localhost:7071/api
dotnet test ../server.tests                           # unit tests
```

Auth, persistence, and history work locally. Real push delivery still needs a
configured Notification Hub and a physical device.

---

## 8. Troubleshooting

- **No push received** — confirm the device registered (sign in completes, push
  permission granted), that `APNS_ENV` matches your build (`Sandbox` for Xcode
  runs), and check Application Insights logs on the Function app for send errors.
- **401 from the API** — session JWT expired (sign in again) or wrong/rotated CLI
  token. Tokens are shown only once; create a new one in Settings.
- **`func publish` fails** — ensure Functions Core Tools v4 and that
  `dotnet publish server/NotifyRelay.fsproj -c Release` succeeds first.
- **Deploy role-assignment error in CI** — the OIDC principal lacks rights to
  create role assignments; grant it Owner/User Access Administrator on the RG.
- **Sign in with Apple fails** — the App ID must have the capability enabled and
  the bundle id must equal `AppleBundleId` configured on the backend.
