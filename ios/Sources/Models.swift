import Foundation

/// A notification as returned by GET /notifications.
struct NotificationItem: Identifiable, Codable, Hashable {
    let id: String
    let title: String
    let body: String
    let data: String?     // raw JSON string, if any
    let priority: String
    var read: Bool
    let createdAt: Date
    var readAt: Date?
}

struct NotificationPage: Codable {
    let items: [NotificationItem]
    let continuation: String?
}

struct AuthResponse: Codable {
    let token: String
    let expiresAt: Date
    let userId: String
}

struct CreatedToken: Codable {
    let tokenId: String
    let label: String
    let token: String   // plaintext, shown once
}

struct ApiTokenInfo: Identifiable, Codable, Hashable {
    var id: String { tokenId }
    let tokenId: String
    let label: String
    let createdAt: Date
    let lastUsed: Date?
}

struct TokenList: Codable {
    let tokens: [ApiTokenInfo]
}

struct UnreadCount: Codable {
    let count: Int
}
