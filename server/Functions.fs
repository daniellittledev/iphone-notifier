namespace NotifyRelay

open System
open System.Net
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging
open NotifyRelay.Domain

/// All HTTP-triggered endpoints. Auth is enforced per-endpoint inside the body
/// (AuthorizationLevel.Anonymous): session JWT for the app, x-api-key for CLI.
type Functions(auth: Auth, storage: Storage, push: PushService, loggerFactory: ILoggerFactory) =

    let log = loggerFactory.CreateLogger("Functions")

    /// Deterministic device id from the APNs token, so re-registering the same
    /// token updates one row/installation instead of creating duplicates.
    let deviceIdFor (apnsToken: string) =
        use sha = SHA256.Create()
        sha.ComputeHash(Encoding.UTF8.GetBytes apnsToken)
        |> Array.take 16
        |> Array.map (fun b -> b.ToString("x2"))
        |> String.concat ""

    let requireSession req f =
        match Http.authenticateSession auth req with
        | Some caller -> f caller
        | None -> Http.error req HttpStatusCode.Unauthorized "Invalid or missing session token"

    // ---- Auth ----

    [<Function("AppleAuth")>]
    member _.AppleAuth([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/apple")>] req: HttpRequestData) =
        task {
            match! Http.readJson<AppleAuthRequest> req with
            | None -> return! Http.error req HttpStatusCode.BadRequest "Invalid body"
            | Some body when String.IsNullOrWhiteSpace body.IdentityToken ->
                return! Http.error req HttpStatusCode.BadRequest "identityToken is required"
            | Some body ->
                try
                    let! appleSub, email = auth.VerifyAppleToken body.IdentityToken
                    let! existing = storage.GetUserByAppleSub appleSub
                    let user =
                        match existing with
                        | Some u -> u
                        | None ->
                            { UserId = Guid.NewGuid().ToString("N")
                              AppleSub = appleSub
                              Email = email
                              CreatedAt = DateTimeOffset.UtcNow }
                    if existing.IsNone then do! storage.UpsertUser user
                    let token, expires = auth.IssueSessionToken user.UserId
                    return! Http.json req HttpStatusCode.OK {| token = token; expiresAt = expires; userId = user.UserId |}
                with ex ->
                    log.LogWarning(ex, "Apple token verification failed")
                    return! Http.error req HttpStatusCode.Unauthorized "Apple token verification failed"
        }

    // ---- Devices ----

    [<Function("RegisterDevice")>]
    member _.RegisterDevice([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "devices")>] req: HttpRequestData) =
        requireSession req (fun caller ->
            task {
                match! Http.readJson<DeviceRegisterRequest> req with
                | None -> return! Http.error req HttpStatusCode.BadRequest "Invalid body"
                | Some body when String.IsNullOrWhiteSpace body.ApnsToken ->
                    return! Http.error req HttpStatusCode.BadRequest "apnsToken is required"
                | Some body ->
                    let deviceId = deviceIdFor body.ApnsToken
                    let platform = if String.IsNullOrWhiteSpace body.Platform then "ios" else body.Platform
                    do! push.RegisterInstallation(deviceId, body.ApnsToken, caller.UserId)
                    do! storage.UpsertDevice
                            { UserId = caller.UserId
                              DeviceId = deviceId
                              ApnsToken = body.ApnsToken
                              InstallationId = deviceId
                              Platform = platform
                              LastSeen = DateTimeOffset.UtcNow }
                    return! Http.json req HttpStatusCode.OK {| deviceId = deviceId |}
            })

    [<Function("DeleteDevice")>]
    member _.DeleteDevice
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "devices/{deviceId}")>] req: HttpRequestData,
         deviceId: string) =
        requireSession req (fun caller ->
            task {
                match! storage.GetDevice(caller.UserId, deviceId) with
                | None -> return! Http.error req HttpStatusCode.NotFound "Device not found"
                | Some device ->
                    do! push.DeleteInstallation device.InstallationId
                    do! storage.DeleteDevice(caller.UserId, deviceId)
                    return! Http.empty req HttpStatusCode.NoContent
            })

    // ---- CLI API tokens ----

    [<Function("CreateToken")>]
    member _.CreateToken([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "tokens")>] req: HttpRequestData) =
        requireSession req (fun caller ->
            task {
                let! body = Http.readJson<CreateTokenRequest> req
                let label =
                    match body with
                    | Some b when not (String.IsNullOrWhiteSpace b.Label) -> b.Label
                    | _ -> "cli"
                let plaintext, hash = auth.GenerateApiToken()
                let tokenId = Guid.NewGuid().ToString("N")
                do! storage.AddToken
                        { UserId = caller.UserId
                          TokenId = tokenId
                          Label = label
                          Hash = hash
                          CreatedAt = DateTimeOffset.UtcNow
                          LastUsed = None }
                // Plaintext is returned exactly once and never stored.
                return! Http.json req HttpStatusCode.Created {| tokenId = tokenId; label = label; token = plaintext |}
            })

    [<Function("ListTokens")>]
    member _.ListTokens([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tokens")>] req: HttpRequestData) =
        requireSession req (fun caller ->
            task {
                let! tokens = storage.ListTokens caller.UserId
                let view =
                    tokens
                    |> List.map (fun t ->
                        {| tokenId = t.TokenId
                           label = t.Label
                           createdAt = t.CreatedAt
                           lastUsed = t.LastUsed |})
                return! Http.json req HttpStatusCode.OK {| tokens = view |}
            })

    [<Function("DeleteToken")>]
    member _.DeleteToken
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "tokens/{tokenId}")>] req: HttpRequestData,
         tokenId: string) =
        requireSession req (fun caller ->
            task {
                do! storage.DeleteToken(caller.UserId, tokenId)
                return! Http.empty req HttpStatusCode.NoContent
            })

    // ---- Notifications: send (CLI) ----

    [<Function("SendNotification")>]
    member _.SendNotification
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notifications/send")>] req: HttpRequestData) =
        task {
            match! Http.authenticateApiKey auth storage req with
            | None -> return! Http.error req HttpStatusCode.Unauthorized "Invalid or missing API key"
            | Some caller ->
                match! Http.readJson<SendRequest> req with
                | None -> return! Http.error req HttpStatusCode.BadRequest "Invalid body"
                | Some body when String.IsNullOrWhiteSpace body.Title && String.IsNullOrWhiteSpace body.Body ->
                    return! Http.error req HttpStatusCode.BadRequest "title or body is required"
                | Some body ->
                    let dataJson =
                        if body.Data.ValueKind = JsonValueKind.Undefined || body.Data.ValueKind = JsonValueKind.Null then
                            None
                        else
                            Some(body.Data.GetRawText())

                    // Idempotency: reuse the supplied key as the id and short-circuit duplicates.
                    let notificationId =
                        if String.IsNullOrWhiteSpace body.IdempotencyKey then
                            Guid.NewGuid().ToString("N")
                        else
                            body.IdempotencyKey

                    let! existing = storage.FindNotification(caller.UserId, notificationId)
                    match existing with
                    | Some _ ->
                        return! Http.json req HttpStatusCode.OK {| id = notificationId; duplicate = true |}
                    | None ->
                        let notification =
                            { UserId = caller.UserId
                              NotificationId = notificationId
                              Title = (if isNull body.Title then "" else body.Title)
                              Body = (if isNull body.Body then "" else body.Body)
                              Data = dataJson
                              Priority = Priority.parse body.Priority
                              Read = false
                              CreatedAt = DateTimeOffset.UtcNow
                              ReadAt = None }
                        do! storage.AddNotification notification
                        let! badge = storage.CountUnread caller.UserId
                        try
                            do! push.SendToUser(caller.UserId, notification, badge)
                        with ex ->
                            log.LogError(ex, "Push delivery failed for {UserId}", caller.UserId)
                        return! Http.json req HttpStatusCode.Created {| id = notificationId; duplicate = false |}
        }

    // ---- Notifications: history (app) ----

    [<Function("ListNotifications")>]
    member _.ListNotifications
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications")>] req: HttpRequestData) =
        requireSession req (fun caller ->
            task {
                let readFilter =
                    match Http.queryParam req "read" with
                    | Some "true" -> Some true
                    | Some "false" -> Some false
                    | _ -> None
                let pageSize =
                    match Http.queryParam req "pageSize" with
                    | Some s ->
                        match Int32.TryParse s with
                        | true, n when n > 0 && n <= 100 -> n
                        | _ -> 50
                    | None -> 50
                let continuation = Http.queryParam req "continuation"
                let! items, next = storage.ListNotifications(caller.UserId, readFilter, pageSize, continuation)
                let view =
                    items
                    |> List.map (fun n ->
                        {| id = n.NotificationId
                           title = n.Title
                           body = n.Body
                           data = n.Data
                           priority = Priority.toString n.Priority
                           read = n.Read
                           createdAt = n.CreatedAt
                           readAt = n.ReadAt |})
                return! Http.json req HttpStatusCode.OK {| items = view; continuation = next |}
            })

    [<Function("MarkNotification")>]
    member _.MarkNotification
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "notifications/{notificationId}")>] req: HttpRequestData,
         notificationId: string) =
        requireSession req (fun caller ->
            task {
                match! Http.readJson<MarkReadRequest> req with
                | None -> return! Http.error req HttpStatusCode.BadRequest "Invalid body"
                | Some body ->
                    match! storage.FindNotification(caller.UserId, notificationId) with
                    | None -> return! Http.error req HttpStatusCode.NotFound "Notification not found"
                    | Some(rowKey, _) ->
                        do! storage.SetRead(caller.UserId, rowKey, body.Read)
                        return! Http.json req HttpStatusCode.OK {| id = notificationId; read = body.Read |}
            })

    [<Function("UnreadCount")>]
    member _.UnreadCount
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications/unread-count")>] req: HttpRequestData) =
        requireSession req (fun caller ->
            task {
                let! count = storage.CountUnread caller.UserId
                return! Http.json req HttpStatusCode.OK {| count = count |}
            })

    // ---- Account ----

    [<Function("DeleteAccount")>]
    member _.DeleteAccount([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "account")>] req: HttpRequestData) =
        requireSession req (fun caller ->
            task {
                let! devices = storage.ListDevices caller.UserId
                for d in devices do
                    do! push.DeleteInstallation d.InstallationId
                do! storage.DeleteAccount caller.UserId
                return! Http.empty req HttpStatusCode.NoContent
            })
