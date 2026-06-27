// =============================================================================
// swfoc_overlay/overlay_preview_ring.h — Phase 4 drop-point preview-ring kernel.
//
// Phase 4 (iter 292-296) lets the operator place units by drag-drop. iter-292
// shipped the spawn pad, iter-293 the minimap. iter-294 (spec line 52) adds a
// PREVIEW RING: while the operator is dragging a unit-type over a spawn target,
// a faction-tinted, gently pulsing ring is drawn at the cursor so they can see
// exactly where the unit will land BEFORE they release the mouse. The ring is
// a pure overlay hint drawn on ImGui's foreground draw list — there is no
// game-side draw (overlay-interactive.md iter-294: "Preview ring is overlay-
// only; no game-side draw").
//
// This header is the PURE kernel of that feature — the parts that have a right
// and a wrong answer and so must be pinned by a unit test before the ImGui
// foreground-draw-list glue in overlay.cpp depends on them:
//
//   1. PreviewRingColor()  — map the Phase 4 Faction-combo index to the ring
//      tint. The spec requires "Ring color = faction tint per iter-92 LED
//      tinting"; a wrong mapping would tint a Rebel spawn Imperial chrome.
//   2. FramePhase01()      — fold the monotonic Present frame counter into a
//      normalized [0,1) animation phase. An un-wrapped phase would make the
//      ring grow without bound instead of breathing.
//   3. PreviewRingRadius() — the pulsing radius for a given phase. A sawtooth
//      instead of a triangle, or an un-clamped negative result, would either
//      jump the ring or hand ImGui a negative-radius circle.
//
// RED-GREEN REGRESSION PINS (overlay_preview_ring_test.cpp)
// --------------------------------------------------------
//   - FACTION TINT CORRECT : PreviewRingColor(0) is REBEL amber, not the
//                            unknown-faction green; a swapped switch fails.
//   - PHASE WRAPS          : FramePhase01(period, period) == 0, never 1.0 —
//                            a missing modulo grows the ring unbounded.
//   - TRIANGLE SYMMETRY    : PreviewRingRadius is symmetric about phase 0.5;
//                            a sawtooth fails radius(0.3) == radius(0.7).
//   - PEAK AT MID-CYCLE    : radius(0.5) is the widest point of the breathe.
//   - NO NEGATIVE RADIUS   : a negative computed radius is clamped to 0 so
//                            ImGui is never asked for a negative-radius ring.
//
// Pure, header-only, std-only — no ImGui, no Windows, no bridge. Unit-tested
// with a plain g++ (build_preview_ring_test.bat).
// =============================================================================

#pragma once

namespace swfoc_overlay
{
    // Alpha applied to every preview-ring color. 0xCC (~80%) is the alpha the
    // iter-92 faction-LED convention uses — FactionTintForSlot() in overlay.cpp
    // packs 0xCCAARRGGBB. Reusing it keeps the ring a translucent hint, the
    // same visual weight as the bridge LED, rather than an opaque mark.
    inline constexpr unsigned char kPreviewRingAlpha = 0xCC;  // 204

    // Base radius of the preview ring, in screen pixels, before the pulse is
    // added. Large enough to read clearly under the cursor without hiding it.
    inline constexpr float kPreviewRingBaseRadius = 14.0f;

    // Peak extra radius the pulse adds at mid-cycle, in screen pixels. The
    // ring breathes between kPreviewRingBaseRadius and
    // kPreviewRingBaseRadius + kPreviewRingPulseAmplitude.
    inline constexpr float kPreviewRingPulseAmplitude = 6.0f;

    // Length of one full breathe cycle, in rendered frames. At the game's
    // ~60 fps this is a ~1.5 s pulse — slow enough to read as a live preview,
    // fast enough to feel responsive. The render glue feeds the global
    // Present frame counter and this period into FramePhase01().
    inline constexpr unsigned long long kPreviewRingPulseFrames = 90;

