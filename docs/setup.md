# Setup runbook

## 0. Prerequisites (manual, one-time)

These require an Apple Developer Program membership ($99/yr) and cannot be automated.

1. **Enroll** in the Apple Developer Program.
2. **App ID**: create an explicit App ID (e.g. `com.example.iphonenotifier`) with
   the **Push Notifications** and **Sign in with Apple** capabilities enabled.
3. **APNs auth key**: Certificates → Keys → create a key with APNs enabled.
   Download the `.p8` file (you get it once). Note the **Key ID** and your **Team ID**.
4. Have an **Azure subscription** and install `az`, `.NET 8 SDK`, and
   `Azure Functions Core Tools v4`. For the app: Xcode + (optional) `xcodegen`.

## 1. Deploy the backend

```bash
export APPLE_BUNDLE_ID=com.example.iphonenotifier
export APNS_KEY_ID=XXXXXXXXXX
export APNS_TEAM_ID=YYYYYYYYYY
export APNS_ENV=Sandbox                 # Production for TestFlight/App Store builds
export APNS_KEY_FILE=~/Downloads/AuthKey_XXXXXXXXXX.p8
az login
./infra/deploy.sh
```

The script runs `what-if`, deploys the Bicep, then publishes the Function app.
It prints the **API base URL** — you'll need it for the app and CLI.

## 2. Configure & run the app

1. `cd ios && xcodegen generate` (or create the Xcode project manually from `Sources/`).
2. In the target's build settings, add a User-Defined setting
   `NTFY_API_BASE_URL` = the API base URL from step 1.
3. Set your signing **Team** and bundle id (`com.example.iphonenotifier`).
4. Run on a **real device** (the simulator cannot obtain an APNs token).
5. Sign in with Apple → grant notification permission.

## 3. Send from the CLI

```bash
export NTFY_API_URL=https://<func>.azurewebsites.net/api
export NTFY_API_KEY=ntfy_...     # created in the app: Settings → Create token
./cli/notify.sh "Hello" "from my laptop"
./cli/notify.sh -p high -d '{"url":"https://example.com"}' "Deploy" "succeeded"
```

## Local backend development

```bash
# Install + run the Azurite table emulator (npm i -g azurite; azurite &)
cd server
cp local.settings.json.example local.settings.json   # fill in NH + secrets
func start
```

Note: real push delivery needs a configured Notification Hub and a real device;
locally you can exercise auth, persistence, and history without delivery.
