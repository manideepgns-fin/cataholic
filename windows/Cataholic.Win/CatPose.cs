using System.Windows;
using System.Windows.Media;

namespace Cataholic;

// The contract between the drawing (CatDrawing.cs) and everything that moves the cat (CatView/CatPanel…).
// A 1:1 mirror of the parameters of CrewCharacter in Sources/Cataholic/CrewCharacter.swift — same names, same
// defaults, same units: character-box points (the box is 46 × 62, origin top-left, y down, feet at y ≈ 49).
// The drawing never decides pose or motion; the movers never draw.

public enum CatState { Needs, Working, Sleeping, Reading, Running, Jump, Hi, Big, Stretch, Yawn, Groom, Look, Clingy, Alert, Nap }

/// An emote's eye override: open, shut, sleepy half, "^ ^" happy, or wide.
public enum CatEyes { Open, Shut, Half, Happy, Wide }

public sealed class CatPose
{
    public CatState State = CatState.Look;
    public string Badge = "";              // needs count, e.g. "3" — empty hides the sign number
    public string Glyph = "♥";             // the glyph on the collar bell (CrewSkinColors.glyph)
    public bool BlinkShut;                 // blink end state
    public double BreatheY;                // sleeping breathing offset
    public double ZRise, ZOpacity;         // (rise, opacity) for the drifting z (zLift)
    public double RunPhase;                // running: gallop phase in radians (0 = standing)
    public double Breathe = 1;             // body scale ~1.0–1.015
    public double TailSway;                // tail sway offset (pt)
    public double EarTwitch;               // 0 = still, 1 = left ear flicked
    public Vector Pupil;                   // look-at-pointer offset, clamped ≤ 1.5 pt
    public bool EyeHalf;                   // clingy: eyes half-closed
    public double HeadTurn;                // head-feature shift (pt)
    public double HeadTilt;                // head tilt (deg)
    public bool JumpCrouch;                // jump take-off crouch
    public double BodyTilt;                // clingy lean toward the pointer (deg)
    public bool Pin = true;                // the pin + string over the head
    public bool Happy;                     // petted: "^ ^" eyes + rosy cheeks
    public double Stride = 38;             // leg swing (deg) on the side-on walker: 38 = gallop, ~22 = walk
    public CatCoat? CoatOverride;          // null = CatCoat.Current
    public double PawRaise = 1;            // hi: the waving arm, 0 down … 1 up
    public double PawWave;                 // hi: wave angle (deg)
    public Point? GroomPaw = new Point(27.5, 27.5);   // groom: the licking paw (null = down)
    public bool Tongue = true;             // groom: tongue out
    public double MouthOpen = 1;           // yawn: 0 shut … 1 wide
    public double EarsBack;                // 0 up … 1 flattened back
    public CatEyes? Eyes;                  // an emote's eye override
    public double PawWrist;                // hi / groom: wrist bend (deg)
    public double NapBreath;               // nap: the curled body rising (1) and falling (0)
    public bool NapPeek;                   // nap: one eye peeks open before it wakes
    public double RunSpeed;                // zoomies: 0 … 1 of top speed
    public int RunGait;                    // zoomies: 0 run · 1 scramble · 2 skid
    public Color? CollarTint;              // Find my cat lookalikes (not used yet on Windows)
    public bool Bell = true;
    public Color? TailTip;
    public double CollarBold = 1;

    public CatPose Clone() => (CatPose)MemberwiseClone();
}
