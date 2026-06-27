// =============================================================================
// swfoc_overlay/overlay_camera_bookmarks_test.cpp — unit test for
// overlay_camera_bookmarks.h (Phase 6 kickoff, iter 544 / spec iter-304).
//
// iter-304 is the Phase 6 "top quick win": bookmarkable camera positions bound
// to F6 / F7 / F8. overlay_camera_bookmarks.h holds the pure kernel — a 3-slot
// store, the SWFOC_GetCameraPos wire-result parser, and the recall
// ActionRequest builder. This test pins all of it so the deferred overlay.cpp
// F-key glue can depend on it build-only.
//
// The integration section runs the full operator cycle: a SWFOC_GetCameraPos
// wire string -> SaveFromWire -> BuildRecall -> a dispatch-ready ActionRequest
// whose Lua is a real SWFOC_SetCameraPos bridge call.
//
// overlay_camera_bookmarks.h is header-only and std-only (<cerrno> / <cmath> /
// <cstddef> / <cstdlib> / <string>, plus <cstdio> via overlay_actions.h and
// <deque> / <functional> / <mutex> via overlay_action_queue.h — -pthread is
// load-bearing for the threading runtime that include chain pulls in). No
// game, no pipe, no ImGui. Build + run via build_camera_bookmarks_test.bat.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - SAVE THEN RECALL ROUND-TRIPS  : Save(slot, p) then BuildRecall(slot)
//                                     emits SWFOC_SetCameraPos(p).
//   - EMPTY SLOT YIELDS EMPTY LUA   : BuildRecall on an unset slot yields an
//                                     empty `lua` + an "empty" label.
//   - PARSE REJECTS MALFORMED WIRE  : ParseCameraPos rejects anything not
//                                     exactly three numeric fields.
//   - MALFORMED WIRE DOESN'T CLOBBER: SaveFromWire with a bad string leaves
//                                     an already-good slot untouched.
//   - SLOTS ARE INDEPENDENT         : the three slots store distinct values.
//   - HOTKEY LABELS THE F-KEY       : the recall label names F6 / F7 / F8.
//   - PLAIN NUMBER ARGS             : BuildSetCameraPosCommand emits bare
//                                     number literals — never LuaQuoted.
// =============================================================================

#include "overlay_camera_bookmarks.h"

#include <cstdio>
#include <string>

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

    void ExpectStr(const char* name, const std::string& got, const char* want)
    {
        ++g_checks;
        if (want != nullptr && got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : \"%s\"\n    want: \"%s\"\n",
                        name, got.c_str(), want != nullptr ? want : "(null)");
        }
    }

    void ExpectContains(const char* name, const std::string& haystack,
                        const char* needle)
    {
        ++g_checks;
        if (needle != nullptr &&
            haystack.find(needle) != std::string::npos)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    \"%s\"\n    must contain: \"%s\"\n",
                        name, haystack.c_str(),
                        needle != nullptr ? needle : "(null)");
        }
    }

    void ExpectAbsent(const char* name, const std::string& haystack,
                      const char* needle)
    {
        ++g_checks;
        if (needle != nullptr &&
            haystack.find(needle) == std::string::npos)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    \"%s\"\n    must NOT contain: \"%s\"\n",
                        name, haystack.c_str(),
                        needle != nullptr ? needle : "(null)");
        }
    }

    // Exact float compare — every value used here is integer- or
    // simple-fraction-valued and round-trips losslessly through the bridge's
    // "%.3f" wire format and through float (all are well under 2^24).
    void ExpectFloat(const char* name, float got, float want)
    {
        ++g_checks;
        if (got == want)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %.6f\n    want: %.6f\n",
                        name, static_cast<double>(got),
                        static_cast<double>(want));
        }
    }

    using swfoc_overlay::ActionRequest;
    using swfoc_overlay::BuildSetCameraPosCommand;
    using swfoc_overlay::CameraBookmark;
    using swfoc_overlay::CameraBookmarkHotkey;
    using swfoc_overlay::CameraBookmarks;
    using swfoc_overlay::ParseCameraPos;
    using swfoc_overlay::ParseCameraPosField;
}

