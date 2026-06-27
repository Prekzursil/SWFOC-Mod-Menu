// =============================================================================
// swfoc_overlay/overlay_preview_ring_test.cpp — unit test for
// overlay_preview_ring.h (Phase 4 cont., iter 531 / spec iter-294).
//
// iter-294 adds a drop-point preview ring: while a unit-type is being dragged
// over a spawn target, a faction-tinted, gently pulsing ring is drawn at the
// cursor so the operator sees where the unit will land before releasing.
// overlay_preview_ring.h holds the three pure pieces of that feature — the
// faction tint, the frame-counter -> animation-phase fold, and the pulsing
// radius. This test pins all three so the ImGui foreground-draw-list glue in
// overlay.cpp can depend on them build-only.
//
// overlay_preview_ring.h is header-only and std-only. Build + run via
// build_preview_ring_test.bat — no game, no pipe, no ImGui.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - FACTION TINT CORRECT : PreviewRingColor(0) must be REBEL amber, not the
//                            unknown-faction green. A swapped switch case
//                            fails this pin.
//   - PHASE WRAPS          : FramePhase01(period, period) must be 0, never
//                            1.0. A missing modulo grows the ring unbounded
//                            and fails this pin.
//   - TRIANGLE SYMMETRY    : PreviewRingRadius must be symmetric about phase
//                            0.5. A sawtooth (no fold) fails radius(0.3) ==
//                            radius(0.7) while still passing a naive
//                            "phase 0 -> base" check.
//   - PEAK AT MID-CYCLE    : PreviewRingRadius(0.5) must be the widest point.
//   - NO NEGATIVE RADIUS   : a negative computed radius must clamp to 0 — the
//                            render glue must never ask ImGui for a
//                            negative-radius circle.
// =============================================================================

#include "overlay_preview_ring.h"

#include <cstdio>

namespace
{
    int g_checks = 0;
    int g_failures = 0;

    void ExpectTrue(const char* name, bool cond)
    {
        ++g_checks;
        if (cond)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    expected true\n", name);
        }
    }

    // Float compare with a small absolute epsilon. Every value the kernel
    // produces here is exactly representable (integers, halves, quarters)
    // except the deliberate n/90 phase checks, which the epsilon covers.
    void ExpectNear(const char* name, float got, float want)
    {
        ++g_checks;
        const float diff = got - want;
        const float absdiff = diff < 0.0f ? -diff : diff;
        if (absdiff <= 0.001f)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %.5f\n    want: %.5f\n",
                        name, static_cast<double>(got),
                        static_cast<double>(want));
        }
    }

    void ExpectEqInt(const char* name, int got, int want)
    {
        ++g_checks;
        if (got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %d\n    want: %d\n",
                        name, got, want);
        }
    }

    // Assert a RingColor equals (r, g, b, a). One named sub-check per channel
    // so a failure report says which channel drifted.
    void ExpectColor(const char* name, const swfoc_overlay::RingColor& got,
                     int wantR, int wantG, int wantB, int wantA)
    {
        char nm[128];
        std::snprintf(nm, sizeof(nm), "%s [r]", name);
        ExpectEqInt(nm, got.r, wantR);
        std::snprintf(nm, sizeof(nm), "%s [g]", name);
        ExpectEqInt(nm, got.g, wantG);
        std::snprintf(nm, sizeof(nm), "%s [b]", name);
        ExpectEqInt(nm, got.b, wantB);
        std::snprintf(nm, sizeof(nm), "%s [a]", name);
        ExpectEqInt(nm, got.a, wantA);
    }
}

