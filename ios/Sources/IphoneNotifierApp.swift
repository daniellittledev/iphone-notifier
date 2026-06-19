import SwiftUI
import UIKit
import UserNotifications

@main
struct IphoneNotifierApp: App {
    @UIApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @State private var auth = AuthManager()
    @State private var store = NotificationStore()

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(auth)
                .environment(store)
                .task {
                    if auth.isAuthenticated {
                        await PushRegistrar.shared.requestAndRegister()
                        await store.refresh()
                    }
                }
        }
    }
}

/// Handles APNs callbacks and foreground notification presentation.
final class AppDelegate: NSObject, UIApplicationDelegate, UNUserNotificationCenterDelegate {
    func application(_ application: UIApplication,
                     didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil) -> Bool {
        UNUserNotificationCenter.current().delegate = self
        return true
    }

    func application(_ application: UIApplication,
                     didRegisterForRemoteNotificationsWithDeviceToken deviceToken: Data) {
        Task { @MainActor in PushRegistrar.shared.didRegister(deviceToken: deviceToken) }
    }

    func application(_ application: UIApplication,
                     didFailToRegisterForRemoteNotificationsWithError error: Error) {
        // Surface in logs; non-fatal.
        print("APNs registration failed: \(error)")
    }

    // Show banners while the app is in the foreground.
    func userNotificationCenter(_ center: UNUserNotificationCenter,
                                willPresent notification: UNNotification) async
        -> UNNotificationPresentationOptions {
        [.banner, .badge, .sound]
    }
}
