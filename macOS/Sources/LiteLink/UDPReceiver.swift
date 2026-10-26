import Foundation
import Darwin

struct Adapter: Identifiable {
    var name: String
    var address: UInt32
    var mask: UInt32
    var id: String { "\(name)-\(address)" }
    var ip: String { ipString(address) }
    static func available() -> [Adapter] {
        var list: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&list) == 0 else { return [] }
        defer { freeifaddrs(list) }
        var items: [Adapter] = [], current = list
        while let entry = current {
            defer { current = entry.pointee.ifa_next }
            let value = entry.pointee
            guard value.ifa_flags & UInt32(IFF_UP) != 0, value.ifa_flags & UInt32(IFF_LOOPBACK) == 0,
                  let address = value.ifa_addr, address.pointee.sa_family == UInt8(AF_INET), let mask = value.ifa_netmask else { continue }
            let ip = address.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee.sin_addr.s_addr }
            let netmask = mask.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee.sin_addr.s_addr }
            items.append(Adapter(name: String(cString: value.ifa_name), address: ip, mask: netmask))
        }
        return items.sorted { $0.id < $1.id }
    }
}
func ipString(_ value: UInt32) -> String {
    var address = in_addr(s_addr: value), text = [CChar](repeating: 0, count: 16)
    guard inet_ntop(AF_INET, &address, &text, 16) != nil else { return "?" }
    return String(cString: text)
}
private final class SharedState {
    private let lock = NSLock()
    private var active = false
    private var state = ProtocolState()
    private var lastInput = Date.distantPast
    var running: Bool {
        get { lock.lock(); defer { lock.unlock() }; return active }
        set { lock.lock(); active = newValue; lock.unlock() }
    }
    func reset() { lock.lock(); state = ProtocolState(); lastInput = .distantPast; lock.unlock() }
    func apply(_ packet: [UInt8]) {
        lock.lock(); defer { lock.unlock() }
        if state.apply(packet) { lastInput = Date() }
    }
    func snapshot() -> ProtocolState {
        lock.lock(); defer { lock.unlock() }
        if Date().timeIntervalSince(lastInput) > 0.5 { state = ProtocolState() }
        return state
    }
}

final class UDPReceiver {
    private let shared = SharedState()
    private let group = DispatchGroup()
    private var sender: Task<Void, Never>?
    private var pad: VirtualGamepad?
    private var inputFD: Int32 = -1, discoveryFD: Int32 = -1
    private let status: @Sendable (String) -> Void
    init(status: @escaping @Sendable (String) -> Void) { self.status = status }

    private func makeSocket(address: UInt32, port: UInt16) throws -> Int32 {
        let fd = Darwin.socket(AF_INET, SOCK_DGRAM, 0)
        guard fd >= 0 else { throw POSIXError(.ENOBUFS) }
        var endpoint = sockaddr_in()
        endpoint.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        endpoint.sin_family = sa_family_t(AF_INET)
        endpoint.sin_addr.s_addr = address; endpoint.sin_port = port.bigEndian
        let bound = withUnsafePointer(to: &endpoint) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { Darwin.bind(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) }
        }
        guard bound == 0 else { let code = errno; Darwin.close(fd); throw POSIXError(POSIXErrorCode(rawValue: code) ?? .EADDRNOTAVAIL) }
        return fd
    }
    func start(adapter: Adapter) async throws {
        guard !shared.running else { return }
        let gamepad = try await VirtualGamepad.create() // Fail before opening a network session.
        do {
            inputFD = try makeSocket(address: adapter.address, port: 8192)
            discoveryFD = try makeSocket(address: INADDR_ANY, port: 8193)
        } catch {
            if inputFD >= 0 { Darwin.close(inputFD); inputFD = -1 }
            throw error
        }
        pad = gamepad; shared.reset(); shared.running = true
        status(localize("虚拟 HID 手柄已创建。等待 Switch 连接…", "Virtual HID gamepad created. Waiting for the Switch..."))
        group.enter()
        DispatchQueue.global(qos: .userInteractive).async { [self] in
            receiveLoop(adapter: adapter)
            group.leave()
        }
        sender = Task { [self] in
            while !Task.isCancelled && shared.running {
                do { try await gamepad.send(shared.snapshot()) }
                catch {
                    shared.running = false
                    status(localize("HID 报告发送失败：", "HID report failed: ") + error.localizedDescription)
                    break
                }
                try? await Task.sleep(nanoseconds: 16_666_667)
            }
        }
    }
    func stop() async {
        shared.running = false
        sender?.cancel()
        await sender?.value
        group.wait()
        if let pad { try? await pad.send(ProtocolState()) }
        if inputFD >= 0 { Darwin.close(inputFD); inputFD = -1 }
        if discoveryFD >= 0 { Darwin.close(discoveryFD); discoveryFD = -1 }
        sender = nil; pad = nil; shared.reset()
    }
    private func receive(_ fd: Int32) -> ([UInt8], sockaddr_in)? {
        var bytes = [UInt8](repeating: 0, count: 64), peer = sockaddr_in()
        var length = socklen_t(MemoryLayout<sockaddr_in>.size)
        let count = withUnsafeMutablePointer(to: &peer) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { address in
                bytes.withUnsafeMutableBytes { Darwin.recvfrom(fd, $0.baseAddress, $0.count, 0, address, &length) }
            }
        }
        return count >= 0 ? (Array(bytes.prefix(count)), peer) : nil
    }
    private func reply(_ destination: sockaddr_in) {
        let bytes: [UInt8] = [120,98,111,120,0]
        var peer = destination
        _ = withUnsafePointer(to: &peer) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { address in
                bytes.withUnsafeBytes { Darwin.sendto(inputFD, $0.baseAddress, $0.count, 0, address, socklen_t(MemoryLayout<sockaddr_in>.size)) }
            }
        }
    }
    private func receiveLoop(adapter: Adapter) {
        var peer: sockaddr_in?
        var sockets = [pollfd(fd: inputFD, events: Int16(POLLIN), revents: 0), pollfd(fd: discoveryFD, events: Int16(POLLIN), revents: 0)]
        let socketCount = nfds_t(sockets.count)
        while shared.running {
            guard Darwin.poll(&sockets, socketCount, 50) > 0 else { continue }
            for index in sockets.indices where sockets[index].revents & Int16(POLLIN) != 0 {
                guard let (packet, source) = receive(sockets[index].fd) else { continue }
                let query = Array(packet.prefix { $0 != 0 }) == Array("xbox_switch".utf8)
                if index == 1 {
                    if query && adapter.mask != 0 && (source.sin_addr.s_addr & adapter.mask) == (adapter.address & adapter.mask) { reply(source) }
                    continue
                }
                if query {
                    if peer == nil || peer?.sin_addr.s_addr == source.sin_addr.s_addr {
                        peer = source; shared.reset(); reply(source)
                        status(localize("已连接 Switch：", "Switch connected: ") + ipString(source.sin_addr.s_addr))
                    }
                } else if peer?.sin_addr.s_addr == source.sin_addr.s_addr && peer?.sin_port == source.sin_port {
                    shared.apply(packet)
                }
            }
        }
    }
}
