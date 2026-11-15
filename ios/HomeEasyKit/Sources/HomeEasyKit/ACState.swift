import Foundation

public enum ACMode: Int, CaseIterable, Identifiable {
    case auto = 0, cool, dry, fan, heat
    public var id: Int { rawValue }
    public var title: String { ["Auto", "Cool", "Dry", "Fan", "Heat"][rawValue] }
}

public enum FanSetting: Int, CaseIterable, Identifiable {
    case auto = 0, l1, l2, l3, l4, l5, l6, quiet, turbo
    public var id: Int { rawValue }
    public var title: String {
        switch self {
        case .auto: return "Auto"
        case .quiet: return "Quiet"
        case .turbo: return "Turbo"
        default: return "Speed \(rawValue)"
        }
    }
}

public enum VerticalSwing: Int, CaseIterable, Identifiable {
    case stop = 0, swing, top, topCenter, center, bottomCenter, bottom
    public var id: Int { rawValue }
    public var title: String { ["Stop", "Swing", "Top", "Top-center", "Center", "Bottom-center", "Bottom"][rawValue] }
}

public enum HorizontalSwing: Int, CaseIterable, Identifiable {
    case stop = 0, swing = 1, left = 2, leftCenter = 3, center = 4, rightCenter = 5, right = 6, leftRight = 12, swingWide = 13
    public var id: Int { rawValue }
    public var title: String {
        switch self {
        case .stop: return "Stop"
        case .swing: return "Swing"
        case .left: return "Left"
        case .leftCenter: return "Left-center"
        case .center: return "Center"
        case .rightCenter: return "Right-center"
        case .right: return "Right"
        case .leftRight: return "Left & right"
        case .swingWide: return "Wide swing"
        }
    }
}

/// 21-byte HomeEasy state frame. Payload byte N is at frame offset N+4; bit 0 is the MSB.
public struct ACState: Equatable {
    public static let frameLength = 21
    public static let statusRequest: [UInt8] =
        [0xAA, 0xAA, 0x12, 0xA0, 0x0A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x1A]

    public private(set) var raw: [UInt8]

    public init?(frame: [UInt8]) {
        guard frame.count == Self.frameLength, frame[0] == 0xAA, frame[1] == 0xAA else { return nil }
        raw = frame
    }

    public static func checksum(_ frame: [UInt8]) -> UInt8 {
        UInt8(truncatingIfNeeded: frame.prefix(frameLength - 1).reduce(0) { $0 + Int($1) })
    }

    public var command: [UInt8] {
        var c = raw
        c[3] = 0x01
        c[Self.frameLength - 1] = Self.checksum(c)
        return c
    }

    private func bits(_ byte: Int, _ bit: Int, _ count: Int = 1) -> Int {
        var v = 0
        for i in 0..<count {
            let pos = (byte + 4) * 8 + bit + i
            v = (v << 1) | Int((raw[pos / 8] >> (7 - pos % 8)) & 1)
        }
        return v
    }

    private mutating func setBits(_ byte: Int, _ bit: Int, _ count: Int, _ value: Int) {
        for i in 0..<count {
            let pos = (byte + 4) * 8 + bit + i
            let mask = UInt8(1 << (7 - pos % 8))
            if (value >> (count - 1 - i)) & 1 == 1 { raw[pos / 8] |= mask } else { raw[pos / 8] &= ~mask }
        }
    }

    private mutating func setFlag(_ byte: Int, _ bit: Int, _ on: Bool) { setBits(byte, bit, 1, on ? 1 : 0) }

    public var power: Bool { get { bits(3, 4) == 1 } set { setFlag(3, 4, newValue) } }
    public var mode: ACMode { get { ACMode(rawValue: bits(3, 5, 3)) ?? .auto } set { setBits(3, 5, 3, newValue.rawValue) } }
    public var fanSpeed: Int { get { bits(3, 1, 3) } set { setBits(3, 1, 3, min(max(newValue, 0), 6)) } }
    public var turbo: Bool { get { bits(3, 0) == 1 } set { setFlag(3, 0, newValue) } }
    public var quiet: Bool { get { bits(4, 1) == 1 } set { setFlag(4, 1, newValue) } }
    public var fahrenheit: Bool { bits(4, 2) == 1 }
    public var horizontalSwing: HorizontalSwing {
        get { HorizontalSwing(rawValue: bits(5, 0, 4)) ?? .stop } set { setBits(5, 0, 4, newValue.rawValue) }
    }
    public var verticalSwing: VerticalSwing {
        get { VerticalSwing(rawValue: bits(5, 4, 4)) ?? .stop } set { setBits(5, 4, 4, newValue.rawValue) }
    }
    public var display: Bool { get { bits(6, 0) == 1 } set { setFlag(6, 0, newValue) } }
    public var health: Bool { get { bits(6, 1) == 1 } set { setFlag(6, 1, newValue) } }
    /// Auxiliary heater in heat mode, drying in cool/dry mode.
    public var aux: Bool { get { bits(6, 3) == 1 } set { setFlag(6, 3, newValue) } }
    public var sleep: Bool { get { bits(6, 6) == 1 } set { setFlag(6, 6, newValue) } }
    public var eco: Bool { get { bits(6, 7) == 1 } set { setFlag(6, 7, newValue) } }
    public var timerFlag: Bool { get { bits(6, 2) == 1 } set { setFlag(6, 2, newValue) } }
    public var offTimerEnabled: Bool { get { bits(7, 0) == 1 } set { setFlag(7, 0, newValue) } }

    /// Minutes until the AC switches itself off; the AC counts this down once enabled. Clamped to 0...1440.
    public var offTimerMinutes: Int {
        get { (bits(7, 1, 3) << 8) | bits(9, 0, 8) }
        set {
            let m = min(max(newValue, 0), 1440)
            setBits(7, 1, 3, m >> 8)
            setBits(9, 0, 8, m & 0xFF)
        }
    }

    /// Starts the AC's built-in off timer, or cancels it when `minutes` is nil.
    public mutating func setOffTimer(minutes: Int?) {
        let m = minutes ?? 0
        offTimerMinutes = m
        offTimerEnabled = m > 0
        timerFlag = m > 0
    }

    /// Target temperature in °C, clamped to 16...30.
    public var setpoint: Int {
        get {
            var v = bits(4, 3, 5)
            if v >= 16 { v -= 16 }
            return v + 16
        }
        set { setBits(4, 3, 5, min(max(newValue, 16), 30) - 16) }
    }

    public var indoor: Double { Double(bits(11, 0, 8)) + Double(bits(12, 0, 8)) / 10 }

    public var fan: FanSetting {
        get { turbo ? .turbo : quiet ? .quiet : (FanSetting(rawValue: fanSpeed) ?? .auto) }
        set {
            turbo = newValue == .turbo
            quiet = newValue == .quiet
            if newValue != .turbo && newValue != .quiet { fanSpeed = newValue.rawValue }
        }
    }
}

public extension Array where Element == UInt8 {
    init?(hex: String) {
        let chars = Array(hex)
        guard chars.count % 2 == 0 else { return nil }
        var out: [UInt8] = []
        for i in stride(from: 0, to: chars.count, by: 2) {
            guard let b = UInt8(String(chars[i...i + 1]), radix: 16) else { return nil }
            out.append(b)
        }
        self = out
    }

    var hex: String { map { String(format: "%02X", $0) }.joined() }
}
