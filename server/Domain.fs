namespace NotifyRelay

open System

/// Core domain types shared across the relay.
module Domain =

    /// An authenticated caller, resolved from either a session JWT (the app)
    /// or an x-api-key header (a CLI token). Both resolve to a userId.
    type Caller =
        { UserId: string }

    type Priority =
        | Normal
        | High

    module Priority =
        let parse (s: string) =
            match (if isNull s then "" else s.ToLowerInvariant()) with
            | "high" -> High
            | _ -> Normal

        let toString =
            function
            | High -> "high"
            | Normal -> "normal"

        /// APNs apns-priority header value (10 = immediate, 5 = power-conscious).
        let toApnsPriority =
            function
            | High -> 10
            | Normal -> 5

    /// A user record, keyed by the Apple subject identifier.
    type User =
        { UserId: string
          AppleSub: string
          Email: string option
          CreatedAt: DateTimeOffset }

    /// A registered device for a user. One row per APNs device token.
    type Device =
        { UserId: string
          DeviceId: string
          ApnsToken: string
          InstallationId: string
          Platform: string
          LastSeen: DateTimeOffset }

    /// A stored notification. This is the durable source of truth for history.
    type Notification =
        { UserId: string
          NotificationId: string
          Title: string
          Body: string
          /// Arbitrary JSON object (serialized) carried to the client.
          Data: string option
          Priority: Priority
          Read: bool
          CreatedAt: DateTimeOffset
          ReadAt: DateTimeOffset option }

    /// A CLI API token. The secret itself is never stored — only a salted hash.
    type ApiToken =
        { UserId: string
          TokenId: string
          Label: string
          Hash: string
          CreatedAt: DateTimeOffset
          LastUsed: DateTimeOffset option }

    /// Inbound JSON contract for POST /notifications/send.
    [<CLIMutable>]
    type SendRequest =
        { Title: string
          Body: string
          Data: System.Text.Json.JsonElement
          Priority: string
          IdempotencyKey: string }

    [<CLIMutable>]
    type AppleAuthRequest = { IdentityToken: string }

    [<CLIMutable>]
    type CreateTokenRequest = { Label: string }

    [<CLIMutable>]
    type DeviceRegisterRequest =
        { ApnsToken: string
          Platform: string }

    [<CLIMutable>]
    type MarkReadRequest = { Read: bool }
