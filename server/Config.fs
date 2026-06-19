namespace NotifyRelay

open System

/// Strongly-typed configuration loaded from environment variables / app settings.
/// In Azure, secrets are surfaced as app settings via Key Vault references, so
/// the worker only ever reads environment variables.
type Config =
    { /// Connection string for the Table Storage account.
      StorageConnectionString: string
      /// Azure Notification Hubs connection string (with Send access).
      NotificationHubConnectionString: string
      /// Notification Hub name.
      NotificationHubName: string
      /// Symmetric signing key for our own session JWTs (base64 or raw text).
      JwtSigningKey: string
      /// Issuer we stamp on session JWTs.
      JwtIssuer: string
      /// Audience we stamp on session JWTs.
      JwtAudience: string
      /// Session JWT lifetime.
      JwtLifetime: TimeSpan
      /// Expected audience for Apple identity tokens (the app bundle id).
      AppleBundleId: string }

module Config =

    let private env name =
        match Environment.GetEnvironmentVariable name with
        | null
        | "" -> None
        | v -> Some v

    let private require name =
        match env name with
        | Some v -> v
        | None -> failwithf "Required setting '%s' is missing" name

    let load () : Config =
        { StorageConnectionString =
            // Functions runtime always provides AzureWebJobsStorage; reuse it for tables.
            env "TableStorageConnectionString"
            |> Option.orElse (env "AzureWebJobsStorage")
            |> Option.defaultWith (fun () -> require "AzureWebJobsStorage")
          NotificationHubConnectionString = require "NotificationHubConnectionString"
          NotificationHubName = require "NotificationHubName"
          JwtSigningKey = require "JwtSigningKey"
          JwtIssuer = env "JwtIssuer" |> Option.defaultValue "iphone-notifier"
          JwtAudience = env "JwtAudience" |> Option.defaultValue "iphone-notifier-app"
          JwtLifetime =
            env "JwtLifetimeDays"
            |> Option.bind (fun s ->
                match Int32.TryParse s with
                | true, d -> Some(TimeSpan.FromDays(float d))
                | _ -> None)
            |> Option.defaultValue (TimeSpan.FromDays 30.0)
          AppleBundleId = require "AppleBundleId" }