    // An RGBA color, 0..255 per channel. The render glue packs this into an
    // ImGui IM_COL32; keeping the kernel a plain struct (no ImU32, no ImGui)
    // is what lets it be unit-tested with a bare g++.
    struct RingColor
    {
        unsigned char r;
        unsigned char g;
        unsigned char b;
        unsigned char a;
    };

    // Map a Phase 4 Faction-combo index to the preview-ring tint.
    //
    // `factionIndex` is g_actionFactionIdx in overlay.cpp — the index into
    // kActionFactions[] = { "REBEL", "EMPIRE", "UNDERWORLD" }. The RGB values
    // match FactionTintForSlot()'s iter-92 LED palette exactly (its slots 0/1/2
    // line up with those three factions):
    //   - 0 REBEL      -> amber      (iter-92 0xCCFFB400)
    //   - 1 EMPIRE     -> chrome     (iter-92 0xCCDCDCDC)
    //   - 2 UNDERWORLD -> sand+rust  (iter-92 0xCCC8965A)
    // Any other index — defensive; ImGui::Combo already clamps its index —
    // yields the same generic green FactionTintForSlot returns for an unknown
    // slot (iter-92 0xCC22AA22). Every result carries kPreviewRingAlpha.
    inline RingColor PreviewRingColor(int factionIndex)
    {
        switch (factionIndex)
        {
            case 0:   // REBEL — amber
                return RingColor{ 0xFF, 0xB4, 0x00, kPreviewRingAlpha };
            case 1:   // EMPIRE — chrome
                return RingColor{ 0xDC, 0xDC, 0xDC, kPreviewRingAlpha };
            case 2:   // UNDERWORLD — sand + rust
                return RingColor{ 0xC8, 0x96, 0x5A, kPreviewRingAlpha };
            default:  // unknown faction — generic green
                return RingColor{ 0x22, 0xAA, 0x22, kPreviewRingAlpha };
        }
    }

    // Fold a monotonically-increasing frame counter into a normalized
    // animation phase in [0, 1). One full cycle spans `periodFrames` frames:
    // frame 0 -> 0, frame periodFrames/2 -> ~0.5, frame periodFrames -> 0
    // again (the wrap). A zero `periodFrames` is degenerate — no animation —
    // and yields 0 (and never divides by zero).
    inline float FramePhase01(unsigned long long frame,
                              unsigned long long periodFrames)
    {
        if (periodFrames == 0)
        {
            return 0.0f;
        }
        const unsigned long long pos = frame % periodFrames;
        return static_cast<float>(pos) / static_cast<float>(periodFrames);
    }

    // The pulsing radius of the preview ring at animation phase `phase01`.
    //
    // The radius breathes on a TRIANGLE wave: at phase 0 it is `baseRadius`,
    // it rises linearly to `baseRadius + pulseAmplitude` at phase 0.5 (the
    // widest point), then falls linearly back to `baseRadius` at phase 1. A
    // triangle (not a sine) keeps every test value exactly representable, and
    // (unlike a sawtooth) it has no discontinuity — the ring never jumps.
    //
    // `phase01` is clamped to [0, 1] first, so a render glue that feeds a
    // slightly out-of-range phase still behaves. The final radius is clamped
    // to be non-negative: a `baseRadius` so negative the pulse cannot lift it
    // back above 0 would otherwise hand ImGui a negative-radius circle.
    inline float PreviewRingRadius(float baseRadius, float pulseAmplitude,
                                   float phase01)
    {
        const float p = phase01 < 0.0f ? 0.0f
                                       : (phase01 > 1.0f ? 1.0f : phase01);
        // Triangle wave folded about p = 0.5: rises 0->1 then falls 1->0.
        const float tri = p < 0.5f ? (2.0f * p) : (2.0f * (1.0f - p));
        const float radius = baseRadius + pulseAmplitude * tri;
        return radius < 0.0f ? 0.0f : radius;
    }
}
