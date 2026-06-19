module NotifyRelay.Tests

open System
open Xunit
open NotifyRelay
open NotifyRelay.Domain

let private testConfig: Config =
    { StorageConnectionString = "UseDevelopmentStorage=true"
      NotificationHubConnectionString = "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v"
      NotificationHubName = "hub"
      JwtSigningKey = "this-is-a-long-enough-test-signing-key-0123456789"
      JwtIssuer = "iphone-notifier"
      JwtAudience = "iphone-notifier-app"
      JwtLifetime = TimeSpan.FromDays 30.0
      AppleBundleId = "com.example.iphonenotifier" }

[<Fact>]
let ``Priority parse is case-insensitive and defaults to Normal`` () =
    Assert.Equal(High, Priority.parse "high")
    Assert.Equal(High, Priority.parse "HIGH")
    Assert.Equal(Normal, Priority.parse "normal")
    Assert.Equal(Normal, Priority.parse "")
    Assert.Equal(Normal, Priority.parse null)

[<Fact>]
let ``APNs priority maps high to 10 and normal to 5`` () =
    Assert.Equal(10, Priority.toApnsPriority High)
    Assert.Equal(5, Priority.toApnsPriority Normal)

[<Fact>]
let ``Token hashing is stable and differs per token`` () =
    let auth = Auth testConfig
    let h1 = auth.HashToken "secret-a"
    let h2 = auth.HashToken "secret-a"
    let h3 = auth.HashToken "secret-b"
    Assert.Equal(h1, h2)
    Assert.NotEqual<string>(h1, h3)
    Assert.Equal(64, h1.Length) // SHA-256 hex

[<Fact>]
let ``Generated token is prefixed and its hash round-trips`` () =
    let auth = Auth testConfig
    let plaintext, hash = auth.GenerateApiToken()
    Assert.StartsWith("ntfy_", plaintext)
    Assert.Equal(hash, auth.HashToken plaintext)

[<Fact>]
let ``Session token round-trips to the same userId`` () =
    let auth = Auth testConfig
    let userId = Guid.NewGuid().ToString("N")
    let token, expires = auth.IssueSessionToken userId
    Assert.True(expires > DateTimeOffset.UtcNow)
    Assert.Equal(Some userId, auth.ValidateSessionToken token)

[<Fact>]
let ``Tampered or garbage session tokens are rejected`` () =
    let auth = Auth testConfig
    Assert.Equal(None, auth.ValidateSessionToken "not-a-jwt")
    let token, _ = auth.IssueSessionToken "abc"
    Assert.Equal(None, auth.ValidateSessionToken (token + "x"))
