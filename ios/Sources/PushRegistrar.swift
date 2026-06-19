import Foundation
import UIKit
import UserNotifications

/// Coordinates APNs permission, device-token capture, and registration with
/// the relay. The AppDelegate forwards the raw token here.
@MainActor
final class PushRegistrar: NSObject {
    static let shared = PushRegistrar()

    private var pendingToken: String?

    /// Requests notification authorization and triggers APNs registration.
    func requestAndRegister() async {
        let center = UNUserNotificationCenter.current()
        do {
            let granted = try await center.requestAuthorization(options: [.alert, .badge, .sound])
            guard granted else { return }
            UIApplication.shared.registerForRemoteNotifications()
        } catch {
            // Permission denied or error; nothing to register.
        }
    }

    /// Called by the AppDelegate when APNs returns a device token.
    func didRegister(deviceToken: Data) {
        let token = deviceToken.map { String(format: "%02x", $0) }.joined()
        pendingToken = token
        Task { await sendToken(token) }
    }

    /// Re-send the last known token (e.g. after sign-in completes).
    func resendIfNeeded() async {
        if let token = pendingToken { await sendToken(token) }
    }

    private func sendToken(_ token: String) async {
        guard Keychain.get("sessionToken") != nil else { return }
        do { try await APIClient.shared.registerDevice(apnsToken: token) }
        catch { /* will retry on next launch/sign-in */ }
    }
}
