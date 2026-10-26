import Foundation

struct ProtocolState {
    var buttons: UInt16 = 0
    var directions: UInt8 = 0
    var lx: Int16 = 0, ly: Int16 = 0, rx: Int16 = 0, ry: Int16 = 0
    var lt: UInt8 = 0, rt: UInt8 = 0

    mutating func apply(_ bytes: [UInt8]) -> Bool {
        guard let code = bytes.first else { return false }
        if code == 18 || code == 19 {
            guard bytes.count == 5 else { return false }
            let x = Int16(bitPattern: UInt16(bytes[1]) << 8 | UInt16(bytes[2]))
            let y = Int16(bitPattern: UInt16(bytes[3]) << 8 | UInt16(bytes[4]))
            // Switch Y points up; HID Y points down. Saturate -32768 safely.
            let hidY = Int16(clamping: -Int32(y))
            if code == 18 { lx = x; ly = hidY } else { rx = x; ry = hidY }
            return true
        }
        guard bytes.count == 2 else { return false }
        let pressed = bytes[1] != 0
        if (1...4).contains(code) {
            let bit = UInt8(1) << (code - 1)
            if pressed { directions |= bit } else { directions &= ~bit }
            return true
        }
        if code == 16 { lt = pressed ? 255 : 0; return true }
        if code == 17 { rt = pressed ? 255 : 0; return true }
        let mapping: [UInt8: Int] = [5:6, 6:7, 7:8, 8:9, 9:4, 10:5, 12:1, 13:0, 14:3, 15:2]
        guard let index = mapping[code] else { return false }
        let bit = UInt16(1) << index
        if pressed { buttons |= bit } else { buttons &= ~bit }
        return true
    }

    var report: Data {
        var bytes: [UInt8] = []
        for value in [buttons, UInt16(bitPattern: lx), UInt16(bitPattern: ly), UInt16(bitPattern: rx), UInt16(bitPattern: ry)] {
            bytes += [UInt8(value & 255), UInt8(value >> 8)]
        }
        let x = (directions & 8 != 0 ? 1 : 0) - (directions & 4 != 0 ? 1 : 0)
        let y = (directions & 2 != 0 ? 1 : 0) - (directions & 1 != 0 ? 1 : 0)
        let hats = ["0,-1":0, "1,-1":1, "1,0":2, "1,1":3, "0,1":4, "-1,1":5, "-1,0":6, "-1,-1":7]
        bytes += [lt, rt, UInt8(hats["\(x),\(y)"] ?? 8)]
        return Data(bytes)
    }
}

func localize(_ zh: String, _ en: String) -> String {
    Locale.preferredLanguages.first?.lowercased().hasPrefix("zh") == true ? zh : en
}
