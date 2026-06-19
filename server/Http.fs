namespace NotifyRelay

open System
open System.Net
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Azure.Functions.Worker.Http
open NotifyRelay.Domain

/// HTTP helpers: JSON (de)serialization, standard responses, and resolving the
/// authenticated caller from either a session JWT or an x-api-key.
module Http =

    let jsonOptions =
        let o = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        o.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        o

    let readJson<'T> (req: HttpRequestData) : Task<'T option> =
        task {
            try
                let! value = JsonSerializer.DeserializeAsync<'T>(req.Body, jsonOptions)
                return Some value
            with _ -> return None
        }

    let private respond (req: HttpRequestData) (status: HttpStatusCode) =
        req.CreateResponse status

    let json (req: HttpRequestData) (status: HttpStatusCode) (body: obj) : Task<HttpResponseData> =
        task {
            let res = respond req status
            res.Headers.Add("Content-Type", "application/json; charset=utf-8")
            let payload = JsonSerializer.Serialize(body, jsonOptions)
            do! res.WriteStringAsync payload
            return res
        }

    let empty (req: HttpRequestData) (status: HttpStatusCode) : Task<HttpResponseData> =
        task { return respond req status }

    let error (req: HttpRequestData) (status: HttpStatusCode) (message: string) : Task<HttpResponseData> =
        json req status {| error = message |}

    let private header (req: HttpRequestData) (name: string) : string option =
        match req.Headers.TryGetValues name with
        | true, values ->
            match Seq.tryHead values with
            | Some v when not (String.IsNullOrWhiteSpace v) -> Some v
            | _ -> None
        | _ -> None

    /// Resolves the caller from a Bearer session JWT (used by the app).
    let authenticateSession (auth: Auth) (req: HttpRequestData) : Caller option =
        header req "Authorization"
        |> Option.bind (fun h ->
            if h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) then
                Some(h.Substring(7).Trim())
            else
                None)
        |> Option.bind auth.ValidateSessionToken
        |> Option.map (fun userId -> { UserId = userId })

    /// Resolves the caller from an x-api-key header (used by CLI scripts).
    let authenticateApiKey (auth: Auth) (storage: Storage) (req: HttpRequestData) : Task<Caller option> =
        task {
            match header req "x-api-key" with
            | None -> return None
            | Some key ->
                let hash = auth.HashToken key
                let! tokenOpt = storage.FindTokenByHash hash
                match tokenOpt with
                | None -> return None
                | Some token ->
                    do! storage.TouchToken(token.UserId, token.TokenId)
                    return Some { UserId = token.UserId }
        }

    let queryParam (req: HttpRequestData) (name: string) : string option =
        let q = System.Web.HttpUtility.ParseQueryString req.Url.Query
        match q.[name] with
        | null -> None
        | v -> Some v
