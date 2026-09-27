import AppKit
import SwiftUI

// Motion rules (mock spec): every animation carries state; reduced-motion users
// get the end states with no animation. Views hold no @State (no Xcode macros) —
// an AppearTracker ObservableObject flips once on appear; snapshots construct
// one already flipped to render the END state.
enum Fx {
    static var reduced: Bool {
        NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
    }
    // True while SnapshotRenderer runs: every tracker starts appeared so
    // shots capture the END state (offscreen cacheDisplay never fires the
    // onAppear animations that live views rely on).
    static var snapshot = false
    static func islandSpring(_ response: Double = 0.38, _ damping: Double = 0.72) -> Animation {
        .spring(response: response, dampingFraction: damping)
    }
}

@MainActor
final class AppearTracker: ObservableObject {
    @Published var appeared: Bool
    init(appeared: Bool = Fx.snapshot) { self.appeared = appeared }
    func appear(animated: Bool = true) {
        guard !appeared else { return }
        if animated, !Fx.reduced {
            withAnimation(.easeOut(duration: 0.25)) { appeared = true }
        } else {
            appeared = true
        }
    }
}
