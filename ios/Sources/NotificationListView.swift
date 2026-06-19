import SwiftUI

struct NotificationListView: View {
    @Environment(NotificationStore.self) private var store

    var body: some View {
        @Bindable var store = store
        NavigationStack {
            List {
                ForEach(store.items) { item in
                    NavigationLink(value: item) {
                        NotificationRow(item: item)
                    }
                }
                if !store.items.isEmpty {
                    HStack { Spacer(); ProgressView().onAppear { Task { await store.loadMore() } }; Spacer() }
                }
            }
            .overlay {
                if store.items.isEmpty && !store.isLoading {
                    ContentUnavailableView("No notifications yet",
                                           systemImage: "bell.slash",
                                           description: Text("Send one from a CLI script using your API token."))
                }
            }
            .navigationTitle("History")
            .navigationDestination(for: NotificationItem.self) { item in
                NotificationDetailView(item: item)
            }
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Picker("Filter", selection: $store.filter) {
                        ForEach(HistoryFilter.allCases) { Text($0.rawValue).tag($0) }
                    }
                    .pickerStyle(.menu)
                }
            }
            .onChange(of: store.filter) { Task { await store.refresh() } }
            .refreshable { await store.refresh() }
            .task { await store.refresh() }
        }
        .badge(store.unread)
    }
}

struct NotificationRow: View {
    let item: NotificationItem

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Circle()
                .fill(item.read ? Color.clear : Color.accentColor)
                .frame(width: 8, height: 8)
                .padding(.top, 6)
            VStack(alignment: .leading, spacing: 2) {
                Text(item.title.isEmpty ? "(no title)" : item.title)
                    .font(.headline)
                    .fontWeight(item.read ? .regular : .semibold)
                    .lineLimit(1)
                if !item.body.isEmpty {
                    Text(item.body).font(.subheadline).foregroundStyle(.secondary).lineLimit(2)
                }
                Text(item.createdAt, style: .relative)
                    .font(.caption).foregroundStyle(.tertiary)
            }
        }
        .padding(.vertical, 2)
    }
}
