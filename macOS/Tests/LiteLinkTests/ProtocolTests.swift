import XCTest
@testable import LiteLink
final class ProtocolTests: XCTestCase {
    func testButtonsAndReleases() {
        var state = ProtocolState()
        XCTAssertTrue(state.apply([13,1])); XCTAssertEqual(state.buttons,1)
        XCTAssertTrue(state.apply([12,1])); XCTAssertEqual(state.buttons,3)
        XCTAssertTrue(state.apply([13,0])); XCTAssertEqual(state.buttons,2)
    }
    func testSticksAndInvalidLength() {
        var state = ProtocolState()
        XCTAssertFalse(state.apply([18,1]))
        XCTAssertTrue(state.apply([18,0x7F,0xFF,0x80,0]))
        XCTAssertEqual(state.lx,32767); XCTAssertEqual(state.ly,32767)
        XCTAssertEqual(state.report.count,13)
        XCTAssertFalse(state.apply([0,1])); XCTAssertFalse(state.apply([11,1]))
    }
    func testHatTriggerAndNeutral() {
        var state = ProtocolState()
        XCTAssertEqual(state.report.last,8)
        _ = state.apply([1,1]); _ = state.apply([4,1])
        XCTAssertEqual(state.report.last,1)
        _ = state.apply([16,1]); XCTAssertEqual(state.lt,255)
        XCTAssertEqual(ProtocolState().report,Data([0,0,0,0,0,0,0,0,0,0,0,0,8]))
    }
}