int main()
{
    std::printf("=== overlay_camera_bookmarks.h kernel test ===\n\n");

    // -----------------------------------------------------------------------
    // 1. CameraBookmarkHotkey — slot -> F-key number.
    // -----------------------------------------------------------------------
    std::printf("[CameraBookmarkHotkey]\n");
    {
        ExpectTrue("slot 0 -> 6 (F6)", CameraBookmarkHotkey(0) == 6);
        ExpectTrue("slot 1 -> 7 (F7)", CameraBookmarkHotkey(1) == 7);
        ExpectTrue("slot 2 -> 8 (F8)", CameraBookmarkHotkey(2) == 8);
        // Out-of-range -> 0 so a label never reads a garbage key.
        ExpectTrue("slot 3 -> 0 (out of range)", CameraBookmarkHotkey(3) == 0);
        ExpectTrue("slot 99 -> 0 (out of range)",
                   CameraBookmarkHotkey(99) == 0);
    }

    // -----------------------------------------------------------------------
    // 2. BuildSetCameraPosCommand — PLAIN NUMBER ARGS pin.
    // -----------------------------------------------------------------------
    std::printf("\n[BuildSetCameraPosCommand]\n");
    {
        // SWFOC_SetCameraPos takes three plain Lua numbers — bare literals,
        // FormatCoord-trimmed. Whole numbers render "0" not "0.000".
        ExpectStr("whole numbers render trimmed",
                  BuildSetCameraPosCommand(100.0f, 200.0f, 30.0f),
                  "return SWFOC_SetCameraPos(100, 200, 30)");
        ExpectStr("origin renders 0, 0, 0",
                  BuildSetCameraPosCommand(0.0f, 0.0f, 0.0f),
                  "return SWFOC_SetCameraPos(0, 0, 0)");
        ExpectStr("negative + fractional coords",
                  BuildSetCameraPosCommand(-1234.5f, 0.25f, 48.0f),
                  "return SWFOC_SetCameraPos(-1234.5, 0.25, 48)");

        // PLAIN NUMBER ARGS pin — the coords are bare number literals. A
        // copy-from-SWFOC_*Lua old form that wrapped them in LuaQuote would
        // emit an escaped-quote sequence; there must be none.
        const std::string cmd =
            BuildSetCameraPosCommand(100.0f, 200.0f, 30.0f);
        ExpectAbsent("PLAIN NUMBER ARGS: coords are never LuaQuoted",
                     cmd, "\\\"");
        ExpectAbsent("PLAIN NUMBER ARGS: no stray double-quote at all",
                     cmd, "\"");
        ExpectContains("PLAIN NUMBER ARGS: names the SetCameraPos wire",
                       cmd, "SWFOC_SetCameraPos(");
    }

    // -----------------------------------------------------------------------
    // 3. ParseCameraPosField — single-field numeric parse.
    // -----------------------------------------------------------------------
    std::printf("\n[ParseCameraPosField]\n");
    {
        float v = -999.0f;
        ExpectTrue("\"12.5\" parses", ParseCameraPosField("12.5", v));
        ExpectFloat("\"12.5\" -> 12.5", v, 12.5f);
        v = -999.0f;
        ExpectTrue("\"-320.500\" parses", ParseCameraPosField("-320.500", v));
        ExpectFloat("\"-320.500\" -> -320.5", v, -320.5f);
        v = -999.0f;
        ExpectTrue("\"0.000\" parses", ParseCameraPosField("0.000", v));
        ExpectFloat("\"0.000\" -> 0", v, 0.0f);

        // Rejections — `v` must be left untouched on a reject.
        v = 777.0f;
        ExpectTrue("empty token rejected", !ParseCameraPosField("", v));
        ExpectTrue("non-numeric token rejected",
                   !ParseCameraPosField("abc", v));
        ExpectTrue("trailing junk (\"12abc\") rejected",
                   !ParseCameraPosField("12abc", v));
        ExpectTrue("\"inf\" rejected", !ParseCameraPosField("inf", v));
        ExpectTrue("\"nan\" rejected", !ParseCameraPosField("nan", v));
        ExpectFloat("rejected token leaves out-param untouched", v, 777.0f);
    }

    // -----------------------------------------------------------------------
    // 4. ParseCameraPos — full "x,y,z" wire-result parse.
    // -----------------------------------------------------------------------
    std::printf("\n[ParseCameraPos]\n");
    {
        // The bridge emits SWFOC_GetCameraPos as "%.3f,%.3f,%.3f".
        const CameraBookmark ok = ParseCameraPos("1500.000,-320.500,48.000");
        ExpectTrue("well-formed triple -> set", ok.set);
        ExpectFloat("x parsed", ok.x, 1500.0f);
        ExpectFloat("y parsed (negative)", ok.y, -320.5f);
        ExpectFloat("z parsed", ok.z, 48.0f);

        // The no-active-camera fallback "0.000,0.000,0.000" is a VALID parse
        // — the kernel cannot (and must not) distinguish it from a genuine
        // origin viewpoint. set == true, position (0,0,0).
        const CameraBookmark origin = ParseCameraPos("0.000,0.000,0.000");
        ExpectTrue("no-camera fallback parses as a valid origin bookmark",
                   origin.set);
        ExpectFloat("origin x is 0", origin.x, 0.0f);

        // PARSE REJECTS MALFORMED WIRE pin — anything not exactly three
        // numeric fields yields set == false.
        ExpectTrue("empty string rejected", !ParseCameraPos("").set);
        ExpectTrue("garbage rejected", !ParseCameraPos("garbage").set);
        ExpectTrue("two fields rejected", !ParseCameraPos("1.0,2.0").set);
        ExpectTrue("four fields rejected",
                   !ParseCameraPos("1.0,2.0,3.0,4.0").set);
        ExpectTrue("three empty fields rejected", !ParseCameraPos(",,").set);
        ExpectTrue("middle field empty rejected",
                   !ParseCameraPos("1.0,,3.0").set);
        ExpectTrue("trailing junk in a field rejected",
                   !ParseCameraPos("1.0,2.0,3.0a").set);
        ExpectTrue("non-numeric field rejected",
                   !ParseCameraPos("1.0,abc,3.0").set);
        ExpectTrue("space-delimited (no commas) rejected",
                   !ParseCameraPos("1.0 2.0 3.0").set);
        ExpectTrue("an ERR: payload rejected",
                   !ParseCameraPos("ERR: no active tactical camera").set);
        ExpectTrue("inf field rejected", !ParseCameraPos("inf,0.0,0.0").set);
    }

    // -----------------------------------------------------------------------
    // 5. Save / Get / IsSet / Clear — SLOTS ARE INDEPENDENT pin.
    // -----------------------------------------------------------------------
    std::printf("\n[Save / Get / IsSet / Clear]\n");
    {
        CameraBookmarks bm;
        ExpectTrue("a fresh slot 0 is unset", !bm.IsSet(0));
        ExpectTrue("a fresh slot 1 is unset", !bm.IsSet(1));
        ExpectTrue("a fresh slot 2 is unset", !bm.IsSet(2));

        bm.Save(0, 100.0f, 0.0f, 0.0f);
        bm.Save(1, 0.0f, 200.0f, 0.0f);
        bm.Save(2, 0.0f, 0.0f, 300.0f);
        ExpectTrue("slot 0 is set after Save", bm.IsSet(0));
        ExpectTrue("slot 1 is set after Save", bm.IsSet(1));
        ExpectTrue("slot 2 is set after Save", bm.IsSet(2));

        // SLOTS ARE INDEPENDENT pin — each slot keeps its own position; a
        // single-shared-slot old form would read 300 in every slot.
        ExpectFloat("SLOTS ARE INDEPENDENT: slot 0 keeps x=100",
                    bm.Get(0).x, 100.0f);
        ExpectFloat("SLOTS ARE INDEPENDENT: slot 1 keeps y=200",
                    bm.Get(1).y, 200.0f);
        ExpectFloat("SLOTS ARE INDEPENDENT: slot 2 keeps z=300",
                    bm.Get(2).z, 300.0f);
        ExpectFloat("slot 0 did not absorb slot 2's z", bm.Get(0).z, 0.0f);

        // An out-of-range slot is an inert no-op and reads the empty bookmark.
        bm.Save(7, 9.0f, 9.0f, 9.0f);
        ExpectTrue("out-of-range Save is a no-op (still unset)", !bm.IsSet(7));
        ExpectFloat("out-of-range Get reads the empty bookmark",
                    bm.Get(7).x, 0.0f);

        // Re-saving a slot overwrites it.
        bm.Save(0, 555.0f, 666.0f, 777.0f);
        ExpectFloat("re-Save overwrites x", bm.Get(0).x, 555.0f);

        // Clear forgets a slot.
        bm.Clear(0);
        ExpectTrue("Clear unsets the slot", !bm.IsSet(0));
        ExpectTrue("Clear leaves the other slots intact", bm.IsSet(1));
    }

    // -----------------------------------------------------------------------
    // 6. SaveFromWire — MALFORMED WIRE DOESN'T CLOBBER pin.
    // -----------------------------------------------------------------------
    std::printf("\n[SaveFromWire]\n");
    {
        CameraBookmarks bm;

        // A well-formed wire result saves.
        ExpectTrue("valid wire saves -> true",
                   bm.SaveFromWire(0, "1500.000,-320.500,48.000"));
        ExpectTrue("the slot is now set", bm.IsSet(0));
        ExpectFloat("the parsed x landed in the slot", bm.Get(0).x, 1500.0f);
        ExpectFloat("the parsed y landed in the slot", bm.Get(0).y, -320.5f);

        // MALFORMED WIRE DOESN'T CLOBBER pin — a bad wire string returns
        // false and leaves the already-good slot 0 EXACTLY as it was. An old
        // form that wiped the slot on a failed read would fail here.
        ExpectTrue("malformed wire saves -> false",
                   !bm.SaveFromWire(0, "ERR: no active tactical camera"));
        ExpectTrue("MALFORMED WIRE DOESN'T CLOBBER: slot 0 still set",
                   bm.IsSet(0));
        ExpectFloat("MALFORMED WIRE DOESN'T CLOBBER: x untouched",
                    bm.Get(0).x, 1500.0f);
        ExpectFloat("MALFORMED WIRE DOESN'T CLOBBER: y untouched",
                    bm.Get(0).y, -320.5f);

        // An out-of-range slot also returns false (and is a no-op).
        ExpectTrue("out-of-range SaveFromWire -> false",
                   !bm.SaveFromWire(9, "1.0,2.0,3.0"));
    }

    // -----------------------------------------------------------------------
    // 7. BuildRecall — round-trip + empty-slot + F-key label pins.
    // -----------------------------------------------------------------------
    std::printf("\n[BuildRecall]\n");
    {
        CameraBookmarks bm;

        // EMPTY SLOT YIELDS EMPTY LUA pin — an unset slot must NOT produce a
        // SetCameraPos call; the render glue toasts "empty" instead of
        // firing a destructive jump to the origin.
        const ActionRequest empty = bm.BuildRecall(0);
        ExpectTrue("EMPTY SLOT YIELDS EMPTY LUA: lua is empty",
                   empty.lua.empty());
        ExpectStr("EMPTY SLOT YIELDS EMPTY LUA: label says empty",
                  empty.label, "F6 camera bookmark empty");

        // SAVE THEN RECALL ROUND-TRIPS pin — a saved slot recalls to exactly
        // the stored position via SWFOC_SetCameraPos.
        bm.Save(0, 1500.0f, -320.5f, 48.0f);
        const ActionRequest recall = bm.BuildRecall(0);
        ExpectStr("SAVE THEN RECALL ROUND-TRIPS: lua jumps to the saved pos",
                  recall.lua,
                  "return SWFOC_SetCameraPos(1500, -320.5, 48)");
        ExpectContains("recall label names the destination", recall.label,
                       "(1500, -320.5, 48)");

        // HOTKEY LABELS THE F-KEY pin — slot 0/1/2 -> F6/F7/F8.
        ExpectContains("HOTKEY LABELS THE F-KEY: slot 0 -> F6",
                       bm.BuildRecall(0).label, "F6");
        bm.Save(1, 10.0f, 20.0f, 30.0f);
        ExpectContains("HOTKEY LABELS THE F-KEY: slot 1 -> F7",
                       bm.BuildRecall(1).label, "F7");
        bm.Save(2, 40.0f, 50.0f, 60.0f);
        ExpectContains("HOTKEY LABELS THE F-KEY: slot 2 -> F8",
                       bm.BuildRecall(2).label, "F8");
        // The empty-slot label also names its F-key.
        ExpectStr("an empty slot 2's label still names F8",
                  CameraBookmarks{}.BuildRecall(2).label,
                  "F8 camera bookmark empty");
    }

    // -----------------------------------------------------------------------
    // 8. Integration — the full F6 save -> recall operator cycle.
    // -----------------------------------------------------------------------
    std::printf("\n[integration: GetCameraPos wire -> save -> recall]\n");
    {
        CameraBookmarks bm;

        // The operator frames a viewpoint and presses Save-F6. The overlay
        // glue reads the camera via SWFOC_GetCameraPos; the bridge replies
        // with this exact 3-decimal wire string.
        const std::string wire = "2048.000,-768.000,512.000";
        ExpectTrue("integration: the F6 save accepts the wire read",
                   bm.SaveFromWire(0, wire));
        ExpectTrue("integration: F6 is now bookmarked", bm.IsSet(0));

        // Later the operator presses F6 to jump back. The glue builds the
        // recall ActionRequest and hands it to the iter-513 ActionQueue.
        const ActionRequest jump = bm.BuildRecall(0);
        ExpectTrue("integration: the recall has a label",
                   !jump.label.empty());
        ExpectContains("integration: the Lua is a real bridge call",
                       jump.lua, "return SWFOC_SetCameraPos(");
        ExpectStr("integration: it recalls the exact saved viewpoint",
                  jump.lua,
                  "return SWFOC_SetCameraPos(2048, -768, 512)");

        // A second viewpoint on F7 does not disturb F6.
        ExpectTrue("integration: an F7 save accepts a second wire read",
                   bm.SaveFromWire(1, "100.000,200.000,300.000"));
        ExpectStr("integration: F7 recalls its own viewpoint",
                  bm.BuildRecall(1).lua,
                  "return SWFOC_SetCameraPos(100, 200, 300)");
        ExpectStr("integration: F6 still recalls the first viewpoint",
                  bm.BuildRecall(0).lua,
                  "return SWFOC_SetCameraPos(2048, -768, 512)");

        // F8 was never saved — pressing it is a safe no-op, not a camera jump.
        const ActionRequest f8 = bm.BuildRecall(2);
        ExpectTrue("integration: an unsaved F8 fires nothing",
                   f8.lua.empty());
        ExpectContains("integration: the unsaved F8 toast names F8",
                       f8.label, "F8");
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
