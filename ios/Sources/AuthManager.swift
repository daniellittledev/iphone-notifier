import Foundation
import Observation

/// Holds session state and bridges Sign in with Apple to the relay.
@MainActor
@Observable
final class AuthManager {
    var isAuthenticated: Bool = Keychain.get("sessionToken") != nil
    var userId: String? = Keychain.get("userId")
    var lastError: String?

    /// Exchanges an Apple identity token for our session JWT and stores it.
    func signIn(identityToken: String) async {
        do {
            let auth = try await APIClient.shared.exchangeApple(identityToken: identityToken)
            Keychain.set(auth.token, for: "sessionToken")
            Keychain.set(auth.userId, for: "userId")
            userId = auth.userId
            isAuthenticated = true
            lastError = nil
            // Ask for push permission + register the device now that we're signed in.
            await PushRegistrar.shared.requestAndRegister()
        } catch {
            lastError = "Sign-in failed: \(error)"
            isAuthenticated = false
        }
    }

    func signOut() {
        Keychain.delete("sessionToken")
        Keychain.delete("userId")
        isAuthenticated = false
        userId = nil
    }

    func deleteAccount() async {
        do {
            try await APIClient.shared.deleteAccount()
            signOut()
        } catch {
            lastError = "Delete failed: \(error)"
        }
    }
}