int main()
{
    using namespace swfoc_overlay;

    std::printf("== overlay_preview_ring unit test ==\n");

    // ---- Constants are sane ------------------------------------------------
    ExpectEqInt("const: kPreviewRingAlpha is 0xCC (iter-92 LED alpha)",
                kPreviewRingAlpha, 204);
    ExpectTrue("const: kPreviewRingBaseRadius is positive",
               kPreviewRingBaseRadius > 0.0f);
    ExpectTrue("const: kPreviewRingPulseAmplitude is positive",
               kPreviewRingPulseAmplitude > 0.0f);
    ExpectTrue("const: kPreviewRingPulseFrames is positive",
               kPreviewRingPulseFrames > 0ull);

    // ---- PreviewRingColor: faction tint matches the iter-92 LED palette ----
    // RGB values mirror FactionTintForSlot() in overlay.cpp exactly.
    ExpectColor("color: index 0 -> REBEL amber (0xCCFFB400)",
                PreviewRingColor(0), 0xFF, 0xB4, 0x00, 0xCC);
    ExpectColor("color: index 1 -> EMPIRE chrome (0xCCDCDCDC)",
                PreviewRingColor(1), 0xDC, 0xDC, 0xDC, 0xCC);
    ExpectColor("color: index 2 -> UNDERWORLD sand+rust (0xCCC8965A)",
                PreviewRingColor(2), 0xC8, 0x96, 0x5A, 0xCC);
    // Any other index falls back to the generic-green unknown tint.
    ExpectColor("color: index 3 -> generic green (0xCC22AA22)",
                PreviewRingColor(3), 0x22, 0xAA, 0x22, 0xCC);
    ExpectColor("color: index -1 -> generic green (defensive)",
                PreviewRingColor(-1), 0x22, 0xAA, 0x22, 0xCC);
    ExpectColor("color: index 99 -> generic green (defensive)",
                PreviewRingColor(99), 0x22, 0xAA, 0x22, 0xCC);

    // PIN faction-tint-correct: REBEL must be amber, not the unknown green.
    {
        const RingColor rebel = PreviewRingColor(0);
        ExpectTrue("PIN faction-tint: index 0 is REBEL amber, not green",
                   rebel.r == 0xFF && rebel.g == 0xB4 && rebel.b == 0x00);
    }
    // PIN distinctness: the three factions are visually distinct tints.
    ExpectTrue("PIN distinct: REBEL differs from EMPIRE",
               PreviewRingColor(0).b != PreviewRingColor(1).b);
    ExpectTrue("PIN distinct: EMPIRE differs from UNDERWORLD",
               PreviewRingColor(1).g != PreviewRingColor(2).g);
    // PIN every faction tint carries the shared iter-92 LED alpha.
    ExpectTrue("PIN alpha: every faction tint carries kPreviewRingAlpha",
               PreviewRingColor(0).a == kPreviewRingAlpha &&
               PreviewRingColor(1).a == kPreviewRingAlpha &&
               PreviewRingColor(2).a == kPreviewRingAlpha &&
               PreviewRingColor(3).a == kPreviewRingAlpha);

    // ---- FramePhase01: frame counter -> normalized [0, 1) phase -----------
    ExpectNear("phase: frame 0 of 90 -> 0.0",
               FramePhase01(0, 90), 0.0f);
    ExpectNear("phase: frame 45 of 90 -> 0.5 (mid-cycle)",
               FramePhase01(45, 90), 0.5f);
    // PIN phase-wraps: a full period maps back to 0, never to 1.0.
    ExpectNear("PIN wrap: frame 90 of 90 -> 0.0 (not 1.0)",
               FramePhase01(90, 90), 0.0f);
    ExpectNear("PIN wrap: frame 180 of 90 -> 0.0 (two full cycles)",
               FramePhase01(180, 90), 0.0f);
    ExpectNear("phase: frame 91 of 90 -> 1/90 (just past the wrap)",
               FramePhase01(91, 90), 1.0f / 90.0f);
    ExpectNear("phase: frame 135 of 90 -> 0.5 (1.5 cycles)",
               FramePhase01(135, 90), 0.5f);
    ExpectNear("phase: frame 89 of 90 -> 89/90 (last frame before wrap)",
               FramePhase01(89, 90), 89.0f / 90.0f);
    // Degenerate period: no animation, never divides by zero.
    ExpectNear("phase: period 0 is degenerate -> 0.0",
               FramePhase01(50, 0), 0.0f);
    ExpectNear("phase: period 1 -> always 0.0 (frame 0)",
               FramePhase01(0, 1), 0.0f);
    ExpectNear("phase: period 1 -> always 0.0 (frame 7)",
               FramePhase01(7, 1), 0.0f);
    // PIN monotonic within a period: a later frame yields a larger phase.
    ExpectTrue("PIN monotonic: phase(10,90) < phase(20,90)",
               FramePhase01(10, 90) < FramePhase01(20, 90));
    // PIN never reaches 1.0 — the phase is a half-open [0, 1) range.
    ExpectTrue("PIN bound: phase(89,90) < 1.0",
               FramePhase01(89, 90) < 1.0f);
    {
        // PIN bounded: every frame across multiple cycles stays in [0, 1).
        bool allBounded = true;
        for (unsigned long long f = 0; f < 360; ++f)
        {
            const float ph = FramePhase01(f, 90);
            if (ph < 0.0f || ph >= 1.0f)
            {
                allBounded = false;
            }
        }
        ExpectTrue("PIN bounded: phase(0..359, 90) all in [0, 1)", allBounded);
    }

    // ---- PreviewRingRadius: triangle-wave pulse ---------------------------
    ExpectNear("radius: phase 0 -> baseRadius",
               PreviewRingRadius(14.0f, 6.0f, 0.0f), 14.0f);
    // PIN peak: phase 0.5 is base + full amplitude.
    ExpectNear("PIN peak: phase 0.5 -> base + amplitude (14 + 6 = 20)",
               PreviewRingRadius(14.0f, 6.0f, 0.5f), 20.0f);
    ExpectNear("radius: phase 1.0 -> baseRadius (cycle closes)",
               PreviewRingRadius(14.0f, 6.0f, 1.0f), 14.0f);
    ExpectNear("radius: phase 0.25 -> base + half amplitude (17)",
               PreviewRingRadius(14.0f, 6.0f, 0.25f), 17.0f);
    ExpectNear("radius: phase 0.75 -> base + half amplitude (17)",
               PreviewRingRadius(14.0f, 6.0f, 0.75f), 17.0f);
    // PIN triangle-symmetry: a sawtooth would fail these mirror pairs.
    ExpectNear("PIN symmetry: radius(0.3) == radius(0.7)",
               PreviewRingRadius(14.0f, 6.0f, 0.3f),
               PreviewRingRadius(14.0f, 6.0f, 0.7f));
    ExpectNear("PIN symmetry: radius(0.1) == radius(0.9)",
               PreviewRingRadius(14.0f, 6.0f, 0.1f),
               PreviewRingRadius(14.0f, 6.0f, 0.9f));
    // Monotone rising before the peak, falling after it.
    ExpectTrue("radius: rising before peak — radius(0.1) < radius(0.4)",
               PreviewRingRadius(14.0f, 6.0f, 0.1f) <
               PreviewRingRadius(14.0f, 6.0f, 0.4f));
    ExpectTrue("radius: falling after peak — radius(0.6) > radius(0.9)",
               PreviewRingRadius(14.0f, 6.0f, 0.6f) >
               PreviewRingRadius(14.0f, 6.0f, 0.9f));
    {
        // PIN peak-is-max: no phase yields a wider ring than phase 0.5.
        bool peakIsMax = true;
        const float peak = PreviewRingRadius(14.0f, 6.0f, 0.5f);
        for (int i = 0; i <= 20; ++i)
        {
            const float p = static_cast<float>(i) / 20.0f;
            if (PreviewRingRadius(14.0f, 6.0f, p) > peak + 0.001f)
            {
                peakIsMax = false;
            }
        }
        ExpectTrue("PIN peak-is-max: radius(0.5) >= radius(p) for all p",
                   peakIsMax);
    }
    // Out-of-range phase is clamped to [0, 1] before the fold.
    ExpectNear("radius: phase -1 clamps to 0 -> baseRadius",
               PreviewRingRadius(14.0f, 6.0f, -1.0f), 14.0f);
    ExpectNear("radius: phase -0.5 clamps to 0 -> baseRadius",
               PreviewRingRadius(14.0f, 6.0f, -0.5f), 14.0f);
    ExpectNear("radius: phase 2.0 clamps to 1 -> baseRadius",
               PreviewRingRadius(14.0f, 6.0f, 2.0f), 14.0f);
    ExpectNear("radius: phase 1.5 clamps to 1 -> baseRadius",
               PreviewRingRadius(14.0f, 6.0f, 1.5f), 14.0f);
    // PIN no-negative-radius: a base so negative the pulse can't lift it
    // above 0 must clamp to 0, not pass a negative radius to ImGui.
    ExpectNear("PIN no-negative: radius(-100, 6, 0) clamps to 0",
               PreviewRingRadius(-100.0f, 6.0f, 0.0f), 0.0f);
    ExpectNear("PIN no-negative: radius(-100, 6, 0.5) clamps to 0",
               PreviewRingRadius(-100.0f, 6.0f, 0.5f), 0.0f);
    // Zero amplitude: a static ring at exactly baseRadius for every phase.
    ExpectNear("radius: amplitude 0 -> baseRadius at phase 0",
               PreviewRingRadius(14.0f, 0.0f, 0.0f), 14.0f);
    ExpectNear("radius: amplitude 0 -> baseRadius at phase 0.5",
               PreviewRingRadius(14.0f, 0.0f, 0.5f), 14.0f);
    ExpectNear("radius: amplitude 0 -> baseRadius at phase 0.9",
               PreviewRingRadius(14.0f, 0.0f, 0.9f), 14.0f);
    // The shipped default constants behave as documented.
    ExpectNear("radius: default constants at phase 0 -> kPreviewRingBaseRadius",
               PreviewRingRadius(kPreviewRingBaseRadius,
                                 kPreviewRingPulseAmplitude, 0.0f),
               kPreviewRingBaseRadius);
    ExpectNear("radius: default constants at phase 0.5 -> base + amplitude",
               PreviewRingRadius(kPreviewRingBaseRadius,
                                 kPreviewRingPulseAmplitude, 0.5f),
               kPreviewRingBaseRadius + kPreviewRingPulseAmplitude);

    // ---- Integration: the full frame -> phase -> radius pipeline -----------
    // The exact sequence DrawPreviewRing() runs in overlay.cpp: fold the
    // frame counter to a phase, then size the ring from that phase.
    {
        const float phaseMid =
            FramePhase01(45, kPreviewRingPulseFrames);  // 45/90 -> 0.5
        ExpectNear("integration: frame 45 of default period -> phase 0.5",
                   phaseMid, 0.5f);
        ExpectNear("integration: phase 0.5 -> peak radius (base + amplitude)",
                   PreviewRingRadius(kPreviewRingBaseRadius,
                                     kPreviewRingPulseAmplitude, phaseMid),
                   kPreviewRingBaseRadius + kPreviewRingPulseAmplitude);
    }
    {
        const float phaseZero =
            FramePhase01(0, kPreviewRingPulseFrames);  // frame 0 -> 0
        ExpectNear("integration: frame 0 -> phase 0 -> base radius",
                   PreviewRingRadius(kPreviewRingBaseRadius,
                                     kPreviewRingPulseAmplitude, phaseZero),
                   kPreviewRingBaseRadius);
    }
    // The default Faction-combo selection (index 0) tints the ring REBEL amber.
    ExpectColor("integration: default faction index 0 -> REBEL amber ring",
                PreviewRingColor(0), 0xFF, 0xB4, 0x00, 0xCC);
    {
        // A full pipeline run mid-cycle produces a positive radius and a
        // valid alpha-carrying color — the contract the render glue relies on.
        const float ph = FramePhase01(45, kPreviewRingPulseFrames);
        const float rad = PreviewRingRadius(kPreviewRingBaseRadius,
                                            kPreviewRingPulseAmplitude, ph);
        const RingColor col = PreviewRingColor(0);
        ExpectTrue("integration: pipeline yields positive radius + valid color",
                   rad > 0.0f && col.a == kPreviewRingAlpha);
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
