namespace NotifyRelay

open System
open System.Threading.Tasks
open Azure
open Azure.Data.Tables
open NotifyRelay.Domain

/// Azure Table Storage persistence. Tables:
///   Users         PK = AppleSub                       (lookup user by Apple identity)
///   UsersById     PK = "user", RK = UserId            (lookup user by our id)
///   Devices       PK = UserId, RK = DeviceId
///   Notifications PK = UserId, RK = invertedTicks_id   (newest-first ordering)
///   ApiTokens     PK = UserId, RK = TokenId
type Storage(config: Config) =

    let svc = TableServiceClient(config.StorageConnectionString)
    let table name =
        let c = svc.GetTableClient(name)
        c.CreateIfNotExists() |> ignore
        c

    let users = table "Users"
    let usersById = table "UsersById"
    let devices = table "Devices"
    let notifications = table "Notifications"
    let apiTokens = table "ApiTokens"

    // Sort key that orders newest-first when listed ascending by row key.
    let invertedTicks (t: DateTimeOffset) =
        sprintf "%019d" (DateTime.MaxValue.Ticks - t.UtcDateTime.Ticks)

    let getStr (e: TableEntity) key =
        match e.TryGetValue key with
        | true, (:? string as s) -> s
        | _ -> ""

    let getStrOpt (e: TableEntity) key =
        match getStr e key with
        | "" -> None
        | s -> Some s

    let getBool (e: TableEntity) key =
        match e.TryGetValue key with
        | true, (:? bool as b) -> b
        | _ -> false

    let getDto (e: TableEntity) key =
        match e.TryGetValue key with
        | true, (:? DateTimeOffset as d) -> d
        | _ -> DateTimeOffset.MinValue

    let getDtoOpt (e: TableEntity) key =
        match e.TryGetValue key with
        | true, (:? DateTimeOffset as d) -> Some d
        | _ -> None

    // ---- Users ----

    member _.UpsertUser(user: User) : Task =
        task {
            let e = TableEntity(user.AppleSub, user.AppleSub)
            e["UserId"] <- user.UserId
            e["AppleSub"] <- user.AppleSub
            e["Email"] <- (user.Email |> Option.defaultValue "")
            e["CreatedAt"] <- user.CreatedAt
            do! users.UpsertEntityAsync(e) :> Task

            let byId = TableEntity("user", user.UserId)
            byId["UserId"] <- user.UserId
            byId["AppleSub"] <- user.AppleSub
            do! usersById.UpsertEntityAsync(byId) :> Task
        }

    member _.GetUserByAppleSub(appleSub: string) : Task<User option> =
        task {
            try
                let! resp = users.GetEntityAsync<TableEntity>(appleSub, appleSub)
                let e = resp.Value
                return
                    Some
                        { UserId = getStr e "UserId"
                          AppleSub = getStr e "AppleSub"
                          Email = getStrOpt e "Email"
                          CreatedAt = getDto e "CreatedAt" }
            with :? RequestFailedException as ex when ex.Status = 404 -> return None
        }

    // ---- Devices ----

    member _.UpsertDevice(device: Device) : Task =
        task {
            let e = TableEntity(device.UserId, device.DeviceId)
            e["ApnsToken"] <- device.ApnsToken
            e["InstallationId"] <- device.InstallationId
            e["Platform"] <- device.Platform
            e["LastSeen"] <- device.LastSeen
            do! devices.UpsertEntityAsync(e) :> Task
        }

    member _.ListDevices(userId: string) : Task<Device list> =
        task {
            let results = ResizeArray<Device>()
            let query = devices.QueryAsync<TableEntity>(filter = sprintf "PartitionKey eq '%s'" userId)
            let mutable en = query.GetAsyncEnumerator()
            let mutable go = true
            while go do
                let! moved = en.MoveNextAsync()
                if moved then
                    let e = en.Current
                    results.Add
                        { UserId = e.PartitionKey
                          DeviceId = e.RowKey
                          ApnsToken = getStr e "ApnsToken"
                          InstallationId = getStr e "InstallationId"
                          Platform = getStr e "Platform"
                          LastSeen = getDto e "LastSeen" }
                else
                    go <- false
            return List.ofSeq results
        }

    member this.GetDevice(userId: string, deviceId: string) : Task<Device option> =
        task {
            try
                let! resp = devices.GetEntityAsync<TableEntity>(userId, deviceId)
                let e = resp.Value
                return
                    Some
                        { UserId = e.PartitionKey
                          DeviceId = e.RowKey
                          ApnsToken = getStr e "ApnsToken"
                          InstallationId = getStr e "InstallationId"
                          Platform = getStr e "Platform"
                          LastSeen = getDto e "LastSeen" }
            with :? RequestFailedException as ex when ex.Status = 404 -> return None
        }

    member _.DeleteDevice(userId: string, deviceId: string) : Task =
        devices.DeleteEntityAsync(userId, deviceId) :> Task

    // ---- Notifications ----

    member _.AddNotification(n: Notification) : Task =
        task {
            let rk = sprintf "%s_%s" (invertedTicks n.CreatedAt) n.NotificationId
            let e = TableEntity(n.UserId, rk)
            e["NotificationId"] <- n.NotificationId
            e["Title"] <- n.Title
            e["Body"] <- n.Body
            e["Data"] <- (n.Data |> Option.defaultValue "")
            e["Priority"] <- Priority.toString n.Priority
            e["Read"] <- n.Read
            e["CreatedAt"] <- n.CreatedAt
            match n.ReadAt with
            | Some r -> e["ReadAt"] <- r
            | None -> ()
            do! notifications.AddEntityAsync(e) :> Task
        }

    member private _.MapNotification(e: TableEntity) : Notification =
        { UserId = e.PartitionKey
          NotificationId = getStr e "NotificationId"
          Title = getStr e "Title"
          Body = getStr e "Body"
          Data = getStrOpt e "Data"
          Priority = Priority.parse (getStr e "Priority")
          Read = getBool e "Read"
          CreatedAt = getDto e "CreatedAt"
          ReadAt = getDtoOpt e "ReadAt" }

    /// Returns a page of notifications (newest first) and a continuation token.
    member this.ListNotifications
        (userId: string, readFilter: bool option, pageSize: int, continuation: string option)
        : Task<Notification list * string option> =
        task {
            let baseFilter = sprintf "PartitionKey eq '%s'" userId
            let filter =
                match readFilter with
                | Some r -> sprintf "%s and Read eq %b" baseFilter r
                | None -> baseFilter

            let pageable = notifications.QueryAsync<TableEntity>(filter = filter, maxPerPage = pageSize)
            let pages = pageable.AsPages(continuationToken = Option.toObj continuation, pageSizeHint = pageSize)
            let en = pages.GetAsyncEnumerator()
            let! moved = en.MoveNextAsync()
            if moved then
                let page = en.Current
                let items = page.Values |> Seq.map this.MapNotification |> List.ofSeq
                let next = if String.IsNullOrEmpty page.ContinuationToken then None else Some page.ContinuationToken
                return items, next
            else
                return [], None
        }

    /// Finds a single notification by its logical id (queries on the property).
    member this.FindNotification(userId: string, notificationId: string) : Task<(string * Notification) option> =
        task {
            let filter = sprintf "PartitionKey eq '%s' and NotificationId eq '%s'" userId notificationId
            let query = notifications.QueryAsync<TableEntity>(filter = filter)
            let en = query.GetAsyncEnumerator()
            let! moved = en.MoveNextAsync()
            if moved then
                let e = en.Current
                return Some(e.RowKey, this.MapNotification e)
            else
                return None
        }

    member _.SetRead(userId: string, rowKey: string, read: bool) : Task =
        task {
            let! resp = notifications.GetEntityAsync<TableEntity>(userId, rowKey)
            let e = resp.Value
            e["Read"] <- read
            if read then e["ReadAt"] <- DateTimeOffset.UtcNow
            do! notifications.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Merge) :> Task
        }

    member _.CountUnread(userId: string) : Task<int> =
        task {
            let filter = sprintf "PartitionKey eq '%s' and Read eq false" userId
            let query = notifications.QueryAsync<TableEntity>(filter = filter, select = [| "RowKey" |])
            let mutable count = 0
            let en = query.GetAsyncEnumerator()
            let mutable go = true
            while go do
                let! moved = en.MoveNextAsync()
                if moved then count <- count + 1 else go <- false
            return count
        }

    // ---- API tokens ----

    member _.AddToken(t: ApiToken) : Task =
        task {
            let e = TableEntity(t.UserId, t.TokenId)
            e["Label"] <- t.Label
            e["Hash"] <- t.Hash
            e["CreatedAt"] <- t.CreatedAt
            do! apiTokens.AddEntityAsync(e) :> Task
        }

    member private _.MapToken(e: TableEntity) : ApiToken =
        { UserId = e.PartitionKey
          TokenId = e.RowKey
          Label = getStr e "Label"
          Hash = getStr e "Hash"
          CreatedAt = getDto e "CreatedAt"
          LastUsed = getDtoOpt e "LastUsed" }

    member this.ListTokens(userId: string) : Task<ApiToken list> =
        task {
            let results = ResizeArray<ApiToken>()
            let query = apiTokens.QueryAsync<TableEntity>(filter = sprintf "PartitionKey eq '%s'" userId)
            let en = query.GetAsyncEnumerator()
            let mutable go = true
            while go do
                let! moved = en.MoveNextAsync()
                if moved then results.Add(this.MapToken en.Current) else go <- false
            return List.ofSeq results
        }

    /// Scans all tokens to find one matching a hash. Token sets are tiny
    /// (per-user CLI tokens), so a full scan is acceptable for this workload.
    member this.FindTokenByHash(hash: string) : Task<ApiToken option> =
        task {
            let query = apiTokens.QueryAsync<TableEntity>(filter = sprintf "Hash eq '%s'" hash)
            let en = query.GetAsyncEnumerator()
            let! moved = en.MoveNextAsync()
            if moved then return Some(this.MapToken en.Current) else return None
        }

    member _.TouchToken(userId: string, tokenId: string) : Task =
        task {
            try
                let! resp = apiTokens.GetEntityAsync<TableEntity>(userId, tokenId)
                let e = resp.Value
                e["LastUsed"] <- DateTimeOffset.UtcNow
                do! apiTokens.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Merge) :> Task
            with :? RequestFailedException -> ()
        }

    member _.DeleteToken(userId: string, tokenId: string) : Task =
        apiTokens.DeleteEntityAsync(userId, tokenId) :> Task

    // ---- Account deletion ----

    member _.GetAppleSubById(userId: string) : Task<string option> =
        task {
            try
                let! resp = usersById.GetEntityAsync<TableEntity>("user", userId)
                return getStrOpt resp.Value "AppleSub"
            with :? RequestFailedException as ex when ex.Status = 404 -> return None
        }

    member this.DeleteAccount(userId: string) : Task =
        task {
            let! appleSub = this.GetAppleSubById userId

            let deleteAll (client: TableClient) =
                task {
                    let query = client.QueryAsync<TableEntity>(filter = sprintf "PartitionKey eq '%s'" userId)
                    let en = query.GetAsyncEnumerator()
                    let mutable go = true
                    while go do
                        let! moved = en.MoveNextAsync()
                        if moved then
                            let e = en.Current
                            do! client.DeleteEntityAsync(e.PartitionKey, e.RowKey) :> Task
                        else
                            go <- false
                }

            do! deleteAll devices
            do! deleteAll notifications
            do! deleteAll apiTokens
            match appleSub with
            | Some sub ->
                try
                    do! users.DeleteEntityAsync(sub, sub) :> Task
                with :? RequestFailedException -> ()
            | None -> ()
            try
                do! usersById.DeleteEntityAsync("user", userId) :> Task
            with :? RequestFailedException -> ()
        }
