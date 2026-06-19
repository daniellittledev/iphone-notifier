import Foundation

/// Reads the relay API base URL from Info.plist (NotifierApiBaseUrl).
enum AppConfig {
    static var apiBaseURL: URL {
        let raw = (Bundle.main.object(forInfoDictionaryKey: "NotifierApiBaseUrl") as? String) ?? ""
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, let url = URL(string: trimmed) else {
            fatalError("NotifierApiBaseUrl is not set. Configure NTFY_API_BASE_URL in build settings.")
        }
        return url
    }
}
