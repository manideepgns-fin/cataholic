import AVFoundation
import Foundation

// CrewSound — the cat's voice (owner 27 Sep 2026: "can we make the cat do sounds?" → only when you interact).
// Real recordings from BigSoundBank, all CC0 (Resources/Sounds/CREDITS.md) — picked for low noise. The cat is
// silent on its own: sounds play only from a click, a right-click emote, a double-click or a drag — never from
// the random life actions. Silent in Quiet mode, with "Cat sounds" switched off (right-click menu), and in
// snapshot runs.
@MainActor
enum CrewSound {
    static var enabled: Bool {
        !CrewPrefs.quiet && !Fx.snapshot && (UserDefaults.standard.object(forKey: "crewSounds") as? Bool ?? true)
    }
    static func setEnabled(_ on: Bool) {
        UserDefaults.standard.set(on, forKey: "crewSounds")
        if !on { purrStop() }
    }
    static var isOn: Bool { UserDefaults.standard.object(forKey: "crewSounds") as? Bool ?? true }

    private static var players: [String: AVAudioPlayer] = [:]
    private static func player(_ name: String) -> AVAudioPlayer? {
        if let p = players[name] { return p }
        guard let url = Bundle.main.url(forResource: name, withExtension: "caf", subdirectory: "Sounds"),
              let p = try? AVAudioPlayer(contentsOf: url) else { return nil }
        p.prepareToPlay()
        players[name] = p
        return p
    }
    private static func play(_ name: String, volume: Float = 0.45) {
        guard enabled, let p = player(name) else { return }
        p.volume = volume
        p.currentTime = 0
        p.play()
    }

    /// A short meow (one of three recordings, never the same twice in a row).
    private static var lastMeow = ""
    static func meow() {
        let pick = ["meow1", "meow2", "meow3"].filter { $0 != lastMeow }.randomElement() ?? "meow1"
        lastMeow = pick
        play(pick)
    }
    /// "Hi": a brighter, pleading meow.
    static func hi() { play("hi") }

    /// A soft purr that loops while you hold the cat, then fades out.
    static func purrStart() {
        guard enabled, let p = player("purr"), !p.isPlaying else { return }
        p.numberOfLoops = -1
        p.volume = 0
        p.currentTime = 0
        p.play()
        p.setVolume(0.35, fadeDuration: 0.4)
    }
    static func purrStop() {
        guard let p = players["purr"], p.isPlaying else { return }
        p.setVolume(0, fadeDuration: 0.35)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { Task { @MainActor in p.stop() } }
    }

    /// The sound for an emote picked from the right-click menu.
    static func forAction(_ a: CrewModel.Action) {
        switch a {
        case .hi: hi()
        case .nap: break                      // napping is quiet
        default: meow()
        }
    }
}
