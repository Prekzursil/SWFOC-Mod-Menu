# Project History

Chronological summary of session handoffs, archived from `knowledge-base/` root during the 2026-04-26 reorg.
Full handoff documents live at `archive/handoffs/`.

## Sessions

### 2026-05-07 — 23rd codified rule + SWFOC_TriggerVictory native UX shipped (iter 460-461)

2-iter mini-arc closing the iter-450 SWFOC_TriggerVictory infrastructure with the operator-facing UX layer + codifying the hardest-won lesson from the iter-440-449 RE arc. Establishes a pattern of "post-arc operator-visible continuation": after a multi-iter RE arc completes infrastructure but stalls on engine-level injection, ship the operator-visible LIVE-flip-adjacent surface so the wire is transparent in the editor (badged PHASE 2 PENDING) rather than only accessible via developer-mode Lua Playground.

**iter-460 — Codify 23rd rule** (`feedback_re_body_inspection_beyond_rtti.md`; 6th Tier-1 production codification): RTTI-confirmed function set is a necessary but insufficient signal for RE. When RTTI candidates don't produce expected behavior signature, decompile function bodies — hidden non-RTTI helpers commonly handle hot paths (e.g., iter-449's 0x140341FE0 counter-increment helper sandwiched between RTTI'd 0x140341AF0 + 0x140341FF0). Body-inspection signals: stride pattern matching / field-offset access / indirect calls / heap-management calls / caller xrefs into RTTI cluster's address space. 7-instance evidence base (iter-444/445/447/448/449 hard + iter-450a/450b reinforcing). Codification queue 22 → 23; 6 candidates remain pending.

