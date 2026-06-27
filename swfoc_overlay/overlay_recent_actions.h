// =============================================================================
// swfoc_overlay/overlay_recent_actions.h — Phase 3 recent-actions history.
//
// The Phase 3 "Actions" widgets (Spawn / Make-Invuln / Kill, iter 520) each
// enqueue an ActionRequest onto the ActionQueue. A recurring operator need is
// re-firing the LAST few calls without re-typing the unit type or address —
// the agent #2 overlay UX research flagged a "recent-actions toolbar" as a top
// quick win (overlay_ux_research_2026-05-08.md; overlay-interactive.md line 35:
// "re-execute last 5 SWFOC calls; ~50 LoC overlay-only; ship in Phase 3 or 4").
//
// RecentActions is that toolbar's backing store: a bounded, most-recent-first
// history of dispatched ActionRequests. The render path draws one clickable
// slot per entry; clicking a slot re-enqueues that exact ActionRequest — the
// click-to-re-fire contract.
//
// Semantics are a recent-FILES list, NOT a raw FIFO ring:
//   - Record() inserts at the front; index 0 is always the most recent.
//   - If an entry with the SAME `lua` line already exists, it is removed from
//     its old slot and re-inserted at the front (promote). A re-fired action
//     therefore never duplicates a slot — the toolbar always shows up to
//     kCapacity DISTINCT actions, which is what makes the 5 slots useful: a
//     raw ring would show "Spawn X" five times if the operator spammed it.
//   - The `lua` line — not the `label` — is the identity: two requests that
//     send the identical bridge command ARE the same action even if their
//     toast labels differ; the newer label wins on a promote.
//   - Recording a genuinely new action when the list is full evicts the
//     OLDEST entry (highest index). A promote never evicts (size is unchanged).
//
// Pure, header-only, std-only — no ImGui, no bridge, no Windows. It reuses
// ActionRequest from overlay_action_queue.h (no new type). The whole structure
// is unit-tested with a plain g++ — see overlay_recent_actions_test.cpp
// (build_recent_actions_test.bat).
//
// THREADING: RecentActions is touched ONLY by the render thread — Record() at
// the button / toolbar onClick site, At() / Count() while drawing the toolbar.
// The background action worker drains the ActionQueue but never touches this
// history. Render-thread-confined, so — unlike ActionQueue — it needs no mutex.
//
// WIRED (iter 521): overlay.cpp now #includes this header, owns the
// process-wide RecentActionsInstance() function-local-static singleton, and
// draws RenderRecentActionsToolbar() inside RenderActionsWindow. Every Phase 3
// dispatch — button onClick or toolbar re-fire — routes through overlay.cpp's
// DispatchAction(), which Enqueue()s the request onto the bridge worker AND
// Record()s it here. See knowledge-base/overlay_phase3_recentactions_iter521.md.
// =============================================================================

#pragma once

#include <cstddef>
#include <vector>

#include "overlay_action_queue.h"  // ActionRequest

namespace swfoc_overlay
{
    // Bounded most-recent-first history of dispatched ActionRequests, backing
    // the Phase 3 recent-actions toolbar. See the file header for the full
    // recent-files-list semantics (front-insert, dedup-promote on `lua`,
    // oldest-evict at capacity).
    class RecentActions
    {
    public:
        // Toolbar slot count — 5 per the agent #2 quick-win spec
        // (overlay-interactive.md line 35: "re-execute last 5 SWFOC calls").
        static constexpr std::size_t kCapacity = 5;

        // Record a dispatched action. If an entry with the same `lua` line is
        // already present it is promoted to the front (no duplicate slot);
        // otherwise the action is inserted at the front and, if that pushes
        // the history past kCapacity, the oldest entry is evicted.
        void Record(const ActionRequest& req)
        {
            // `req` MAY alias an element of items_ — the toolbar's
            // click-to-re-fire path naturally calls Record(At(i)). Copy first
            // so the promote-erase below cannot dangle the reference before
            // the re-insert reads it. Without this copy, re-firing a recent
            // slot is undefined behaviour.
            const ActionRequest entry = req;

            // Promote: drop any existing entry that sends the same command.
            for (std::size_t i = 0; i < items_.size(); ++i)
            {
                if (items_[i].lua == entry.lua)
                {
                    items_.erase(items_.begin() +
                                 static_cast<std::ptrdiff_t>(i));
                    break;  // `lua` is unique within items_, so one match max.
                }
            }

            // Insert at the front — index 0 is the most recent.
            items_.insert(items_.begin(), entry);

            // Evict the oldest beyond capacity. At most one over: a promote
            // erased one before this insert, so a re-fire never evicts.
            if (items_.size() > kCapacity)
            {
                items_.resize(kCapacity);
            }
        }

        // Number of distinct actions currently held (0..kCapacity).
        std::size_t Count() const { return items_.size(); }

        // True when no action has been recorded yet (toolbar draws nothing).
        bool Empty() const { return items_.empty(); }

        // The action at slot `i` — 0 is the most recent. `i` must be < Count();
        // .at() bounds-checks so an out-of-range render bug throws instead of
        // reading garbage. The returned reference is the click-to-re-fire
        // payload: pass it straight to ActionQueue::Enqueue() (and back into
        // Record() — alias-safe, see the copy in Record()).
        const ActionRequest& At(std::size_t i) const { return items_.at(i); }

        // Drop the whole history (e.g. an operator "clear recent" control).
        void Clear() { items_.clear(); }

    private:
        // Front == most recent; size always <= kCapacity; `lua` unique.
        std::vector<ActionRequest> items_;
    };
}
