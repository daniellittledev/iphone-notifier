namespace NotifyRelay

open System.Text.Json
open System.Threading.Tasks
open Microsoft.Azure.NotificationHubs
open NotifyRelay.Domain

/// Wraps Azure Notification Hubs: device registration via per-user tags and
/// fan-out delivery to all of a user's devices.
type PushService(config: Config) =

    let client =
        NotificationHubClient.CreateClientFromConnectionString(
            config.NotificationHubConnectionString,
            config.NotificationHubName)

    let userTag (userId: string) = sprintf "user:%s" userId

    /// Registers (or updates) a device installation tagged for its user, so a
    /// send to user:{userId} fans out to every device that user owns.
    member _.RegisterInstallation(installationId: string, apnsToken: string, userId: string) : Task =
        task {
            let installation = Installation()
            installation.InstallationId <- installationId
            installation.Platform <- NotificationPlatform.Apns
            installation.PushChannel <- apnsToken
            installation.Tags <- ResizeArray<string>([ userTag userId ])
            do! client.CreateOrUpdateInstallationAsync(installation)
        }

    member _.DeleteInstallation(installationId: string) : Task =
        task {
            try
                do! client.DeleteInstallationAsync(installationId)
            with _ -> ()
        }

    /// Builds the APNs payload and sends it to all of the user's devices.
    member _.SendToUser(userId: string, n: Notification, badge: int) : Task =
        task {
            let dataValue =
                match n.Data with
                | Some json when json <> "" -> JsonDocument.Parse(json).RootElement
                | _ -> JsonDocument.Parse("{}").RootElement

            let payload =
                let aps =
                    {| alert = {| title = n.Title; body = n.Body |}
                       badge = badge
                       sound = "default"
                       ``mutable-content`` = 1 |}
                {| aps = aps
                   notificationId = n.NotificationId
                   data = dataValue |}

            let json = JsonSerializer.Serialize payload
            let notification = AppleNotification(json)
            notification.Headers.Add("apns-priority", string (Priority.toApnsPriority n.Priority))
            notification.Headers.Add("apns-push-type", "alert")
            let! _outcome = client.SendNotificationAsync(notification, userTag userId)
            return ()
        }
