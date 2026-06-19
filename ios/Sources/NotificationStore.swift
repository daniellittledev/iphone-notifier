import Foundation
import Observation
import UIKit

enum HistoryFilter: String, CaseIterable, Identifiable {
    case all = "All"
    case unread = "Unread"
    case read = "Read"
    var id: String { rawValue }

    var readParam: Bool? {
        switch self {
        case .all: return nil
        case .unread: return false
        case .read: return true
        }
    }
}

/// Source-of-truth-backed history. The server is authoritative; this caches the
/// last fetch to disk so the list shows instantly and survives relaunch offline.
@MainActor
@Observable
final class NotificationStore {
    var items: [NotificationItem] = []
    var filter: HistoryFilter = .all
    var unread: Int = 0
    var isLoading = false
    var errorMessage: String?

    private var continuation: String?
    private let cacheURL: URL = {
        let dir = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
        return dir.appendingPathComponent("notifications.json")
    }()

    init() { loadCache() }

    func refresh() async {
        isLoading = true
        defer { isLoading = false }
        do {
            let page = try await APIClient.shared.listNotifications(read: filter.readParam, continuation: nil)
            items = page.items
            continuation = page.continuation
            unread = try await APIClient.shared.unreadCount()
            await updateBadge()
            saveCache()
            errorMessage = nil
        } catch {
            errorMessage = "\(error)"
        }
    }

    func loadMore() async {
        guard let continuation else { return }
        do {
            let page = try await APIClient.shared.listNotifications(read: filter.readParam, continuation: continuation)
            items.append(contentsOf: page.items)
            self.continuation = page.continuation
        } catch {
            errorMessage = "\(error)"
        }
    }

    func markRead(_ item: NotificationItem, read: Bool) async {
        do {
            try await APIClient.shared.markRead(id: item.id, read: read)
            if let idx = items.firstIndex(where: { $0.id == item.id }) {
                items[idx].read = read
            }
            unread = try await APIClient.shared.unreadCount()
            await updateBadge()
            saveCache()
        } catch {
            errorMessage = "\(error)"
        }
    }

    private func updateBadge() async {
        try? await UNUserNotificationCenter.current().setBadgeCount(unread)
    }

    // MARK: - Disk cache

    private func saveCache() {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        if let data = try? encoder.encode(items) {
            try? data.write(to: cacheURL, options: .atomic)
        }
    }

    private func loadCache() {
        guard let data = try? Data(contentsOf: cacheURL) else { return }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        if let cached = try? decoder.decode([NotificationItem].self, from: data) {
            items = cached
        }
    }
}
