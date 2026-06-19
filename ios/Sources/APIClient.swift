import Foundation

enum APIError: Error {
    case unauthorized
    case server(Int, String)
    case decoding(Error)
    case transport(Error)
}

/// Thin async wrapper over the relay HTTP API. Carries the session JWT.
actor APIClient {
    static let shared = APIClient()

    private let baseURL = AppConfig.apiBaseURL
    private let session = URLSession(configuration: .default)

    private lazy var decoder: JSONDecoder = {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .iso8601
        return d
    }()

    private lazy var encoder: JSONEncoder = {
        let e = JSONEncoder()
        e.dateEncodingStrategy = .iso8601
        return e
    }()

    // MARK: - Public API

    func exchangeApple(identityToken: String) async throws -> AuthResponse {
        try await request("auth/apple", method: "POST",
                          body: ["identityToken": identityToken], authed: false)
    }

    func registerDevice(apnsToken: String) async throws {
        let _: EmptyResponse = try await request("devices", method: "POST",
                                                 body: ["apnsToken": apnsToken, "platform": "ios"])
    }

    func listNotifications(read: Bool?, continuation: String?) async throws -> NotificationPage {
        var items: [URLQueryItem] = []
        if let read { items.append(URLQueryItem(name: "read", value: read ? "true" : "false")) }
        if let continuation { items.append(URLQueryItem(name: "continuation", value: continuation)) }
        return try await request("notifications", method: "GET", query: items)
    }

    func markRead(id: String, read: Bool) async throws {
        let _: EmptyResponse = try await request("notifications/\(id)", method: "PATCH",
                                                 body: ["read": read])
    }

    func unreadCount() async throws -> Int {
        let r: UnreadCount = try await request("notifications/unread-count", method: "GET")
        return r.count
    }

    func createToken(label: String) async throws -> CreatedToken {
        try await request("tokens", method: "POST", body: ["label": label])
    }

    func listTokens() async throws -> [ApiTokenInfo] {
        let r: TokenList = try await request("tokens", method: "GET")
        return r.tokens
    }

    func deleteToken(id: String) async throws {
        let _: EmptyResponse = try await request("tokens/\(id)", method: "DELETE")
    }

    func deleteAccount() async throws {
        let _: EmptyResponse = try await request("account", method: "DELETE")
    }

    // MARK: - Core request

    private func request<T: Decodable>(
        _ path: String,
        method: String,
        query: [URLQueryItem] = [],
        body: [String: Any]? = nil,
        authed: Bool = true
    ) async throws -> T {
        var components = URLComponents(url: baseURL.appendingPathComponent(path),
                                       resolvingAgainstBaseURL: false)!
        if !query.isEmpty { components.queryItems = query }

        var req = URLRequest(url: components.url!)
        req.httpMethod = method
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if let body {
            req.httpBody = try JSONSerialization.data(withJSONObject: body)
        }
        if authed {
            guard let token = Keychain.get("sessionToken") else { throw APIError.unauthorized }
            req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }

        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: req)
        } catch {
            throw APIError.transport(error)
        }

        guard let http = response as? HTTPURLResponse else {
            throw APIError.server(-1, "No HTTP response")
        }
        switch http.statusCode {
        case 200..<300:
            if data.isEmpty, let empty = EmptyResponse() as? T { return empty }
            do {
                return try decoder.decode(T.self, from: data)
            } catch {
                throw APIError.decoding(error)
            }
        case 401:
            throw APIError.unauthorized
        default:
            throw APIError.server(http.statusCode, String(data: data, encoding: .utf8) ?? "")
        }
    }
}

/// Placeholder for endpoints that return no JSON body.
struct EmptyResponse: Codable {}
