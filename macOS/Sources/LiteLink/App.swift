import SwiftUI

@MainActor final class ReceiverModel: ObservableObject {
    @Published var adapters = Adapter.available()
    @Published var selected = ""
    @Published var running = false
    @Published var busy = false
    @Published var status = localize("实验性 macOS 原生 HID 后端，需要 Apple 专用签名权限。", "Experimental native macOS HID backend. Requires Apple's restricted signing entitlement.")
    private var receiver: UDPReceiver?
    init() { if adapters.count == 1 { selected = adapters[0].id } }
    func refresh() {
        let old = selected; adapters = Adapter.available()
        selected = adapters.contains(where: { $0.id == old }) ? old : adapters.count == 1 ? adapters[0].id : ""
    }
    func toggle() {
        guard !busy else { return }
        busy = true
        Task {
            if running {
                await receiver?.stop(); receiver = nil; running = false
                status = localize("服务已停止，手柄输入已归零。", "Server stopped. Controller input released.")
            } else if let adapter = adapters.first(where: { $0.id == selected }) {
                let server = UDPReceiver { [weak self] text in Task { @MainActor in self?.status = text } }
                do { try await server.start(adapter: adapter); receiver = server; running = true }
                catch { status = error.localizedDescription; running = false }
            }
            busy = false
        }
    }
}

@main @MainActor struct LiteLinkApp: App {
    @StateObject private var model = ReceiverModel()
    var body: some Scene {
        WindowGroup("Lite Link") {
            VStack(alignment: .leading, spacing: 20) {
                HStack {
                    VStack(alignment: .leading) { Text("LITE / LINK").font(.largeTitle.weight(.semibold)); Text(localize("真正的虚拟 HID 手柄 · macOS", "Native virtual HID gamepad · macOS")).foregroundStyle(.secondary) }
                    Spacer(); Link("GitHub @Yukinsnow", destination: URL(string:"https://github.com/Yukinsnow")!)
                }
                Picker(localize("网络接口 / IPv4", "Network adapter / IPv4"), selection: $model.selected) {
                    Text(localize("请选择网络接口", "Select an adapter")).tag("")
                    ForEach(model.adapters) { Text("\($0.name) — \($0.ip)").tag($0.id) }
                }.disabled(model.running || model.busy)
                HStack {
                    Button(localize("刷新", "Refresh")) { model.refresh() }.disabled(model.running || model.busy)
                    Spacer()
                    Button(model.running ? localize("停止服务", "Stop server") : localize("创建手柄并启动", "Create gamepad & start")) { model.toggle() }
                        .disabled(model.busy || (!model.running && model.selected.isEmpty))
                }
                Text(model.status).textSelection(.enabled).frame(maxWidth: .infinity, minHeight: 110, alignment: .topLeading)
                Text(localize("不会使用键鼠映射作为替代。未获 Apple 虚拟 HID 授权时，不建立虚假的网络连接。兼容性取决于游戏对通用 HID 手柄的支持。", "No keyboard/mouse fallback. Without Apple's virtual HID authorization, no misleading network session is started. Game compatibility depends on generic HID gamepad support."))
                    .font(.footnote).foregroundStyle(.secondary)
            }.padding(28).frame(width: 690)
        }.windowResizability(.contentSize)
    }
}
