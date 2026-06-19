import SwiftUI
import UIKit

struct SettingsView: View {
    @Environment(AuthManager.self) private var auth
    @State private var tokens: [ApiTokenInfo] = []
    @State private var newTokenLabel = ""
    @State private var createdToken: CreatedToken?
    @State private var showDeleteAccount = false
    @State private var error: String?

    var body: some View {
        NavigationStack {
            Form {
                Section("CLI API Tokens") {
                    ForEach(tokens) { token in
                        VStack(alignment: .leading) {
                            Text(token.label).font(.headline)
                            Text("Created \(token.createdAt, style: .date)")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                    }
                    .onDelete(perform: deleteTokens)

                    HStack {
                        TextField("New token label", text: $newTokenLabel)
                        Button("Create") { Task { await createToken() } }
                            .disabled(newTokenLabel.trimmingCharacters(in: .whitespaces).isEmpty)
                    }
                }

                Section("Account") {
                    Button("Sign Out") { auth.signOut() }
                    Button("Delete Account", role: .destructive) { showDeleteAccount = true }
                }

                if let error {
                    Section { Text(error).foregroundStyle(.red).font(.footnote) }
                }
            }
            .navigationTitle("Settings")
            .task { await loadTokens() }
            .alert("New token", isPresented: Binding(get: { createdToken != nil },
                                                     set: { if !$0 { createdToken = nil } })) {
                Button("Copy") {
                    UIPasteboard.general.string = createdToken?.token
                }
                Button("Done", role: .cancel) {}
            } message: {
                Text("Copy this token now — it is shown only once:\n\n\(createdToken?.token ?? "")")
            }
            .confirmationDialog("Delete your account and all history?",
                                isPresented: $showDeleteAccount, titleVisibility: .visible) {
                Button("Delete Everything", role: .destructive) {
                    Task { await auth.deleteAccount() }
                }
                Button("Cancel", role: .cancel) {}
            }
        }
    }

    private func loadTokens() async {
        do { tokens = try await APIClient.shared.listTokens() }
        catch { self.error = "\(error)" }
    }

    private func createToken() async {
        do {
            let created = try await APIClient.shared.createToken(label: newTokenLabel)
            createdToken = created
            newTokenLabel = ""
            await loadTokens()
        } catch { self.error = "\(error)" }
    }

    private func deleteTokens(at offsets: IndexSet) {
        let ids = offsets.map { tokens[$0].tokenId }
        Task {
            for id in ids { try? await APIClient.shared.deleteToken(id: id) }
            await loadTokens()
        }
    }
}
