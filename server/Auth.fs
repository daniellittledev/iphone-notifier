namespace NotifyRelay

open System
open System.Security.Cryptography
open System.Text
open System.IdentityModel.Tokens.Jwt
open System.Security.Claims
open System.Threading
open Microsoft.IdentityModel.Tokens
open Microsoft.IdentityModel.Protocols
open Microsoft.IdentityModel.Protocols.OpenIdConnect

/// Authentication: Apple identity-token verification, our own session JWTs,
/// and CLI API-token hashing/verification.
type Auth(config: Config) =

    let appleConfigManager =
        ConfigurationManager<OpenIdConnectConfiguration>(
            "https://appleid.apple.com/.well-known/openid-configuration",
            OpenIdConnectConfigurationRetriever())

    let signingKey =
        SymmetricSecurityKey(Encoding.UTF8.GetBytes config.JwtSigningKey)

    // Keep original claim names (e.g. "sub") instead of remapping to long URIs.
    let handler = JwtSecurityTokenHandler(MapInboundClaims = false)

    let claimValue (principal: ClaimsPrincipal) (name: string) =
        match principal.FindFirst name with
        | null -> None
        | c when String.IsNullOrEmpty c.Value -> None
        | c -> Some c.Value

    /// Verifies an Apple identity token (signature, issuer, audience, expiry)
    /// and returns (appleSub, emailOption). Throws on invalid tokens.
    member _.VerifyAppleToken(identityToken: string) : System.Threading.Tasks.Task<string * string option> =
        task {
            let! oidc = appleConfigManager.GetConfigurationAsync(CancellationToken.None)
            let validationParams =
                TokenValidationParameters(
                    ValidateIssuer = true,
                    ValidIssuer = "https://appleid.apple.com",
                    ValidateAudience = true,
                    ValidAudience = config.AppleBundleId,
                    ValidateLifetime = true,
                    IssuerSigningKeys = oidc.SigningKeys,
                    ValidateIssuerSigningKey = true)

            let mutable validated = Unchecked.defaultof<SecurityToken>
            let principal = handler.ValidateToken(identityToken, validationParams, &validated)
            let sub =
                match claimValue principal JwtRegisteredClaimNames.Sub with
                | Some s -> s
                | None -> failwith "Apple token missing sub claim"
            return sub, claimValue principal JwtRegisteredClaimNames.Email
        }

    /// Mints a session JWT for our own app, with the userId as the subject.
    member _.IssueSessionToken(userId: string) : string * DateTimeOffset =
        let now = DateTimeOffset.UtcNow
        let expires = now.Add config.JwtLifetime
        let creds = SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
        let claims = [| Claim(JwtRegisteredClaimNames.Sub, userId) |]
        let token =
            JwtSecurityToken(
                issuer = config.JwtIssuer,
                audience = config.JwtAudience,
                claims = claims,
                notBefore = Nullable now.UtcDateTime,
                expires = Nullable expires.UtcDateTime,
                signingCredentials = creds)
        handler.WriteToken token, expires

    /// Validates a session JWT and returns the userId (subject), or None.
    member _.ValidateSessionToken(token: string) : string option =
        try
            let validationParams =
                TokenValidationParameters(
                    ValidateIssuer = true,
                    ValidIssuer = config.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = config.JwtAudience,
                    ValidateLifetime = true,
                    IssuerSigningKey = signingKey,
                    ValidateIssuerSigningKey = true)
            let mutable validated = Unchecked.defaultof<SecurityToken>
            let principal = handler.ValidateToken(token, validationParams, &validated)
            claimValue principal JwtRegisteredClaimNames.Sub
        with _ -> None

    /// Generates a new opaque CLI token and returns (plaintext, hash).
    /// The plaintext is shown to the user exactly once; only the hash is stored.
    member this.GenerateApiToken() : string * string =
        let bytes = RandomNumberGenerator.GetBytes 32
        let plaintext =
            "ntfy_" + Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=')
        plaintext, this.HashToken plaintext

    /// Stable SHA-256 hex hash of a token for storage/lookup.
    member _.HashToken(token: string) : string =
        use sha = SHA256.Create()
        sha.ComputeHash(Encoding.UTF8.GetBytes token)
        |> Array.map (fun b -> b.ToString("x2"))
        |> String.concat ""
