import SwiftUI

struct NotificationDetailView: View {
    @Environment(NotificationStore.self) private var store
    @State var item: NotificationItem

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text(item.title).font(.title2.bold())
                Text(item.createdAt, format: .dateTime.day().month().year().hour().minute())
                    .font(.caption).foregroundStyle(.secondary)
                if !item.body.isEmpty {
                    Text(item.body).font(.body)
                }
                if let data = item.data, !data.isEmpty, data != "{}" {
                    GroupBox("Data") {
                        Text(prettyJSON(data))
                            .font(.system(.footnote, design: .monospaced))
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                Spacer()
            }
            .padding()
        }
        .navigationTitle("Notification")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button(item.read ? "Mark unread" : "Mark read") {
                    Task {
                        await store.markRead(item, read: !item.read)
                        item.read.toggle()
                    }
                }
            }
        }
        .task {
            if !item.read {
                await store.markRead(item, read: true)
                item.read = true
            }
        }
    }

    private func prettyJSON(_ raw: String) -> String {
        guard let data = raw.data(using: .utf8),
              let obj = try? JSONSerialization.jsonObject(with: data),
              let pretty = try? JSONSerialization.data(withJSONObject: obj, options: [.prettyPrinted]),
              let str = String(data: pretty, encoding: .utf8) else { return raw }
        return str
    }
}
