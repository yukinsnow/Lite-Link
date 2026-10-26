import Foundation
import CoreHID

// Original generic gamepad descriptor: 16 buttons, 4 signed stick axes,
// 2 unsigned digital triggers, 8-way hat + null. Input report: 13 bytes.
let gamepadDescriptor: [UInt8] = [
    0x05,0x01, 0x09,0x05, 0xA1,0x01,
    0x05,0x09, 0x19,0x01, 0x29,0x10, 0x15,0x00, 0x25,0x01,
    0x75,0x01, 0x95,0x10, 0x81,0x02,
    0x05,0x01, 0x09,0x30, 0x09,0x31, 0x09,0x33, 0x09,0x34,
    0x16,0x00,0x80, 0x26,0xFF,0x7F, 0x75,0x10, 0x95,0x04, 0x81,0x02,
    0x09,0x32, 0x09,0x35, 0x15,0x00, 0x26,0xFF,0x00,
    0x75,0x08, 0x95,0x02, 0x81,0x02,
    0x09,0x39, 0x15,0x00, 0x25,0x07, 0x35,0x00, 0x46,0x3B,0x01,
    0x65,0x14, 0x75,0x04, 0x95,0x01, 0x81,0x42,
    0x75,0x04, 0x95,0x01, 0x81,0x03, 0xC0
]

final class PadDelegate: HIDVirtualDeviceDelegate {
    func hidVirtualDevice(_ device: HIDVirtualDevice, receivedSetReportRequestOfType type: HIDReportType,
                          id: HIDReportID?, data: Data) async throws { }
    func hidVirtualDevice(_ device: HIDVirtualDevice, receivedGetReportRequestOfType type: HIDReportType,
                          id: HIDReportID?, maxSize: Int) async throws -> Data {
        Data(ProtocolState().report.prefix(maxSize))
    }
}

enum PadError: LocalizedError {
    case entitlement
    var errorDescription: String? {
        localize("无法创建真正的虚拟手柄。需要 Apple 批准的 com.apple.developer.hid.virtual.device 权限及匹配的签名描述文件。辅助功能权限或普通临时签名不能替代。",
                 "Cannot create a real virtual gamepad. An Apple-approved com.apple.developer.hid.virtual.device entitlement and matching signing profile are required. Accessibility permission or ad-hoc signing cannot replace it.")
    }
}

final class VirtualGamepad {
    private let device: HIDVirtualDevice
    private let delegate = PadDelegate()
    private init(device: HIDVirtualDevice) { self.device = device }
    static func create() async throws -> VirtualGamepad {
        let properties = HIDVirtualDevice.Properties(descriptor: Data(gamepadDescriptor),
            vendorID: 0xFFFF, productID: 0x4C4C, transport: nil,
            product: "Lite Link Virtual Gamepad", manufacturer: "Yukinsnow",
            versionNumber: 0x0200, serialNumber: "LiteLink-1", uniqueID: "org.yukinsnow.litelink.gamepad")
        guard let device = HIDVirtualDevice(properties: properties) else { throw PadError.entitlement }
        let pad = VirtualGamepad(device: device)
        await device.activate(delegate: pad.delegate)
        return pad
    }
    func send(_ state: ProtocolState) async throws {
        try await device.dispatchInputReport(data: state.report, timestamp: .now)
    }
}
