import XCTest
@testable import HomeEasyKit

final class ACStateTests: XCTestCase {
    // Real frames captured from CH-S12FTXN-E2wf on 2026-11-15.
    let heat21 = [UInt8](hex: "AAAA12000A0A000C0500D40000000019050000007D")!

    func testDecodesRealStatusFrame() {
        let s = ACState(frame: heat21)!
        XCTAssertTrue(s.power)
        XCTAssertEqual(s.mode, .heat)
        XCTAssertEqual(s.setpoint, 21)
        XCTAssertEqual(s.indoor, 25.5, accuracy: 0.01)
        XCTAssertEqual(s.fan, .auto)
        XCTAssertTrue(s.display)
        XCTAssertTrue(s.aux)
        XCTAssertFalse(s.eco)
    }

    func testSetpointCommandMatchesRealCapture() {
        var s = ACState(frame: heat21)!
        s.setpoint = 22
        XCTAssertEqual(s.command.hex, "AAAA12010A0A000C0600D40000000019050000007F")
    }

    func testOffTimerCommandMatchesRealCapture() {
        var s = ACState(frame: [UInt8](hex: "AAAA12000A0A000C0501D40000003C1905000000BA")!)!
        s.setOffTimer(minutes: 2)
        XCTAssertEqual(s.command.hex, "AAAA12010A0A000C0501F48000023C19050000005D")
        s.setOffTimer(minutes: nil)
        XCTAssertFalse(s.offTimerEnabled)
        XCTAssertEqual(s.offTimerMinutes, 0)
    }

    func testFanAndSwingRoundTrip() {
        var s = ACState(frame: heat21)!
        s.fan = .turbo
        s.verticalSwing = .swing
        s.horizontalSwing = .swingWide
        s.mode = .cool
        let back = ACState(frame: s.command)!
        XCTAssertEqual(back.fan, .turbo)
        XCTAssertEqual(back.verticalSwing, .swing)
        XCTAssertEqual(back.horizontalSwing, .swingWide)
        XCTAssertEqual(back.mode, .cool)
        XCTAssertEqual(back.setpoint, 21)
    }

    func testFrameExtractionSkipsGarbage() {
        let buffer: [UInt8] = [0x00, 0x13] + heat21 + [0x01]
        XCTAssertEqual(ACClient.extractFrame(buffer), heat21)
        XCTAssertNil(ACClient.extractFrame(Array(heat21.prefix(20))))
    }
}