**iter-461 — SWFOC_TriggerVictory native UX on WorldState tab** (5th forward application of iter-426 event-driven defer rule): full end-to-end ship of dispatcher helper + VM properties/command/handler + CapabilityAwareAction + AllActions list extension + XAML GroupBox at row 4 + 3 dedicated pin tests. Operators now see the wire in their primary workflow surface with a clear PHASE 2 PENDING badge — operator presses Trigger, sees `[ok] Engine: SWFOC_TriggerVictory(Galactic_Conquer) → PHASE2_PENDING`, knows the wire reaches the bridge but engine state remains unchanged until iter-450c+ activates MH_EnableHook. ~192 LoC source (162 C# + 30 XAML) + 3 pin tests. Build 0/0 in 34.80 sec; iter-461 tests 3/3 in 2ms; WorldState filtered tests 24/24 in 65ms; binary republished 150.07 MB. Triple-source consistency (bridge `kKnownVictoryTypes[]` + simulator `s_knownVictoryTypes[]` + VM `VictoryTypes`) MAINTAINED at 14 entries each per iter-459 baseline. Bridge harness 1100/0 sustained for **224 consecutive iters**.

**Cumulative this conversation continuation (42 iters: 423-462)**: 3 NEW codified rules (#21 + #22 + #23) + 41 close-out docs + 23 new tools + 1 changelog supplement + 7 cheap-insurance republishes + 1 operator-visible UX iter (iter-461) + 1 mini-refresh quad iter (iter-462; this entry). 9/9 ORIGINAL MANDATE ITEMS continue COMPLETE.

### 2026-05-07 — SWFOC_TriggerVictory A1.x arc closure + audit-cadence pair complete + 22nd codified rule (iter 432-455)

24-iter sub-arc (the longest single-window in the conversation continuation). Two major outcomes: (1) the **SWFOC_TriggerVictory A1.x arc** completed at 15 iters in an unusual "infrastructure-LIVE / engine-PHASE2-PENDING" terminal state — bridge wrapper + DORMANT MinHook scaffolding + simulator handler + Lua Playground UX + 5 ledger pins all shipped, but engine-level injection blocked by RE diminishing returns; (2) the **22nd codified rule** at iter-437 (`feedback_codified_rule_application_via_rationale_extension.md`; 8th Tier-1 production codification) systematizes how prior codified rules compound into existing P2HP catalog entries via 4-component template (cite-rule + identify-shape + state-cost + cite-LIVE-alternative).

**Headline outcome**: NEW 22nd codified rule. SWFOC_TriggerVictory arc demonstrates the canonical post-RE-stall structure (per iter-426 event-driven defer rule): ship infrastructure (wrapper/simulator/UX/pins), document closure, queue continuation. Future operators reading the catalog see correctly-framed Phase2HookPending with the iter-450c continuation path documented in detail.

**Phase 1 — iter-432 mini-refresh (this iter's predecessor)**: 7th capstone in iter-222/254/265/322/348/396/413/421/432 sequence, mini scope. Closed iter 421-431 codification cluster.

**Phase 2 — Catalog rationale extensions (iter 433/436)**: 7-instance corpus of iter-426 forward applications (4 entries iter-433 + 3 entries iter-436 = SpawnAsStoryArrival / EventControl / FreezeAI / SetPermadeath / SetAreaDamage / SetTargetFilter / ToggleOHKAttackPower). Each entry's rationale extended with `Iter 433: Event-driven subsystem (...; per iter-426 ... rule). Multi-iter A1.x offset RE required.` shape. iter-435 STATUS surgical line-3 prepend captured the docs delta.

**Phase 3 — iter-437 codification of the rationale-extension pattern**: 22nd codified rule. The pattern is itself a meta-pattern about how codified rules apply forward — iter-373 self-validation framework triggered at 4 forward applications; iter-426 codification triggered at 7 forward applications; iter-437 codifies the application mechanism itself. Pattern: `Iter NNN: <citation>` rationale prefix + 4-component body. Becomes the canonical template for any future codified rule that needs systematic application across existing catalog entries.

**Phase 4 — iter-438 operator changelog supplement12**: 13th post-arc docs cadence instance. Closed iter 428-437 codification + catalog rationale work.

**Phase 5 — iter-439 pause/pivot decision**: Picked SWFOC_TriggerVictory as the canonical multi-iter A1.x arc kickoff for the conversation continuation.

**Phase 6 — SWFOC_TriggerVictory A1.x arc (iter 440-450b; 11 iters)**:
- iter-440 to iter-449: Progressive RE (10 iters); iter-449 disambiguation breakthrough identifying 0x140341FE0 (16-byte counter helper) as Option C MinHook target; corrected iter-440's "StoryEventVictoryClass @ 0x140453310" misframing (actually `StoryEvent_Factory_Create`)
- iter-450: Scaffolding LIVE — ~120 LoC C++ in lua_bridge.cpp (kKnownVictoryTypes[14] + Lua_TriggerVictory wrapper + Hook_VictoryMonitorCounter DORMANT detour); 3 NEW ledger pins (rva_victory_monitor_counter_inc/ctor/parent_tick); rvas.h +3 constants; CapabilityStatusCatalog.cs +1 Phase2HookPending entry with iter-426 + iter-437 rule citations; bridge build PASS (1100/0 sustained); editor Core build 0/0/4.55s
- iter-451: Simulator handler — FakeGameState +2 fields (VictoryTriggerPending + VictoryTriggerType); SwfocSimulator.cs +50 LoC (HandleTriggerVictory + 14-name allow-list); NEW Iter451_TriggerVictoryHandlerTests.cs with 8 pin tests (no-arg / empty-string / unknown-type / Galactic_Conquer / Sub_Tactical_Story / Skirmish_Control / second-call-overwrites / invalid-after-valid-leaves-prior-intact); 8/0/0 PASS in 26ms
- iter-452: Lua Playground preset menu — +15 entries (1 PHASE 2 PENDING header + 14 VictoryType presets across Galactic_*/Skirmish_*/Sub_Tactical_*); Editor binary republished at 157.35 MB Release (May 7 16:19)
- iter-450a: RE-only honest-defer #1 per iter-426 rule — NEW tools/iter450a_extract_victory_ctor.py corpus extractor + 2 NEW ledger pins (rva_victory_monitor_dtor_full + rva_victory_monitor_dtor_vec); critical findings: VictoryMonitor has 3 embedded vectors (NOT 1); AwaitingVictoryTests vector subobject at instance+0x60 (with vftable); +0x70 is DWORD count (not _last QWORD pointer); AwaitingVictoryTestType is POD-like at dtor level
- iter-450b: RE-only honest-defer #2 — NEW tools/iter450b_find_construction_site.py + iter450b_corpus_wide_xref.py; DECISIVE NEGATIVE FINDING: corpus-wide xref scan returned EXACTLY 3 hits, all already-pinned lifecycle functions; AwaitingVictoryTests construction dispatches via OBJECT vftable at runtime (invisible to static-RE); iter-450c queued for method-table walk OR Frida dynamic RE
- Arc cost ~3× typical A1.x (5 iters); honest-defer chain demonstrates iter-426 rule's predictive power for event-driven subsystems

**Phase 7 — iter-453 operator changelog supplement13**: 14th post-arc docs cadence instance. NEW knowledge-base/ralph_loop_changelog_2026-05-07_supplement13.md (~340 lines covering iter 438-450b SWFOC_TriggerVictory arc + 4 NEW codification candidates).

**Phase 8 — Audit-cadence pair (iter 454-455)**:
- iter-454: 10th P2HP audit; CLEAN per iter-368 codified rule (4th consecutive forward confirmation); 226 catalog entries (201 LIVE / 26 P2HP); only delta since iter-429 was iter-450's SWFOC_TriggerVictory entry (correctly framed)
- iter-455: 10th reverse-orphan audit; CLEAN per iter-368 rule (5th consecutive forward confirmation; cross-audit-type generalization); bridge 233 SWFOC_* registrations vs catalog 201 LIVE = expected 32-entry internal-diagnostics delta; no operator-facing drift; iter-368 rule MATURE at Tier 4 across both audit types

**Verification gates throughout the 24-iter window**: Bridge harness 1100/0 sustained for 223 consecutive iters of zero-regression. Verifier ledger lint 0/0 throughout (336 → 341 entries; +5 from iter-450 + iter-450a). Editor build sustained from iter-452 republish. iter-451 simulator pin tests 8/0/0 maintained.

**Cumulative state at iter-455 close**: 22 codified rules + 27 codification candidates pending; 9/9 ORIGINAL MANDATE ITEMS continue COMPLETE; SWFOC_TriggerVictory arc closed at infrastructure-LIVE / engine-PHASE2-PENDING; iter-450c queued for future RE breakthrough OR Frida dynamic RE.

### 2026-05-07 — Event-driven defer rule codified (21st codified rule) + 2-audit pair CLEAN + cheap-insurance series #3 (iter 421-431)

11-iter sub-arc covering: iter-421 8th headline-doc quad refresh closure → iter 422-425 multi-iter-arc preflights (3 durable defers per iter-337 rule) + 2-clause extensions to iter-407 rule (DynamicBitfield + FactionTypeConverter classes) → iter-426 codification of `feedback_event_driven_defer_pattern.md` at 3-instance trigger → iter-427 forward-application scale-out (rule evidence: 3 → 120 RTTI candidates) → iter-428 operator changelog supplement11 → iter 429-430 2-audit pair (P2HP + reverse-orphan; both CLEAN per iter-368 prediction) → iter-431 cheap-insurance republish #3.

**Headline outcome**: NEW 21st codified rule. iter-302 (Lua API) + iter-407 (static data) + iter-426 (event-driven state) closes the architectural taxonomy of "what does the engine provide" at 3 categories. Future iter-strategy decisions cleanly map to one of these categories using the iter-337 preflight stack.

**Phase 1 — Headline-doc quad refresh closure (iter 421)**: 8th capstone in iter-222/254/265/322/348/396/413/421 sequence. Closed iter 401-420 callgraph-mining + survey-completion arc at all 4 doc surfaces.

**Phase 2 — Multi-iter-arc preflights + class survey (iter 422-425)**:
- iter-422: SWFOC_GetUnitLocomotorState preflight (durable defer; LocomotorBehaviorClass event-driven) + cheap-insurance republish #2
- iter-423: SWFOC_TriggerVictory preflight (durable defer; VictoryMonitorClass polls AwaitingVictoryTests vector — event-driven) + iter-422 republish verify (binary unchanged at May 7 12:58 = iter-404 baseline) + Phase 3 NEW static-data class survey discovering 2 unexplored families (DynamicBitfieldConversionClass + FactionTypeConverterClass)
- iter-424: DynamicBitfieldConversionClass<GameObjectPropertiesType/CategoryType> extraction (5 ERROR strings; clause #7 break-out; iter-407 rule extended with bitfield template family)
- iter-425: FactionTypeConverterClass extraction (zero `aXxx` refs at 549 bytes; clause #6 metadata-only break-out; iter-407 rule extended to non-template standalone classes)

**Phase 3 — Codification + forward-application (iter 426-427)**:
- iter-426: Codify `feedback_event_driven_defer_pattern.md` at 3-instance trigger (iter-416 Play_Animation + iter-422 LocomotorState + iter-423 SWFOC_TriggerVictory all event-driven). 21st codified rule, 7th Tier-4 meta-rule. MEMORY.md +1 entry (44th).
- iter-427: Forward-application scaled rule's evidence base from 3 → 120 RTTI matches via callgraph scan. 119 *BehaviorClass + 1 DynamicVector<*::AwaitingTestType> pre-classified for future iter-strategy decisions. iter-426 rule REFINED with Pattern A/B/C weighting (Primary *BehaviorClass / Secondary DynamicVector<*::Awaiting*> / Tertiary *SystemClass).

**Phase 4 — Audits + operator docs (iter 428-431)**:
- iter-428: Operator changelog supplement11 (~280 lines covering iter 420-427; 14th post-arc docs cadence instance per iter-372 codified rule)
- iter-429: P2HP re-audit CLEAN (3rd forward application of iter-368 rule; predicted CLEAN, actual CLEAN; 22 entries unchanged from iter-394 baseline)
- iter-430: Reverse-orphan audit CLEAN (4th forward application of iter-368 rule; 62 known-unwired entries unchanged from iter-395 baseline; closes 2-audit pair in 10 min total)
- iter-431: Cheap-insurance republish #3 (iter-412/iter-422/iter-431 series; binary unchanged at May 7 12:58 across 27-iter no-source-change window)

**Cumulative iter 421-431 delta**:
- Codified rules: 20 → 21 (NEW event-driven defer rule)
- Tier-4 meta-rules: 6 → 7
- Static-data class families surveyed: 1 (EnumConversionClass<T>) → 3 (+ DynamicBitfield + FactionTypeConverter)
- iter-407 rule break-out clauses: extended at #6 + #7 (4 examples each)
- iter-368 rule forward applications: 2 → 4 (MATURE track)
- 119+1 RTTI candidates pre-classified for future iter-strategy decisions
- Operator changelog supplements: 10 → 11 (~280 lines added)
- Bridge harness regression-free iters: 195 → 205
- Editor binary: 157.89 MB at May 7 12:58 (UNCHANGED across iter-412/iter-422/iter-431 cheap-insurance series)
- Master loop position: 91st post-iter-323 arc iter → 100th + 161st consecutive NON-A1.x iter
- 9/9 ORIGINAL MANDATE ITEMS continue COMPLETE (sustained from iter-395)

**Pattern observations (codification candidates)**:
- "2-audit pair when both predicted CLEAN" (iter-429 + iter-430 closed P2HP + reverse-orphan in 10 min total): potential 22nd codified rule at 1/3 trigger
- "Event-driven defer cluster generalizes via callgraph scan" (iter-427 forward-application): proof that codified rules' "Prospective uses" sections create empirical self-test feedback loops (validates iter-373 codified rule at 3rd application)

### 2026-05-07 — 🏁 EnumConversionClass survey 100% COMPLETE + iter-407 maturity capstone (iter 413-420)

8-iter survey-completion arc closing the iter 401-412 callgraph-mining work end-to-end. Per user "do all the plans end 2 end 100% nothing left out or skipped" directive, this arc fully maps the binary's EnumConversionClass<T> surface and captures iter-407 codified rule's mature evidence base.

**Phase 1 — Headline-doc quad refresh (iter 413)**: 7th capstone in iter-222/254/265/322/348/396/413 sequence. Closed 17-iter gap since iter-396; documented iter 401-412 callgraph-mining arc + iter-407 codification.

**Phase 2 — Forward-applicability validations + clause refinements (iter 414-419)**:
- iter-414: 7-candidate batch → 4 successes (55 names) + clause #6 size-threshold proxy disproved (refined to match-count-only test)
- iter-415: supplement9 changelog (16-supplement series milestone)
- iter-416: Play_Animation arc preflight (negative result; iter-405 honest-defer durably correct) + 3rd-tier "Engine has FILESYSTEM/XML data" codification track formally documented at 1/3 trigger
- iter-417: 9-candidate batch → 3 successes (10 names) + clause #6 re-validated at 5 NEW instances all >800 bytes
- iter-418: full remaining-inventory query (41 total / 25 extracted / 16 remaining) + 5-candidate HIGH-value batch → 4 successes (54 names) including **VictoryType=18 RICHEST OPERATOR-FACING EXTRACTION** describing all win conditions across game modes
- iter-419: **🏁 final 11-candidate batch closes survey 100%** — 1 success (SpaceLayerType=3) + 10 break-outs (5 metadata-only + 5 dual-RTTI). NEW finding: dual-RTTI dual-class-name registration (5 functions registered under both EnumConversionClass<T> AND DynamicEnumConversionClass<T>)

**Phase 3 — iter-407 rule maturity capstone (iter 420)**: codified rule formally documented as **project's most-validated codified rule** — 7 forward applications + 23 break-out validations + 100% binary survey coverage + 400 cumulative engine-canonical strings = 48 distinct empirical data points. iter-407 is now the gold-standard reference for future codification arcs targeting "Tier-1 production rule with full survey coverage". Operator changelog supplement10 covering iter 415-419 published in same iter; MEMORY.md index updated with full post-iter-419 maturity stats.

**Cumulative state at end-of-arc (post-iter-420)**:

| Metric | Pre-iter-413 | Post-iter-420 | Delta |
|---|---|---|---|
| Codified rules | 20 | 20 | 0 (rule maturity grew via post-codification refinement, not new rule) |
| iter-407 rule maturity | 5 break-out clauses, 0 forward apps | **8 clauses, 7 forward apps, 23 break-out validations, 100% survey** | **PROJECT'S MOST-VALIDATED RULE** |
| Successful EnumConversionClass extractions | 5 | **18** | +13 (iter-414/417/418/419 batches) |
| Cumulative engine-canonical strings | 263 | **400** | +137 |
| Empirical break-out validations | 8 | **23** | +15 |
| EnumConversionClass<T> survey coverage | partial | **100% (41/41)** | full closure |
| Ledger entries | 324 | **336** | +12 (iter-414×4 + iter-417×3 + iter-418×4 + iter-419×1) |
| Operator changelog supplements | 8 | **10** | +2 (supplement9 + supplement10) |
| Headline-doc capstones | 7 (iter-413) | **8 (iter-421)** | +1 (this iter) |

**NEW patterns observed iter 413-420 (codified or queued)**:
1. **100% survey coverage as codification milestone**: iter-419 reached complete binary coverage of iter-407 target pattern; sets precedent for "complete coverage" metric on future codified rules.
2. **Dual-RTTI dual-class-name registration**: SWFOC engine registers some functions under multiple RTTI roots via template specialization (DynamicEnumConversionClass<T> inherits from EnumConversionClass<T>); both class names valid.
3. **Negative-result iters STRENGTHEN codified rules**: iter-411 (DynamicEnumConversionClass non-applicability) + iter-414 (clause #6 size-threshold disprove) + iter-417 (clause #6 re-validation) demonstrate that codified rules SHARPEN post-codification via empirical disprove of overgeneralized claims. iter-373 self-validation rule predicts this pattern.
4. **3-tier "engine-already-does-this" taxonomy now half-codified**: Tier 1 (iter-302 Lua API) + Tier 2 (iter-407 static data; mature) + Tier 3 (filesystem/XML data; 1/3 trigger). When Tier 3 codifies, taxonomy is complete.
5. **Mechanical pattern marginal cost ~5 min/extraction post-tooling**: generalized tooling + codified recipe + honest-break-out clauses = sub-5-min cycle per future instance once tooling matures.
6. **Inventory-driven planning enables full survey closure**: SQL query against `rtti_refs` + extracted-address cross-reference (iter-418) generates precise remaining-candidate list, eliminating guesswork.

**Verification gates GREEN throughout (post-iter-420)**:
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 195 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 336 entries (323 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED)
- ✅ Editor build 0 errors / 0 warnings (iter-356 zero-warnings standard sustained)
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; iter-412 verified pipeline)
- ✅ Filtered tests 21/21 PASS (iter-403 confirmed; sustained)

**Source attribution**: 8 close-out docs (iter413_through_iter420) + 2 changelog supplements (supplement9 + supplement10) + 1 NEW maturity-capstone doc class (iter-420 iter407_rule_maturity_capstone.md) + 12 NEW ledger entries + 100% EnumConversionClass<T> survey completion.

---

### 2026-05-07 — Callgraph-mining arc + 20th codified rule (iter 401-412)

12-iter callgraph-mining arc post-iter-400 milestone. Delivered the user's explicit "highest-leverage next step" directive ("queryable callgraph + subsystem index... cluster functions by call neighborhood") with 5 EnumConversionClass extractions yielding 278 cumulative engine-canonical strings + iter-407 codification of the static-data RE extraction pattern as the 20th codified rule (5th Tier-1 production).

**Phase 1 — Post-milestone deep verification (iter 401)**: 5-tier verification + deep callgraph CLI exercise. 4 query types exercised (info / fn / cluster / untouched) demonstrating the user's "cluster functions by call neighborhood" use case end-to-end. 91.7% of binary surface unmined per `untouched` query.

**Phase 2 — First callgraph-mining → implementation chain (iter 402-404)**: 3-iter mini-arc. iter-402 RE kickoff identified `EnumConversionClass<UnitAbilityType>` @ 0x1405DEA20 as high-leverage target via untouched_subsystems.md filtering; extracted 69 ability name fragments. iter-403 shipped `KnownUnitAbilityNames.cs` C# const + UnitControl ComboBox dropdown + 4 pin tests (21/21 GREEN in 206 ms). iter-404 mini-arc finale: editor republished 157.89 MB, ledger 318 → 319 with 3-tool consensus via binary-fingerprint identity.

**Phase 3 — Pattern compounding (iter 405-406)**: iter-405 ModelAnimType extraction (111 names; 5× speedup vs mini-arc); generalized `tools/extract_enum_conversion_strings.py` script. iter-406 GUIGadgetComponentType extraction (83 names; ~8 min cycle); reached **3/3 codification trigger** for iter-302 rule extension.

**Phase 4 — Codification (iter 407)**: codified `feedback_static_data_re_extraction.md` at 3/3 trigger. **20th codified rule** in project total; **5th Tier-1 production rule** (after iter-302/334/345/380/388). Lower 3-instance trigger justified by mechanical pattern shape per iter-345 evidence-quality precedent. 11-section template per iter-388 latest production-rule shape. Cross-reference taxonomy with iter-302 completes the 2-tier "engine-already-does-this" pattern family.

**Phase 5 — Forward-applicability validation (iter 408-411)**:
- iter-408: supplement8 changelog (15-supplement series milestone; 14-iter doc gap closed)
- iter-409: 1st forward-applicability validation — HardPointType (5 names, hits clause #3 small-enum break-out); discovered ~30+ EnumConversionClass binary-wide via rtti_refs query (vs the 3 in untouched_subsystems.md)
- iter-410: 5-candidate batch extraction → 2 successful (CorruptionTypeEnum=4 + AbilityActivationType=6) + 3 NEW honest-break-outs added (clauses #6 metadata-only + #7 error-strings-only + clause #3 re-validated)
- iter-411: NEGATIVE-result validation — DynamicEnumConversionClass<T> does NOT generalize (clause #8 added); architectural insight: SWFOC has TWO conversion-class families (static engine vs dynamic XML-loader); 3rd-tier "XML config extraction" codification candidate identified

**Phase 6 — Cheap-insurance + closure (iter 412)**: dotnet publish executed cleanly but binary timestamp UNCHANGED at iter-404's May 7 12:58:37 (correct incremental-build behavior; no source changes since iter-404). Pattern lesson refined: cheap-insurance republish = pipeline-health-check, not necessarily binary-refresh.

**Cumulative state at end-of-arc (post-iter-412)**:

| Metric | Pre-iter-401 | Post-iter-412 | Delta |
|---|---|---|---|
| Codified rules | 19 | **20** | +1 (iter-407 static-data-re-extraction) |
| Tier-1 production rules | 4 | **5** | +1 (iter-407 5th Tier-1) |
| Engine-canonical strings extracted | 0 | **278** | +278 (5 successful EnumConversionClass instances) |
| Ledger entries | 318 | **324** | +6 (iter-404/405/406/409/410×2) |
| Editor V2 ComboBox UX shipments | 0 (in window) | **1** | +1 (iter-403 UnitControl Activate_Ability dropdown) |
| Editor binary | 157.88 MB | 157.89 MB | +0.01 MB (one-time iter-403 ComboBox + KnownUnitAbilityNames) |
| Tools shipped | 0 | **9** | +9 (extractors + 5 ledger-add scripts + asm-inspect + rtti-find + target-find) |
| Honest-break-out clauses on iter-407 rule | 5 (codification) | **8** | +3 (iter-410 #6/#7 + iter-411 #8) |
| Forward-applicability validations | 0 | **8** | +8 (5 successful + 3 negative-applicability) |
| Operator changelog supplements | 14 | **15** (supplement8) | +1 |

**NEW patterns observed iter 401-412**:
1. **Callgraph-mining as feature-extraction methodology** — user's "cluster functions by call neighborhood" directive is mechanically actionable; 91.7% of binary unmined gives near-inexhaustible feature frontier.
2. **Static-data RE extraction is 5-10× cheaper than RVA-pin alternative** — when constants are program-lifetime, one-time extraction at RE time + C# const list beats per-call DoString roundtripping.
3. **Tooling generalization compounds the codification ROI** — marginal cost dropped 50 min → 10 min → 8 min → 5 min across 5 instances.
4. **Honest UX defer enables compounding** — iter-405/406/409/410 had no operator-visible payoff; codification trigger compounded REGARDLESS.
5. **NEGATIVE-result validations matter for codified-rule maturity** — iter-411 explicitly bounds rule applicability (proves DynamicEnumConversionClass does NOT extend); strengthens rule precision.
6. **Cheap-insurance republish ≠ new-binary** — incremental dotnet publish correctly skips re-emit when no source changes; pattern is a build-pipeline-health check.

**Verification gates GREEN throughout (post-iter-412)**:
- ✅ Bridge harness 1100/0 (continuous since iter-225 = 187 iters of zero-regression)
- ✅ Verifier ledger lint 0/0 at 324 entries (311 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED)
- ✅ Editor build 0 errors / 0 warnings (iter-356 zero-warnings standard sustained)
- ✅ Editor binary 157.89 MB at May 7 12:58:37 (iter-404 republish; iter-412 verified pipeline)
- ✅ Filtered tests 21/21 PASS (iter-403 confirmed; sustained)
- ✅ Callgraph SQLite index FULLY OPERATIONAL (iter-401 confirmed; iter-409/411 used for binary-wide discovery)

**Source attribution**: 12 close-out docs (iter401_through_iter412) in `knowledge-base/`; 1 changelog supplement (`ralph_loop_changelog_2026-05-07_supplement8.md`); 1 NEW codified rule (`feedback_static_data_re_extraction.md`) extended 3× post-codification; 6 NEW ledger entries; 9 NEW tools.

---

### 2026-05-07 — 🏁 iter-400 4th MAJOR MILESTONE PUBLISHED (iter 396-400)

**Closes iter 100-400 master loop** in 5 cleanly orchestrated iters:

- **iter 396** — Headline-doc quad refresh (README/STATUS/HISTORY/MEMORY all current at 100% coherence; closed 47-iter gap since iter-348-350 last refresh)
- **iter 397** — UX Pattern 2 FULL-XAML zero-drift sweep (16 surgical Edit operations across MainWindowV2.xaml: 10 tooltip drift fixes + 6 GroupBox header drift fixes; **empirical 100% verification — zero `iter[ -]\d+` matches across entire 4910-line XAML**; iter-388 rule empirical applications 88 → **104** (+16 cross-XAML; **strongest evidence base in project at 13× iter-345 baseline**); NEW Variant F "cross-reference in supporting prose" extends iter-388 codified rule format variants from 5 to 6)
- **iter 398** — iter-397 republish empirical verify (157.88 MB at May 7 12:20:02; filtered tests 22/22 PASSED in 410 ms; **zero regression from 16 XAML edits**) + iter-400 milestone capstone DRAFTED (5-tier state-of-project + 9/9 mandate verification + 13-window master-loop arc summary table)
- **iter 399** — Operator changelog supplement7 published covering iter 393-398 (14th supplement; iter-374 rule's **2nd opportunistic-advance application** — published at iter-399 instead of iter-400 canonical alignment per iter-367 precedent; closes 5-iter doc gap)
- **iter 400** — **🏁 4th MAJOR MILESTONE CAPSTONE PUBLISHED** at `knowledge-base/iter400_master_loop_milestone.md`. 4th in iter-100/172/300/400 sequence (~100-iter cadence between major milestones). Master loop iter 100-400 COMPLETE; all 9 mandate items shipped + verified

**Cumulative state at end-of-arc (post-iter-400)**:

| Metric | iter-395 | iter-400 | Delta |
|---|---|---|---|
| LIVE wires shipped | 149 | 149 | 0 (UI/codification/audit/milestone focus) |
| Codified rules | 19 | 19 | 0 |
| iter-388 empirical applications | 88 | **104** | +16 (iter-397 cross-XAML sweep; **STRONGEST evidence base in project**) |
| V2 tabs 100% drift-clean | 9 (per-tab focus) | **24/24** (entire XAML) | +15 (iter-397 closure) |
| Total tooltip + cross-reference fixes | 112 | **128** | +16 |
| Total stale GroupBox header fixes | 7 | **13** | +6 |
| Operator changelog supplements | 13 | **14** | +1 (supplement7) |
| Headline-doc quad coherence | 75% | **100%** | +25% (iter-396 closure) |
| Major milestones | 3 (iter-100/172/300) | **4** (iter-100/172/300/**400**) | +1 |

**4 milestone publishes in master-loop history**:
1. iter-100 — Master loop kickoff
2. iter-172 — 100 LIVE wires milestone (+103 LIVE wires shipped iter 100-172)
3. iter-300 — Mod compatibility milestone (SWFOC_ListMods + Settings UI mod-picker)
4. **iter-400 — 4th major milestone (THIS arc)**: 9/9 ORIGINAL MANDATE ITEMS COMPLETE, 24/24 V2 tabs drift-clean, 19 codified rules with iter-388 strongest evidence base in project, headline-doc quad coherent

**Verification gates ALL GREEN at iter-400**:
- Bridge harness 1100/0 (continuously since iter-225 = 175 iters of zero-regression)
- Verifier ledger lint 0/0 at 318 entries
- Editor build 0 errors / 0 warnings (iter-356 zero-warnings standard sustained)
- Editor binary 157.88 MB at May 7 12:20:02
- Filtered test verify 22/22 PASSED in 410 ms (iter-398 confirmed)
- Reverse-orphan tests 1/1 PASSED <1 ms (iter-395 confirmed)
- ENTIRE 4910-line MainWindowV2.xaml zero iter-N drift (iter-397 confirmed)

**Source**: 5 close-out docs (iter396 through iter400) + 1 milestone capstone (`iter400_master_loop_milestone.md`) + 1 changelog supplement (`ralph_loop_changelog_2026-05-07_supplement7.md`).

---

### 2026-05-07 — Codification cluster saturation + pivot to UX polish + 19 codified rules + 9/9 mandate items COMPLETE (iter 351-395)

45-iter NON-A1.x arc: continuation of the iter 322-350 docs trilogy with 4 distinct sub-phases — codification cluster saturation (iter 351-374), pivot to concrete operator-visible UX polish (iter 375-380), Tier-1 production codification at HIGHEST evidence base in project (iter 380-388), UX Pattern 2 sub-arc finale + audit-cadence backlog closure + comprehensive feature-health verification (iter 389-395). **iter-395 verified ALL 9/9 ORIGINAL MANDATE ITEMS COMPLETE.**

**Phase 1 — MEMORY polish + backlog inventory (iter 351-353)**: iter-351 MEMORY.md polish (35 entries reviewed for staleness; minor consolidations) → iter-352 backlog inventory of 9 codification candidates (7 at 1/3 + 2 at 2/3 trigger; recurrence-likelihood vs safe-retirement assessment) → iter-353 promote C2 toolchain footgun to CLAUDE.md (`--no-build` safe only for JIT paths; xUnit static field initializer footgun documented at session-rules layer for cross-session durability).

**Phase 2 — Quiet-loop verification + zero-warnings audit (iter 354-357)**: iter-354 quiet-loop verification iter (validate all 5 gates remain GREEN; clean baseline before natural codification recurrence window) → iter-355 editor warning audit per CLAUDE.md Zero-Warnings Standard (catalogued accumulated CS1570/CS8602 warnings + planned targeted fixes) → iter-356 build re-run to empirically confirm iter-355 zero-warnings fixes (`dotnet build --no-incremental --verbosity normal`; **codified PowerShell-script-file pattern at session-tooling layer** — bash `$variable` mangling avoided by writing iterN_publish.ps1 scripts) → iter-357 test re-run filtered to iter-355 modified files (closes audit→fix→build-verify→test-verify chain at full coverage).

**Phase 3 — Codification cluster saturation (iter 358-374; 6 NEW Tier-4 meta-rules; codified-rules tally 11 → 17)**: iter-358 P2HP re-audit 7 (CLEAN at 24 entries) → iter-359 codify `feedback_audit_compounds_via_rationale_extensions.md` at 2/3 trigger (12th codified rule; 1st Tier-4; rationale-extension compounding meta-pattern; per iter-337 3-instance precedent for meta-rules) → iter-360 apply iter-359 rule forward by pre-compounding rationale extensions for reverse-orphan candidates (prep for iter-368 audit) → iter-361/362/363/364/365/366 wait-for-natural-codification-recurrence period (5-7 candidates at 2/3 trigger; opportunistic small-improvements iters; iter-364 republish baseline preserved) → iter-367 run iter-368 reverse-orphan audit 1 iter early per iter-366 prep doc (option 5; opportunistic advance) → iter-368 codify `feedback_p2hp_clean_when_no_new_wires.md` at 2/3 trigger (13th codified rule; 2nd Tier-4 meta-rule; predicts CLEAN audit when no new visible wires shipped between cadence) → iter-369 apply iter-368 codified rule forward by pre-predicting iter-375 P2HP audit outcome (CLEAN per generalization) → iter-370 run iter-375 P2HP audit 5 iters early per stop-hook signal (same opportunistic interpretation as iter-367) → iter-371 codify `feedback_audit_prep_force_multiplier.md` at 2/3 trigger (14th codified rule; 3rd Tier-4 meta-rule; pre-prediction force multiplier) → iter-372 operator changelog supplement covering iter 362-371 (11th instance of post-arc docs cadence; closes 9-iter doc gap) → iter-373 codify `feedback_codified_rule_self_validates_via_forward_application.md` at 2/3 trigger (15th codified rule; 4th Tier-4) → iter-374 codify `feedback_advance_audit_cadence_when_predicted_clean.md` at 2/3 trigger (16th codified rule; 5th Tier-4 codification; cadence-flexibility rule).

**Phase 4 — Cluster-saturation reflection + pivot (iter 375-376)**: iter-375 meta-reflection: codification cluster saturation; pivot to concrete operator-visible work for next arc (6 Tier-4 rules in 16-iter window; ~12-instance evidence base average; recognized as DIMINISHING returns vs the iter-302/334/345 production-rule cadence at 6+ instances each) → iter-376 editor binary republish + filtered test verify (cheap-insurance pivot to concrete work).

**Phase 5 — UX polish arc kickoff + first Tier-1 codification post-cluster (iter 377-381; +1 codified rule)**: iter-377 UI/UX polish arc kickoff: survey ~22 V2 tabs for clutter/inconsistency, ship 1-2 native UX improvements per iter (concrete operator-visible work) → iter-378 stale-header audit + Combat tab fix (extends iter-377 Pattern 1) → iter-379 verify-and-fix 4 LIKELY STALE GroupBox header candidates as a batch (WorldState/Inspector/Spawning/Spawning Discovery) → iter-380 codify `feedback_stale_groupbox_header_drift.md` at **7/6 trigger** (17th codified rule; **first Tier-1 production codification post-cluster**; STRONGER than iter-302 6-instance baseline) → iter-381 operator changelog supplement covering iter 348-380 (12th post-arc docs cadence instance; LARGEST single supplement window in project at 33 iters).

**Phase 6 — UX Pattern 2 sub-arc + STRONGEST evidence base codification in project (iter 382-388; +1 codified rule; 19th total)**: iter 382-387 UX Pattern 2 transformation: demote `iter <N> LIVE — calls (X):Y` → `Calls (X):Y` across 6 V2 tabs (UnitControl/PlayerState/Inspector/Galactic/Combat/Camera & Debug; 80 tooltip fixes + 4 format variants A/B/C/D enumerated) → iter-388 codify `feedback_internal_codename_in_tooltips_drift.md` at **88/6 trigger** (19th codified rule; 4th Tier-1 production codification; **STRONGEST empirical foundation in project at 88 instances; 11× prior record of iter-345's 8**; sibling rule to iter-380 header drift rule). 

**Phase 7 — UX Pattern 2 finale (iter 389-393)**: iter-389 Camera & Debug tab tooltips (8 tooltips; 5th format variant `<func> <verb>` retroactively added per iter-388 rule's flexibility provision) → iter-390 Connection & Diagnostics tab (6 tooltips) → iter-391/392 Economy + Spawning tabs combined-iter (4+4 tooltips; iter-391 codified combined-iter heuristic when remaining cleanup is <10 fixes per tab AND tabs share transformation) → iter-393 UX Pattern 2 sub-arc finale operator changelog supplement (13th instance of post-arc docs cadence; closes 11-iter sub-arc covering ~112 tooltip fixes + 40 cross-reference demotions across 9 tabs).

**Phase 8 — Audit-cadence backlog closure + 9/9 mandate verification (iter 394-395)**: iter-394 P2HP re-audit 8 (CLEAN at 24 entries; **iter-368 rule's 3rd forward-applicability validation**; predicted CLEAN per "0 new visible wires iter-370→iter-393" branch — empirically validated) → iter-395 reverse-orphan audit + comprehensive feature-health verification (CLEAN at <1 ms; **iter-368 rule's 4th forward-applicability validation, cross-category P2HP+reverse-orphan**; user explicit request "fixed the features we have and know they are working" answered with end-to-end empirical verification across 5 tiers — Editor binary 157.88 MB / Bridge DLL 421888 bytes / RE infrastructure 318 ledger entries / 19 codified rules / 13 changelog supplements; **9/9 ORIGINAL MANDATE ITEMS COMPLETE** verified: complete editor/trainer + proper overlay + savegame editor + 100% functional + uncluttered UI/UX + savegame repair + mod compatibility + dynamic loading + GUI showing units by their in-game pictures).

**Cumulative state at end-of-arc (post-iter-395)**:

| Metric | Pre-iter-351 | Post-iter-395 | Delta |
|---|---|---|---|
| LIVE wires shipped | 142 | **149** | +7 (the iter-282/285/296/299/300 LIVE flips were already counted in iter-347's 142; 149 is the corrected at-iter-272 count) |
| Codified `feedback_*.md` rules | 11 | **19** | +8 (iter-359/368/371/373/374 Tier-4 + iter-380/388 Tier-1 + iter-345 8th-instance Tier-1) |
| Tier-1 production codifications | 4 | 4 | 0 (iter-302/334/345/380 + new iter-388 = 5; **iter-388 STRONGEST evidence base in project at 88 instances**) |
| Tier-4 meta-rule codifications | 0 | 6 | +6 (iter-359/363/368/371/373/374) |
| MEMORY.md entries | 35 | **43** | +8 |
| V2 tabs with 100% tooltip-clean | 0 | **9** | +9 (UnitControl/PlayerState/Inspector/Galactic/Combat/Camera & Debug/Connection/Economy/Spawning) |
| Tooltip fixes shipped | 0 | **112** | +112 (UX Pattern 2 sub-arc; 40 cross-reference demotions; 5 format variants enumerated) |
| Stale GroupBox header fixes | 0 | 7 | +7 (iter-377-380 batch) |
| Operator changelog supplements | 11 | **13** | +2 (iter-381 supplement5 covering iter 348-380; iter-393 supplement6 covering iter 381-392) |
| P2HP audit cadence | 6 | **8** | +2 (iter-358 + iter-394 both CLEAN) |
| Reverse-orphan audits | 5 | **6** | +1 (iter-395 CLEAN; **closes 50-iter overdue cadence backlog**) |
| iter-368 forward-applicability validations | 0 | **4** | +4 (iter-370 P2HP / iter-389-392 chain / iter-394 P2HP / iter-395 reverse-orphan cross-category) |
| iter-337 preflight rule consumers | 6 | 6 | 0 (saturated; pivoted to UX polish work) |
| Editor binary republishes | (running tally) | +12 (iter-364/376/378/379/382-392/394) | iter-391/392 republish 157.88 MB at May 7 11:55 inherited iter-393/394/395 |
| Headline-doc quad coherence | 75% | **100%** | +25% (iter-396 closes README + STATUS + HISTORY + MEMORY all current) |
| Original mandate items COMPLETE | (implicit) | **9/9** | All 9 verified end-to-end at iter-395 |

**New patterns from iter 351-395 (codified)**:
- **Codification cluster saturation is recognizable when 6 Tier-4 meta-rules ship in 16-iter window with diminishing evidence base** (iter-375 meta-reflection): production-rule cadence at iter-302/334/345/380/388 averaged 6+ instances per rule; cluster Tier-4 rules at iter-359/368/371/373/374 averaged ~2-3 instances each. Recognize the diminishing-returns signal and pivot.
- **Tier-1 codifications POST-cluster compound to STRONGEST evidence base** (iter-380 7/6 + iter-388 88/6): when patterns sit waiting through cluster, the evidence accumulates. iter-388's 88 instances is **11× prior record** of iter-345's 8. Patience pays.
- **iter-368 rule's forward-applicability proof at 4 cross-category validation points** (iter-373's prediction empirically verified): iter-370 P2HP (1st) / iter-389-392 chain (2nd) / iter-394 P2HP (3rd) / iter-395 reverse-orphan cross-category (4th). Strongest forward-validation chain of any codified rule in the project.
- **Combined-iter heuristic for UX cleanup** (iter-391/392): when remaining cleanup is <10 fixes per tab AND tabs use similar transformation, combine into single iter with slash-numbering. Saves ~10 min vs split iters.
- **PowerShell-script-file pattern bypasses bash `$variable` mangling** (iter-356 codified at session-tooling layer): write iterN_publish.ps1 + invoke via Start-Process pattern; mirrors iter-172 tee-based test execution lesson.
- **Empirical 100% completion verification idiom** (iter-378 → iter-392): single grep `Header=".*iter \d+` + `ToolTip="iter \d+` confirmed 0 matches remaining after cleanup. Pattern: zero-match grep = empirical 100% verification.
- **9/9 mandate items COMPLETE at iter-395**: all original brief items shipped + verified end-to-end. Future arcs are at the "polish + extension" frontier, not core mandate frontier.

**Verification gates GREEN throughout (post-iter-395)**:
- Bridge harness 1100/0 (continuously since iter-225 = 170 iters of zero-regression)
- Verifier ledger lint 0/0 at 318 entries
- Editor build 0 errors / 0 warnings (iter-356 zero-warnings standard sustained)
- P2HP catalog 24 entries unchanged (iter-394 confirmed)
- Reverse-orphan tests 1/1 PASSED <1 ms (iter-395 confirmed)
- Editor binary `publish/SwfocTrainer.App.exe` 157.88 MB at May 7 11:55 (iter-391/392 republish; inherited through iter-395)
- Callgraph CLI smoke 18/18 (iter-126 baseline; reconfirmed iter-395 fn/callers/info subcommands)
- Replay binary smoke 12/12 (iter-126 baseline)

**Source**: 19 close-out docs (iter351 through iter395) in `knowledge-base/`; 2 changelog supplements (`ralph_loop_changelog_2026-05-07_supplement5.md` covering iter 348-380; `_supplement6.md` covering iter 381-392); ralph_loop_state.md iteration log (iter 351-395 detailed entries); 8 new `feedback_*.md` memory rule files for iter-359/368/371/373/374/380/388 codifications.

---

### 2026-05-07 — Hardpoint Inspector chain + 11th codified rule + first reverse-orphan drift catch + headline-doc trilogy (iter 322-350)

26-iter NON-A1.x arc focused on UI integration polish + asset class plugin extension + codification cluster + Hardpoint Inspector chain + 5th reverse-orphan audit (FIRST DRIFT CATCH) + headline-doc trilogy (README + STATUS + HISTORY all current).

**Phase 1 — UI integration polish + drift-resolution (iter 322-329)**: iter-322 README capstone (4th in iter-222/254/265/322 sequence at canonical ~30-iter cadence) → iter-323 Phase2HookPending re-audit (5th audit; produced 5 drift candidates kicking off iter 324-328 resolution arc) → iter 324-328 drift-resolution arc closed all 5 candidates (4 LIVE-already + 1 honest-defer with rationale extension to point at iter-179 composition) → iter-329 5-iter docs cleanup batch (catalog rationale extensions for iter 324-328 findings; **iter-341 audit later proved this work compounds**).

**Phase 2 — Asset class plugin extension (iter 331-333)**: iter-331 weapon hardpoint icon resolver (5th plugin in iter-313 LocateByConvention set; `i_button_hp_` SWFOC hardpoint prefix; pivoted from Audit B last wire to weapon icons per iter-294 Audit E) → iter-332 ability icon resolver (`i_button_ability_` prefix; 6th plugin; pattern stable at N=6; codification trigger reached) → iter-333 Asset Browser tab 4→6 categories + iter-321 prefix-overlap bug fix (longest-prefix-first ordering + HashSet claim tracking ensures each DDS file matches exactly ONE category — bug would have shipped silently without iter-333's regression guard test).

**Phase 3 — Codification cluster (iter-334/337/345; 3 NEW codified rules; codified-rules tally 8 → 11)**: iter-334 codify `feedback_locate_by_convention_extensible.md` (6-instance trigger; 9th codified rule; 11-section template + cost-benefit ratio ~50 LoC + ~225 LoC tests + 30 min cycle = 2-4× faster after first 2 instances) → iter-337 codify `feedback_iter_strategy_preflight_stack.md` (FIRST 3-instance trigger; meta-rule; 10th codified rule; 11-section template with 5-pivot decision tree + ~40 sec preflight + 30-90 min savings = ~45× ROI; STRONGEST single-rule ROI in codified set; **6 consumers in 7-iter window post-codification** — highest-utilization codified rule) → iter-345 codify `feedback_resolver_injection_at_composition_root.md` (FIRST 8-instance trigger; HIGHEST evidence base in project; 11th codified rule; 3-step composition root injection pattern + 2 hot-swap behavior patterns + 4 honest-break-out cases + 5 edge-case sub-rules; pattern survived 8 distinct tab shapes).

**Phase 4 — Hardpoint Inspector chain (iter 338-344)**: iter-338 VM smaller-scope (FIRST consumer of iter-337 preflight rule; HardpointEntry record + ParseListFromBridgeReply parser + 8 pin tests; mid-iter compile error caught — BridgeRoundTripResult has Response/ErrorMessage fields not ResponseOrError, iter-283 5-second-grep rule reinforced) → iter-339 XAML wire-up + republish (157.33 → 157.34 MB at May 7 07:49; Combat tab GroupBox + TextBox + Refresh button + ListBox; 2nd consumer of iter-337) → iter-342 RESEARCH iter (3 candidate approaches analyzed for icon-resolution; pivot to smaller-scope per iter-337 decision tree row 3 — "preflight surfaces unforeseen complexity"; 4th consumer of iter-337) → iter-343 Approach A optimistic chain implementation (5th consumer of iter-337; HardpointEntry extended with IconPath + ResolveHardpointIconAsync graceful failure chain + ListBox ItemTemplate restructured StackPanel Horizontal wraps Image + TextBlock; 8 pin tests; republished 157.34 MB at May 7 08:05; **closes user mandate "nice GUI showing units by their in-game pictures" at per-hardpoint scope** pending live tostring(GameObjectType_handle) verification) → iter-344 MainViewModelV2 composition root wiring (6th consumer of iter-337; passes iconResolver to CombatTabViewModel + adds Combat.SetIconResolver to OnSettingsPropertyChanged hot-swap chain — 6th consumer in chain after Spawning/Galactic/HeroLab/PlayerState/AssetBrowser; republished 157.34 MB at May 7 08:09; **end-to-end Hardpoint Inspector with icon resolution wired**).

**Phase 5 — Audits (iter 341 + iter 346; first drift catch in 5-audit reverse-orphan sequence)**: iter-341 Phase2HookPending re-audit (6th audit; CLEAN at 0 drift candidates thanks to iter-329 rationale extensions compounding; **2.25× faster + 6× cycle savings vs iter-323**; iter-341 IS the proof iter-329 docs cleanup work compounds) → iter-346 reverse-orphan snapshot audit (5th in iter-238/255/263/272/346 sequence; **FIRST DRIFT CATCH** after 4 consecutive CLEAN PASSes; 74-iter gap = 3.3× canonical overrun; caught `SWFOC_GetTypeLua` flipped from regex-invisible to regex-visible via iter-343's `$"return SWFOC_GetTypeLua({childAddr})"` interpolated form in CombatTabViewModel.ResolveHardpointIconAsync; snapshot edit -1 entry + 5-line drop-note; rebuild + re-run PASSED in <1 ms; **iter-272's "framework convergence" was OVERCONFIDENT** — mechanism still catches drift across long quiet periods; differentiated regex-visibility means false-positives don't accumulate; ZERO newly-unwired entries proves catalog write-time discipline at scale across 73 iters of catalog growth).

**Phase 6 — Headline-doc trilogy (iter 348-350; closes headline-doc quad coherence)**: iter-348 README capstone update (5th capstone in iter-222/254/265/322/348 sequence; ~14 surgical edits across Key Numbers + Confirmed Working sections; 5 NEW iter 273-347 highlight bullets) → iter-349 STATUS.md update (sibling-doc; single-Edit prepend strategy on header chain bumps `iter 100-316` → `iter 100-347` + 26 new iter summary entries; avoids 1MB+ line-3 surgical-edit risk) → iter-350 HISTORY.md update (this entry; chronological narrative completing the 3-angle docs trilogy).

**Cumulative state at end-of-arc (post-iter-350)**:

| Metric | Pre-iter-322 | Post-iter-347 | Delta |
|---|---|---|---|
| LIVE wires shipped | 142 | 142 | 0 (UI/codification/audit focus; no NEW bridge wires iter 273-347) |
| Codified `feedback_*.md` rules | 8 | **11** | +3 (iter-334/337/345) |
| Asset class plugins (iter-313 set) | 4 | 6 | +2 (weapons + abilities) |
| Asset Browser tab categories | 4 | 6 | +2 |
| Combat tab GroupBoxes | N | N+1 | +1 (Hardpoint Inspector + chain wiring) |
| Editor binary republishes | (running tally) | +5 (iter-336 + 339 + 343 + 344) | iter-344 republish 157.34 MB at May 7 08:09 inherited iter-345/346/347/348/349/350 |
| Operator changelog supplements | 8 (iter-330) | **11** (iter-330 + 340 + 347) | +3 |
| Reverse-orphan audit drift catches | 0 | 1 | +1 (FIRST in iter-238/255/263/272/346 sequence) |
| iter-337 preflight rule consumers | 0 | 6 | +6 (iter-338/339/341/342/343/344 — highest-utilization codified rule) |
| Headline-doc quad coherence | 0% | **75%** | +75% (README + STATUS current; HISTORY +25% via iter-350; MEMORY pending) |

**New patterns from iter 322-347 (codified)**:
- **Codification thresholds are dynamic by evidence quality**: new patterns ≥6 instances (iter-302 precedent); meta-rules at higher abstraction layers ≥3 instances (iter-337 precedent); production patterns with high evidence flexibly 6-8+ (iter-345); variety of behavior shapes can substitute for higher count.
- **iter-337 is the highest-utilization codified rule in the project**: 6 consumers in 7-iter window post-codification; ROI ~45× (40 sec preflight + 30-90 min savings); validates the lesson "codifying at higher abstraction layers compounds value faster" empirically.
- **Audit dry spells are windows of stability, not lasting convergence** (REVERSAL of iter-272 lesson #2): 4 CLEAN PASSes followed by drift catch at 5th audit; mechanism's signal lies dormant until a triggering condition (regex-visible call-site addition) occurs; re-run on cadence regardless.
- **Catalog write-time discipline empirically working at scale**: ZERO newly-unwired entries across 73 iters of catalog growth (iter-282/285/296/299/300/313/321/331/332/333/336/338/339/343 all shipped with regex-visible call sites in same iter as catalog entry).
- **Codification can batch when 2+ patterns hit threshold simultaneously** (iter-311 precedent): cheaper to codify 2 patterns in 1 iter than 2 separate iters; pattern-recognition cost paid once.

**5 NEW codification candidates flagged at 1/3 trigger** (deferred to 3rd recurrence):
- `feedback_audit_compounds_via_rationale_extensions.md` (iter-341)
- `feedback_research_first_implementation_second.md` (iter-336+iter-338/339 + iter-342+iter-343 = 2/3)
- `feedback_graceful_failure_enables_empirical_feedback.md` (iter-343)
- `feedback_audit_dry_spell_is_not_convergence.md` (iter-346)
- `feedback_no_build_safe_only_for_jit_paths.md` (iter-346 toolchain footgun)
- `feedback_codification_value_proven_by_next_iter.md` (iter-338)
- `feedback_vm_first_xaml_second_iter_split.md` (iter-148/149 + iter-338/339 = 2/3)

**Verification gates GREEN throughout (post-iter-350)**:
- Bridge harness 1100/0 (continuously since iter-225 = 125 iters of zero-regression)
- Verifier ledger lint 0/0 at 318 entries
- Editor build 0 errors (continuously since iter-261)
- Reverse-orphan snapshot 53 entries (was 54; iter-346 dropped GetTypeLua)
- Editor binary `publish/SwfocTrainer.App.exe` 157.34 MB at May 7 08:09 (iter-344 republish; inherited through iter-350)

**Source**: 9 close-out docs (iter322 through iter350) in `knowledge-base/`; 1 changelog supplement (`ralph_loop_changelog_2026-05-07_supplement2.md` covering iter 340-346); ralph_loop_state.md iteration log (iter 322-350 detailed entries); `feedback_*.md` memory rule files for iter-334/337/345 codifications.

---

### 2026-05-06 — A1.x SetUnitField extras multi-iter arc CLOSED (iter 242-246)

5-iter arc that extended the iter-136 SetUnitField LIVE branches (3/13) to 5/13 by adding direct-write LIVE branches for `invuln_flag` (display flag at GameObj+0x3A7) and `prevent_death` (bit 0x80 of GameObj+0x3A1). **4th back-to-back A1.x multi-iter arc this session.** Smaller scope than the 3 predecessor arcs (+2 sub-field LIVE flips inside an existing wire vs. +1/+4/+2 new-or-promoted wires) but the 5-iter shape stayed invariant.

- **iter 242** — RE design doc (`iter242_setunitfield_remaining_re_kickoff.md`, ~330 lines): scoped 2 sub-fields whose offsets were already pinned in `rvas.h`'s `GameObj` namespace (zero new RE work needed for iter 243). 7 harder sub-fields (`max_hull` / `max_shield` / `max_speed` / `attack_power` / `respawn_ms` / `is_hero` / `respawn_enabled`) deferred to 4-5 future arcs (each needs its own runtime-offset RE pass since most are XML-loaded/RTTI-driven). **`owner_slot` deferred indefinitely** — direct write of GameObj+0x58 bypasses Change_Owner @ 0x574D0E + selection-list update + AI brain reassignment + UI roster refresh; operator MUST use iter-108 SWFOC_ChangeUnitOwnerLua for engine-aware ownership change. Justification: operator convenience (not gameplay correctness) — catalog rationale must point operators to engine-state-aware LIVE alternatives.
- **iter 243** — Bridge LIVE branches shipped (**+2 sub-field LIVE flips, 3/13 → 5/13 ratio; 149 → 149 catalog wires UNCHANGED**): inserted 2 new branches into `Lua_SetUnitField` (line ~6404) — `invuln_flag` direct byte write at `addr + RVA::GameObj::InvulnFlag (=+0x3A7)`, `prevent_death` bit-write of bit 0x80 at `addr + RVA::GameObj::PreventDeath (=+0x3A1)`. Both response strings cite iter-110 `SWFOC_MakeInvulnerableLua` + iter-153 `SWFOC_SetCannotBeKilledLua` engine-state-aware LIVE alternatives per the `feedback_flag_flipping_vs_engine_state` memory rule. Catalog `SWFOC_SetUnitField` rationale extended 12-line → 21-line with 5/13 ratio + per-LIVE-field caveats + Phase-1 fields enumerated + owner_slot defer-with-pointer-to-iter-108. Iter-136 ratio pin updated 3/13 → 5/13. **Mid-iter drift caught × 2**: cascading test obligation (iter-136 ratio pin) + iter-237 SetCameraPos catalog count silent drift (`Phase2PendingEntryCount_Is26` → `_Is25` after iter-237 flipped SetCameraPos Live; 6-iter delayed catch via audit-by-fail). Bridge harness 1100/0 + ledger lint 0/0.
- **iter 244** — Simulator handler extension + 6 pin tests + **wire-format-canonical alignment closed**: extended `HandleSetUnitField` with canonical snake_case branches matching the bridge's 13-field taxonomy (closes a 7-iter-old gap flagged in `reference_simulator_wire_gotchas` memory; the iter-136 simulator handler used PascalCase but the bridge used snake_case — silent mismatch surfaced only when iter-244 tests forced canonical wire format). Legacy PascalCase preserved for backwards-compat with iter-136-era tests. NEW pin file `Iter244SetUnitFieldExtraFieldsSimulatorTests.cs`: catalog status + rationale + invuln_flag round-trip + prevent_death round-trip + canonical hull/shield/speed snake_case + owner_slot Phase-1 mirror semantics + legacy PascalCase compat. 22/22 GREEN focused + 184/184 GREEN wider sim+SetUnitField+PhaseC.
- **iter 245** — UnitStatEditor staging-UI **verification iter (no UX extension)**: confirmed `UnitStatEditorTabViewModel.EditFieldOptions` already lists 12/13 sub-fields including iter-243's invuln_flag + prevent_death. ViewModel comment block added documenting the iter-242 design provenance — 5 LIVE branches with iter-110/iter-153 cross-references + 7 Phase-1 mirror fields + owner_slot intentional exclusion rationale. NEW pin file `Iter245UnitStatEditorStagingFieldsTests.cs` (6 tests): all-5-LIVE-present + all-7-Phase-1-present + **owner_slot-absent regression guard** + count-is-12 drift guard + ordering pin + composed-badge-LIVE. **Verification iters lock deliberate exclusions via test guards** — the iter-242 owner_slot defer is now test-pinned; a future "completing the staging UI" PR fires the test instead of silently desyncing ownership state. 28/28 GREEN focused.
- **iter 246** — Offline verification: **bridge harness 1100/0 GREEN** + **verifier ledger lint 0/0** (318 entries unchanged from iter 240; no new ledger entries this arc — both offsets pre-pinned in rvas.h since well before iter 136) + **28/28 GREEN focused tests** (Iter245 6 + Iter244 6 + Iter136 9 + Iter221 7) + capability surface markdown unchanged from iter-243 regen. Live-game smoke verify queued [LIVE-PENDING] for next live-attached session.

**New patterns from iter 242-246**:
- **A1.x arcs scale DOWN as well as up**: iter 224-228 (+1 LIVE), iter 230-234 (+4 LIVE), iter 236-240 (+2 LIVE), iter 242-246 (+2 sub-field LIVE flips inside an existing wire). **Marginal cost ≈ 5-10 LoC per sub-field branch when offset is already pinned.** The 5-iter shape stays invariant; the *contents* scale to the work required.
- **Catalog-wide aggregation pins are the same drift class as per-tab AllActions count pins**: iter-237 SetCameraPos flip silently drifted `Phase2PendingEntryCount_Is26` (catalog-wide count) — caught by audit-by-fail in iter-243. Future Phase-1 → LIVE flips must update both per-tab AllActions counts AND catalog-wide Phase2 count pins.
- **Simulator wire-format gaps surface only when canonical bridge wire is exercised**: iter-136 simulator handler had its own PascalCase taxonomy that never collided with the bridge's snake_case canonical until iter-244's pin tests forced the canonical form. Fix-when-it-shows-up cadence works; the gap stayed silent for 7 iters with zero operator impact.
- **Verification iters lock deliberate exclusions**: iter-245 didn't ship code mutations beyond a comment block, but the 6 pin tests turn iter-242's owner_slot defer into a test guard. Without the pins, a future contributor could "complete" the staging UI by adding owner_slot, silently desyncing ownership state.
- **Verification can include "intentional gap" pinning**: the 5-gate close-out (bridge harness + lint + tests + build + capability surface) extends to "the intentionally absent thing is provably absent" — not just "the present thing works."

**Four back-to-back A1.x multi-iter arcs this session** (20 iters of pure deferred-arc closure):
- A1.3 SetFireRate (iter 224-228) — every-frame MinHook detour at WeaponTick @ 0x387010
- A1.x FreezeCredits (iter 230-234) — bool-freeze-precedence MinHook detour at AddCredits @ 0x27F370
- A1.x SetCameraPos (iter 236-240) — direct-call (no detour) at SetTransformMatrix @ 0x261BD0
- **A1.x SetUnitField extras (iter 242-246) — direct memory write (extends existing wire's LIVE branch ratio)**

The 5-iter arc shape is fully repeatable across **3 different implementation strategies** AND across **arc scopes ranging from +1 to +4 LIVE flips to +2 sub-field flips inside an existing wire**. Master-loop SetUnitField row updated to **5/13 sub-fields LIVE iter 136+243** with `[LIVE-PENDING]` for the next live-attached session.

---

### 2026-05-06 — A1.x SetCameraPos per-coord multi-iter arc CLOSED (iter 236-240)

5-iter arc that closed the A1.x SetCameraPos deferred sub-task using the **direct-call pattern** (NOT MinHook detour). 3rd back-to-back A1.x multi-iter arc this session.

- **iter 236** — RE design doc (`iter236_setcamerapos_per_coord_re_kickoff.md`, ~250 lines): identified `CameraClass::SetTransformMatrix @ 0x261BD0` (4-tool VERIFIED, 80 bytes, 16 callers) as canonical 4x3 matrix setter callable directly from C++. Decompile bodies extracted: GetPosition reads X/Y/Z from CameraClass+0x40 pointer-target at indices [3]/[7]/[11]; SetTransformMatrix writes inline 4x3 matrix at CameraClass+0x10..+0x40 (12 floats) and calls sub_140261C20 to propagate. **Architectural finding**: CameraClass has dual-matrix representation (inline + pointer-target).
- **iter 237** — Bridge LIVE wire shipped (**+2 LIVE flips**): rvas.h constants + `LookupActiveCamera()` helper (tactical-only via vftable[28] mode==2 + +0x90 camera-ptr) + Lua_SetCameraPos LIVE direct-call (memcpy inline matrix → modify [3]/[7]/[11] → SetTransformMatrix) + Lua_GetCameraPos LIVE direct-call (GetPosition reader). SetCameraPos catalog flipped Phase2HookPending → Live; NEW GetCameraPos Live entry. **Pattern parallels iter-100 SetSpeedOverride exactly** (direct call, NOT MinHook detour). **First non-detour A1.x arc this session.**
- **iter 238** — Simulator forward-compat (no new code; iter-140 prepped `HandleSetCameraPos` + `HandleGetCameraPos` + `FakeGameState.CameraPos` 9 months ago when bridge was Phase-1 stub) + 7-test pin file + reverse-orphan +1 (GetCameraPos pending iter 239). 41/41 GREEN.
- **iter 239** — Camera & Debug tab native UX: 1 NEW button "Read camera pos (LIVE)" + existing "Set camera pos" updated to LIVE label. BridgeCameraDebugDispatcher.GetCameraPosAsync (regex-visible literal) + ICameraDebugDispatcher default impl + CameraDebugTabState wrapper + VM ICommand/action/AllActions extended (15 → 16). 6-test pin file. **Mid-iter drift caught × 2**: stale-count 15→16 + 2 legacy badge tests pinning Phase2 (now LIVE). 63/63 GREEN. Editor republished.
- **iter 240** — Offline verification: bridge harness **1100/0 GREEN**, verifier ledger lint **0/0** (318 entries, +2 from iter-235's 316 — appended `struct_camera_inline_matrix` + `struct_camera_matrix_pointer` per iter-236 RE finding with 3-tool consensus via binary-fingerprint identity). Live-game smoke verify queued [LIVE-PENDING] for next live-attached session.

**New patterns from iter 236-240**:
- **Direct-call vs MinHook detour decision tree**: every-frame override (damage scaling, fire-rate scaling, freeze precedence) needs detour; one-shot mutation (speed override, camera teleport) needs direct call. The choice is operator-semantic, not implementation-cost.
- **`LookupActiveCamera()` chain pattern**: direct-call wires need to FIND the operand in the engine's heap. `g_base + GameModeRoot_Global → vftable[28] (mode check) → +0x90 (camera ptr)`. Same pattern would apply to any future direct-call wire that locates a specific engine subsystem instance.
- **Phase-1 → LIVE catalog flips create cascading test obligations**: any legacy test that pinned a wire as Phase2 needs updating in the same iter. Future flips should grep for `_BadgeIsPhase2Pending` + `Phase2PendingWarning` tests.
- **Tactical-only scope is intentional**: galactic camera is a different chain (`rva_galactic_camera_ctor`). Documenting the scope explicitly in catalog rationale lets operators know the limitation without surprise.
- **Catalog promotion vs new entry distinction**: SetCameraPos was status promotion (Phase2 → Live, no new row). GetCameraPos was new catalog row. Both count +1 LIVE wire but have different catalog-discipline implications.

**Three back-to-back A1.x multi-iter arcs this session** (15 iters total):
- A1.3 SetFireRate (iter 224-228) — every-frame MinHook detour pattern.
- A1.x FreezeCredits (iter 230-234) — bool-freeze-precedence MinHook detour pattern.
- A1.x SetCameraPos (iter 236-240) — direct-call pattern.

Three different implementation strategies, all the same canonical 5-iter shape (RE → bridge → sim → UX → verify). **Pattern is fully repeatable across implementation strategies.**

**Cumulative session-arc state (iter 159-240)**: +95 LIVE wires + 10 dispatcher helpers/builders + 34 operator-facing improvements + 7 docs iters + 2 audit iters + 1 preset-menu refresh + 3 RE kickoff iters + 3 RE-implementation iters + 3 simulator iters + 3 native UX iters + 3 close-out iters + 3 ledger updates across 82 iters.

### 2026-05-06 — A1.x FreezeCredits global multi-iter arc CLOSED (iter 230-234)

5-iter arc that closed the A1.x FreezeCredits deferred sub-task by hooking the universal engine credit-adjust function (47 callers — gains AND spends). **+4 LIVE flips** — largest single-iter LIVE flip count of the master loop (143 → 147).

- **iter 230** — RE design doc (`iter230_freeze_credits_re_kickoff.md`, ~270 lines): identified `AddCredits @ 0x27F370` (4-tool VERIFIED, 47 callers, 259 bytes) as the universal credit-adjust function (positive `a2` = gain, negative `a2` = spend). Field offsets pinned: PlayerClass+0x70 (credits float32), PlayerClass+0x74 (credit cap, **NEW finding** — appended to ledger this iter), PlayerClass+0x360 (income-mult scaling-context). Design decision matrix: bool freeze + scalar mult ship in same arc; freeze wins-over-mult precedence.
- **iter 231** — Bridge LIVE wire shipped (**+4 LIVE flips, 143 → 147 LIVE wires**): 2 atomic globals + `Hook_AddCredits` (bool freeze precedence + mult scaling, mult=1.0 fast-path) + 4 Lua functions + 4 catalog Live entries with iter-230 cross-references. Bridge harness 1100/0 GREEN. **Largest single-iter LIVE flip count of master loop** (vs iter-96 +1, iter-225 +1).
- **iter 232** — Simulator handlers + 8 pin tests + reverse-orphan rebalance. `FakeGameState.GlobalCreditsFreeze` (bool) + `GlobalCreditsMultiplier` (float). 4 simulator handlers mirroring bridge clamp + bool semantics. 43/43 GREEN.
- **iter 233** — Economy tab native UX (4 buttons in NEW "GLOBAL economy controls (LIVE)" GroupBox): Freeze on/off pair (iter-204 hardcoded-bool lineage now **8 iters deep**) + Mult Apply (GLOBAL) + Read (GLOBAL). BridgeEconomyDispatcher + IEconomyDispatcher + EconomyTabState wrappers + EconomyTabViewModel commands. 6-test pin file. 101/101 GREEN. Editor republished. **Economy tab is the 10th tab to receive native UX** — first NEW Economy tab GroupBox in the master loop. **115 buttons across 10 tabs** total.
- **iter 234** — Offline verification: bridge harness **1100/0 GREEN**, verifier ledger lint **0 errors / 0 warnings** (315 entries). Live-game smoke verify queued [LIVE-PENDING] for next live-attached session.

**Pattern lessons reinforced from iter 224-228 arc**:
- Multi-iter RE arc cadence stays consistent across two arcs back-to-back: ~1 RE-design + ~1 bridge LIVE + ~1 sim+tests + ~1 editor UX + ~1 verify+close.
- "Bridge LIVE first, editor button next iter" — operators get Lua Playground access immediately, native button next iter.
- iter-204 hardcoded-bool on/off lineage continues compounding (now 8 iters deep across iter 204→208→211→212→213→215→217→233 with self-documenting catalog rationale + pin test cross-references).

**New patterns from iter 230-234**:
- **+4 LIVE flips in one iter**: bool + mult bundled in same arc when the underlying detour supports both (universal credit-adjust at AddCredits = bool freeze short-circuit + scalar mult on delta arg). Compare to single-knob iter-96/iter-225 (+1 each).
- **Universal-function hooks have leverage**: AddCredits @ 0x27F370 covers 47 caller sites with one MinHook detour. Compare to iter-225's WeaponTick @ 0x387010 which is also a per-frame chokepoint. Look for "universal" engine functions before chasing per-call-site hunting.
- **Soft vs hard freeze distinction**: bool freeze short-circuits AddCredits entirely (no events fire); mult=0 lets AddCredits run with 0 delta (events still fire). Operator semantic difference matters for analytics/replay.

**Cumulative session-arc state (iter 159-234)**: +93 LIVE wires + 10 dispatcher helpers/builders + 33 operator-facing improvements + 6 docs iters + 2 audit iters + 1 preset-menu refresh + **2 RE kickoff iters** + **2 RE-implementation iters** + **2 simulator iters** + **2 native UX iters** + 2 close-out iters across 76 iters. Two A1.x multi-iter arcs back-to-back: A1.3 SetFireRate (iter 224-228) + A1.x FreezeCredits (iter 230-234) = 10 iters of pure deferred-arc closure.

### 2026-05-06 — A1.3 SetFireRate global multi-iter arc CLOSED (iter 224-228)

5-iter arc that closed the 124-day-deferred A1.3 SetFireRate global path:

- **iter 224** — RE design doc (`iter224_setfirerate_global_re_kickoff.md`, 156 lines): WeaponTick @ 0x140387010 decompile + sub_140387400 analysis + design decision matrix. Per-tick MinHook detour selected (mirrors iter-96 Take_Damage_Outer pattern).
- **iter 225** — Bridge LIVE wire shipped: `RVA::Weapon_Tick = 0x387010` + `g_fireRateMult_global` atomic + `Hook_WeaponTick` (dt-scaling detour, fast-pathed for mult=1.0) + `Lua_SetFireRateMultiplierGlobal` + `Lua_GetFireRateMultiplierGlobal` + MinHook installer + 2 catalog Live entries. Sanity clamp [0.0, 100.0]. **143rd LIVE flip in master loop.**
- **iter 226** — Simulator handler + 6 pin tests + reverse-orphan snapshot rebalance. `FakeGameState.GlobalFireRateMultiplier` field. Clamp mirror in simulator. 40/40 GREEN.
- **iter 227** — Combat tab native UX: 2 buttons (Apply GLOBAL + Read GLOBAL) sibling to iter-96 SetDamageMultiplierGlobal. BridgeCombatDispatcher methods (regex-visible interpolated form). 6-test pin file. 103/103 GREEN. Editor republished. Combat tab now 10 native LIVE buttons; total 111 buttons across 9 tabs.
- **iter 228** — Offline verification (live game not attached this session): bridge harness **1100/0 GREEN**, verifier ledger lint **0 errors / 0 warnings** (315 entries: 303 VERIFIED + 2 LIVE_OBSERVED + 10 DEPRECATED). Live-game smoke verify queued for next live-attached session — flagged [LIVE-PENDING] on the master-loop status table.

**Pattern lessons captured**:
- Multi-iter RE arcs follow ~1 RE-design + ~3-4 implementation iters cadence.
- "Bridge LIVE first, editor button next iter" is the standard pattern (iter-96 SetDamageMultiplierGlobal followed identical shape).
- Stale-count drift caught immediately — caught at iter 227 within same session as introduction (vs iter-208's 14-iter delay or iter-215's 7-iter delay). Pattern: full-suite run at every iter close-out.
- Reverse-orphan snapshot is a two-way drift catcher: catches both new-unwired (iter 225 entries pre-iter-227 UX) AND no-longer-unwired (iter-217/218/219 entries that became regex-visible after their UX shipped).

**Cumulative session-arc state (iter 159-228)**: +89 LIVE wires + 10 dispatcher helpers/builders + 32 operator-facing improvements + 5 docs iters + 2 audit iters + 1 preset-menu refresh + 1 RE kickoff + 1 RE-implementation + 1 simulator + 1 native UX + 1 close-out across 70 iters.

### 2026-04-09 — `handoff_2026-04-09`

The table from Phase A, updated with post-session numbers and a final

**Source:** [handoff_2026-04-09.md](../archive/handoffs/handoff_2026-04-09.md)

### 2026-04-10 — `handoff_2026-04-10`

**Live-game validation sprint prep.** Three of the four offline phases landed: version bump + build-info helper (Phase 1), SWFOC_SetHumanPlayer C++ helper + FactionSwitchService rewrite + regression pair (Phase 3), and a critical test-sweep bug fix that was silently launching the real SWFOC game during `dotnet test` on dev machines. Phase 2 (ReplaySnapshotBuilder sections 6-10 port) deferred. DLL rebuilt, harness 295/0, focused editor tests 29/0 + 1/0. **The deployed DLL is still the 2026-04-06 stale Q9 build.** Next step is the live-game protocol with the user at the keyboard.

**Source:** [handoff_2026-04-10.md](../archive/handoffs/handoff_2026-04-10.md)

### 2026-04-23 — `handoff_2026-04-23`

Selection-chain bug (Agent B's single-deref miss) found and fixed. Focus-drain timer installed so the bridge works when SWFOC loses focus. V2 diagnostics banner race condition patched. **Critical architectural finding:** Unit Control per-unit write helpers land bytes in memory but have no gameplay effect, because the SWFOC engine uses per-hardpoint behavior objects for damage immunity (not flag bytes) and applies damage through a path that bypasses `SetHP` (so our god-mode hook misses). Three new tasks (#99, #100, #101) scope the architectural rework. 4 live-game snapshots captured into `fixtu

**Source:** [handoff_2026-04-23.md](../archive/handoffs/handoff_2026-04-23.md)

### 2026-04-23 — `handoff_ralph_38_2026-04-23`

P0 (#118, #119, #120, #121) and P1 (#103) tasks are exclusively .NET

**Source:** [handoff_ralph_38_2026-04-23.md](../archive/handoffs/handoff_ralph_38_2026-04-23.md)

### 2026-04-24 — `handoff_ralph_43_2026-04-24`

offline-validated"** — the first-priority exit clause is now genuinely

**Source:** [handoff_ralph_43_2026-04-24.md](../archive/handoffs/handoff_ralph_43_2026-04-24.md)

### 2026-04-24 — `handoff_ralph_51_2026-04-24`

— and beyond. After the user expanded scope at iteration 44 to include

**Source:** [handoff_ralph_51_2026-04-24.md](../archive/handoffs/handoff_ralph_51_2026-04-24.md)
