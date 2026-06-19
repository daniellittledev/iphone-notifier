# API contract

Base URL: `https://<function-app>.azurewebsites.net/api`

Auth:
- **App endpoints** use a session JWT: `Authorization: Bearer <jwt>` (obtained from `POST /auth/apple`).
- **Send endpoint** uses a CLI token: `x-api-key: <ntfy_...>` (created in the app's Settings).

## Send a notification (CLI)

```
POST /notifications/send
x-api-key: ntfy_xxx
Content-Type: application/json

{
  "title": "Build finished",        // string (title or body required)
  "body": "deploy succeeded",       // string
  "data": { "url": "https://..." }, // optional arbitrary JSON object
  "priority": "high",               // "high" | "normal" (default normal)
  "idempotencyKey": "ci-1234"       // optional; repeated keys are de-duped
}
```

Response `201`: `{ "id": "...", "duplicate": false }`
(`200` with `duplicate: true` if the idempotency key was already used.)

## Auth

```
POST /auth/apple
{ "identityToken": "<apple identity token>" }
```
Response: `{ "token": "<jwt>", "expiresAt": "...", "userId": "..." }`

## Devices

```
POST /devices            { "apnsToken": "<hex>", "platform": "ios" }  -> { "deviceId": "..." }
DELETE /devices/{id}                                                   -> 204
```

## History (app)

```
GET /notifications?read=false&pageSize=50&continuation=<token>
  -> { "items": [ { id, title, body, data, priority, read, createdAt, readAt } ], "continuation": "<token|null>" }

PATCH /notifications/{id}   { "read": true }   -> { "id": "...", "read": true }
GET  /notifications/unread-count               -> { "count": 3 }
```

## CLI tokens (app)

```
POST   /tokens   { "label": "laptop" }  -> { "tokenId": "...", "label": "...", "token": "ntfy_..." }  (token shown once)
GET    /tokens                          -> { "tokens": [ { tokenId, label, createdAt, lastUsed } ] }
DELETE /tokens/{id}                     -> 204
```

## Account

```
DELETE /account   -> 204   (deletes user, devices, notifications, tokens, and ANH installations)
```

## APNs payload produced

```json
{
  "aps": { "alert": { "title": "...", "body": "..." }, "badge": 3, "sound": "default", "mutable-content": 1 },
  "notificationId": "...",
  "data": { ... }
}
```
