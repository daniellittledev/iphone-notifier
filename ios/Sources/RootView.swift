import SwiftUI
import AuthenticationServices

/// Switches between the sign-in screen and the main app based on auth state.
struct RootView: View {
    @Environment(AuthManager.self) private var auth

    var body: some View {
        if auth.isAuthenticated {
            MainTabView()
        } else {
            SignInView()
        }
    }
}

struct SignInView: View {
    @Environment(AuthManager.self) private var auth

    var body: some View {
        VStack(spacing: 24) {
            Spacer()
            Image(systemName: "bell.badge.fill")
                .font(.system(size: 64))
                .foregroundStyle(.tint)
            Text("Notifier")
                .font(.largeTitle.bold())
            Text("Push notifications to your phone from a simple web request.")
                .multilineTextAlignment(.center)
                .foregroundStyle(.secondary)
                .padding(.horizontal)
            Spacer()
            SignInWithAppleButton(.signIn) { request in
                request.requestedScopes = [.email]
            } onCompletion: { result in
                handle(result)
            }
            .signInWithAppleButtonStyle(.black)
            .frame(height: 50)
            .padding(.horizontal)

            if let err = auth.lastError {
                Text(err).font(.footnote).foregroundStyle(.red)
            }
            Spacer().frame(height: 24)
        }
        .padding()
    }

    private func handle(_ result: Result<ASAuthorization, Error>) {
        switch result {
        case .success(let authorization):
            guard let credential = authorization.credential as? ASAuthorizationAppleIDCredential,
                  let tokenData = credential.identityToken,
                  let token = String(data: tokenData, encoding: .utf8) else {
                auth.lastError = "No identity token returned"
                return
            }
            Task { await auth.signIn(identityToken: token) }
        case .failure(let error):
            auth.lastError = "\(error.localizedDescription)"
        }
    }
}

struct MainTabView: View {
    var body: some View {
        TabView {
            NotificationListView()
                .tabItem { Label("History", systemImage: "bell") }
            SettingsView()
                .tabItem { Label("Settings", systemImage: "gear") }
        }
    }
}
