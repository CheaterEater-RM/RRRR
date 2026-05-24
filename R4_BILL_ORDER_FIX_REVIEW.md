# R4 Bill Ordering Fix — Review

*Source-grounded review of `BILL_ORDER_FIX.md` · RimWorld 1.6 · Harmony 2.4.x*

---

## Verification summary

I cross-checked every technical claim in the proposal against the decompiled 1.6 source. Where the proposal makes a structural claim, it's accurate:

- `WorkGiver_DoBill.JobOnThing` precondition chain matches the proposal: `ThingIsUsableBillGiver` → `AnyShouldDoNow` → `UsableForBillsAfterFueling` → `CanReserve` → `IsBurning` → `CanReserveSittableOrSpot` (for `hasInteractionCell`) → `CompRefuelable.HasFuel` (returns refuel job early) → `RemoveIncompletableBills` → `StartOrResumeBillJob`.
- `StartOrResumeBillJob` is a flat `for` loop with the cheap-check skip list the proposal lists, in that exact order.
- `requiredGiverWorkType` is enforced *per-bill in the loop*, not as a bench-level filter — so a sentinel work type does cause vanilla to skip those recipes on every bench.
- `ReCheckFailedBillTicksRange` is `IntRange(500, 600)`. The cooldown write is gated on `FloatMenuMakerMap.makingFor != pawn`. Both match the proposal.
- `Bill.CompletableEver` defaults to `true` and is only overridden in `Bill_Medical`/`Bill_Mech`, so `RemoveIncompletableBills` won't delete R4 repair/clean bills.
- `recipe.requiredGiverWorkType` has only three readers in the codebase: `StartOrResumeBillJob` (dispatch), `MechWorkUtility.AnyWorkMechCouldDo` (one UI dropdown in `Dialog_BillConfig`), and `RecipeDefGenerator` (copies from `recipeMaker`, irrelevant to XML recipes). The blast radius of switching to `RRRR_BillOnly` is much smaller than feared.

The core thesis — that vanilla single-scanner ordering can't be restored across separate WorkGivers — is correct and well-argued. Approach 5 is the right architectural direction. The pain points below are mostly about the implementation details *underneath* Approach 5, not the choice itself.

One claim worth correcting for the record:

> "If two WorkGivers at the same priority both return a job for the same bench, the last one in iteration order wins (overwrites `bestTargetOfLastPriority`)."

This is half right. Reading `JobGiver_Work.TryIssueJobPackage` (~line 80): inside the loop, as soon as a WorkGiver sets `bestTargetOfLastPriority` AND the bottom-of-iteration `JobOnThing` call returns non-null, it `return`s immediately. The "last wins" case only triggers when an earlier WorkGiver's `HasJobOnThing` (which internally calls `JobOnThing` once) said yes but the second `JobOnThing` returned null — then the next same-tier WorkGiver gets a chance and overwrites. In practice that's still bad enough to break ordering, so the proposal's conclusion stands; the mechanism description is just slightly imprecise.

This is also why **`JobOnThing` is called twice per accepted target** — once inside `HasJobOnThing` (via `WorkGiver_Scanner.HasJobOnThing` calling `JobOnThing != null`), once at the bottom of the iteration. The postfix runs both times. More on that under Pain Point 1.

---

## Major pain points

### Pain point 1 — `HasJobOnThing` calls `JobOnThing` twice per scan, and the postfix has side effects

`WorkGiver_Scanner.HasJobOnThing` is literally `return JobOnThing(...) != null`. `JobGiver_Work` then calls `JobOnThing` *again* on the winning target. Each call runs vanilla's `StartOrResumeBillJob` *and* R4's postfix.

The functional consequences:

1. **Vanilla writes `nextTickToSearchForIngredients` cooldowns on the first pass**. On the second pass, those cooldowns are now set, so vanilla skips those bills via `TicksGame <= bill.nextTickToSearchForIngredients`. Vanilla's pick on pass 2 can be a *lower* bill than on pass 1.
2. **R4's postfix writes cooldowns on R4 bills it tried and failed**. Pass 2 of the postfix sees those cooldowns set and skips those R4 bills.
3. The postfix's choice on pass 2 may be a different bill than pass 1 (lower priority, or fall through to vanilla's pass-2 pick).

Vanilla tolerates this for vanilla bills because cooldowns are idempotent and the search is monotone (cooldown only causes lower priority bills to be picked). R4 inherits the same tolerance for the same reason. So this is *correctness-safe* by happy accident — but it's a real correctness footgun if anyone later changes the postfix to do something non-idempotent (logging, achievement tracking, designation cleanup, etc.).

Also: the postfix's R4 stack scan, ingredient search, and BIS path will run **twice** for every accepted target. That's not free.

**Options:**

- **(1A) Per-pawn/per-bench/per-tick memoization in the postfix.** Key on `(pawn.thingIDNumber, bench.thingIDNumber, Find.TickManager.TicksGame)` → cache the postfix's decision (replaced job or "no replacement"). First call computes; second call replays. Solves both the perf and the non-idempotency footgun.
- **(1B) Memoize only the expensive part** (ingredient search / BIS candidate lookup), let the bill iteration run twice. Slightly less robust but smaller change.
- **(1C) Do nothing and document the assumption** that cooldown writes must remain idempotent and the search must remain deterministic per tick. Acceptable now, fragile later.

**Recommendation: 1A.** The existing per-WorkGiver caches in `WorkGiver_R4RepairBill`/`WorkGiver_R4CleanBill` already key this way; carry the same pattern into `R4BillJobFactory`. The proposal mentions this in passing under "Performance Considerations" but doesn't elevate it to a correctness requirement — it should be.

---

### Pain point 2 — `PassesVanillaBenchPrechecks` is a moving target

The whole "vanilla returned null → R4 must re-check bench preconditions" branch depends on `R4BillJobFactory.PassesVanillaBenchPrechecks` exactly mirroring the precondition block at the top of `WorkGiver_DoBill.JobOnThing`. From source, that block is:

```csharp
ThingIsUsableBillGiver(thing)
  && billGiver.BillStack.AnyShouldDoNow
  && billGiver.UsableForBillsAfterFueling()
  && pawn.CanReserve(thing, 1, -1, null, forced)
  && !thing.IsBurning()
  && (!thing.def.hasInteractionCell || pawn.CanReserveSittableOrSpot(thing.InteractionCell, thing, forced))
  // CompRefuelable: if has comp and !HasFuel, vanilla returned a RefuelJob, not null;
  //                 so R4's null-result path doesn't need to re-check fuel
  && /* RemoveIncompletableBills ran (no fail signal, just a mutation) */
```

But future RimWorld 1.7+/DLC versions can add to this. The DESIGN doc's `MILESTONES.md` lists 1.6 as the target, but `delQ`, `Lookouts`, and others already track multi-version concerns. If 1.7 adds (say) a power check or a new comp-based gate, R4 silently picks up jobs vanilla rejected.

There's a related subtle issue: **vanilla returning null is overloaded.** It can mean:
- All bills are non-runnable (genuine "nothing to do")
- A bench precondition failed (e.g., not reservable)
- A `Bill_Medical`/`Bill_ProductionWithUft`/`Bill_Autonomous` case fell through without producing a job

R4 only wants to act in case 1. Re-checking preconditions handles case 2. But case 3 — vanilla looked at a UFT bill, didn't find the bound UFT, *continued the loop*, and eventually returned null — is harmless because vanilla actually scanned the stack and found nothing. R4 scanning the same stack and finding R4 bills is the right call.

**Options:**

- **(2A) Keep `PassesVanillaBenchPrechecks` and tag the patch with the vanilla version it was authored against.** Add a startup log warning if `VersionControl.CurrentVersion` doesn't match. Cheap and self-documenting.
- **(2B) Use a sentinel scan instead of replicating prechecks.** Add a hidden `RRRR_PrecheckProbe` `RecipeDef` with `requiredGiverWorkType` matching the bench's WorkGiver workType, and have the postfix consult whether `StartOrResumeBillJob` would have visited any bill — if vanilla skipped the whole stack because preconditions failed, the probe wouldn't have been visited either. This is clever but very fragile and adds a dummy recipe to every bench. **Don't recommend.**
- **(2C) Stricter contract: only run R4 when vanilla returned a bill job.** Drop the "vanilla returned null" branch entirely. R4 bills only get dispatched when at least one vanilla bill on the same bench is runnable. **This breaks Scenario 4** ("only R4 repair/clean bills on the bench") — but maybe that's an acceptable constraint? It would mean players have to keep at least one vanilla bill on a bench to enable repair/clean. Probably too painful.
- **(2D) Hybrid: extract the precondition block into a static helper in R4 that calls only public/`AccessTools`-reachable methods on a `WorkGiver_DoBill` instance, and call it from both branches.** Then if 1.7 changes the prechecks, only one helper needs updating. (Effectively 2A with better hygiene.)

**Recommendation: 2A + 2D.** Treat `PassesVanillaBenchPrechecks` as a single-source-of-truth helper, version-stamp it, and call it explicitly when `__result == null`. Add a version-mismatch warning at startup.

---

### Pain point 3 — Other mods cancelling `JobOnThing` with a bool prefix

Common Sense, Better Workbench Management, Pick Up And Haul, and others all patch `WorkGiver_DoBill.JobOnThing`. The relevant case is a **prefix that returns `false`** to skip vanilla and set `__result` themselves.

Harmony 2.x semantics: a `false`-returning prefix skips the original method and any remaining prefixes, **but postfixes still run** with whatever `__result` the cancelling prefix set. So R4's postfix runs in all these cases. That's the good news.

The concerning case: a mod's prefix explicitly sets `__result = null` to *block* work at a bench (e.g., "this pawn shouldn't work here right now"). R4's postfix sees null, treats it as "vanilla found nothing", runs the precheck branch, *passes the precheck* (because the precheck is just a precondition check, not a behavioral block), and creates an R4 job — overriding the mod's block.

This is unavoidable in the general case without a registry of "known blocking prefixes". The proposal mentions this only obliquely under "main compatibility risk to watch in testing".

**Options:**

- **(3A) `[HarmonyPriority(Priority.Last)]` on the postfix.** Doesn't help with cancelling prefixes (they always run before all postfixes) but does help if other mods *also* postfix and rely on R4's choice being final.
- **(3B) Detect a cancelling prefix marker.** Check if `Harmony.GetPatchInfo(...)` shows another cancelling prefix exists, and skip R4's postfix when those mods are loaded. Heuristic, brittle, and effectively a hard-coded mod blacklist.
- **(3C) Settings toggle: "Override mods that block bill work" off by default.** Defensive default. Players who hit conflicts can flip it on knowing the trade-off.
- **(3D) Only run R4 when vanilla returned a *vanilla* `DoBill` job for this stack** — and add a config note that benches with only R4 bills require at least one vanilla bill to enable repair/clean. Same trade-off as 2C; combines well with this concern.
- **(3E) Accept it. Document it. Move on.** This is essentially the proposal's current position.

**Recommendation: 3E for v1, with 3C in the back pocket if testing surfaces real conflicts.** Don't paint yourself into 3B's mod-blacklist corner; that's a maintenance treadmill.

---

### Pain point 4 — `HaulStuffOffBillGiverJob` is run by vanilla *before* `TryStartNewDoBillJob`, but the postfix may need to run it again

When vanilla creates a bill job, it calls `WorkGiverUtility.HaulStuffOffBillGiverJob` and, if the bench is blocked by junk, returns a haul-off job *instead* of a DoBill job (with `job.bill == null`). The proposal correctly preserves these (`__result != null && __result.bill == null` → don't replace).

But: when R4's postfix decides to *create* a job for an R4 bill, **R4 also needs to check whether the bench needs haul-off first**. Otherwise an R4 repair job spawns a pawn who walks to a blocked bench. The existing `WorkGiver_R4RepairBill` / `WorkGiver_R4CleanBill` presumably do this; `R4BillJobFactory` must inherit it. This is mentioned in the proposal under "preserving haul-off behavior" in step 3 of Implementation Sequence, but it's worth elevating: **`R4BillJobFactory.TryCreateRepairOrCleanBillJob` must call `HaulStuffOffBillGiverJob` itself**, exactly as vanilla's `TryStartNewDoBillJob` does.

There's a related subtle question: should an R4 haul-off job preempt a vanilla bill? Vanilla never sees R4 bills, so vanilla wouldn't generate a haul-off triggered by an R4 bill. If R4's pre-job haul-off check finds the same junk that vanilla's already-running haul-off would clear, both jobs target the same thing — reservation conflicts cause one to drop, which is fine. But if R4's haul-off check fires and vanilla returned a *bill* job (not a haul-off), R4 would be replacing vanilla's bill job with R4's haul-off, which is wrong — the pawn should do vanilla's bill if R4 isn't ready.

**Options:**

- **(4A) Only check haul-off when `__result == null`.** If vanilla returned a bill, vanilla already declared the bench is good to work; R4 piggybacks. If vanilla returned null and R4 wants to dispatch, R4 checks haul-off. This is the cleanest.
- **(4B) Always check haul-off; if R4 has a haul-off and vanilla had a bill, return vanilla's bill.** Functionally same as 4A but with redundant work.

**Recommendation: 4A.** Document it as a property of the factory.

---

### Pain point 5 — `Bill_ProductionWithUft`, `Bill_Autonomous`, `Bill_Medical`, `Bill_Mech` paths

`StartOrResumeBillJob` doesn't just match `requiredGiverWorkType` — it also has special branches for UFT bills (returns `FinishUftJob`), `Bill_Autonomous` (returns `WorkOnFormedBill`), `Bill_Medical` (extra ingredient handling, surgery violation check, faction check), and `Bill_Mech` (waste container check).

R4's recipes are presumably regular `Bill_Production`, so they don't trigger any of these. But the proposal's postfix needs to be careful not to *replace* a result from one of these branches. Specifically:

- `FinishUftJob` returns a `JobDefOf.DoBill` with `job.bill = bill_ProductionWithUft`. From the postfix's perspective, this looks like a normal bill job. Its index in the stack is well-defined. R4 could legitimately replace it with an R4 bill above. **This is the desired behavior** — the player put repair above the UFT bill, so repair wins.
- `WorkOnFormedBill` returns a similar shape. Same reasoning — replaceable if R4 is above.
- `Bill_Medical` lives on pawns, not workbenches. Unless R4 supports repair/clean on pawn bill stacks (it doesn't — those are crafting workbenches), this is moot. But the postfix should not crash if it runs on a non-workbench `IBillGiver`. **Guard with `if (!(thing is IBillGiver billGiver))` at the top, which the pseudocode already does. ✓**
- `Bill_Mech` lives on the mech gestator. Same as medical — moot but guard against.

This is mostly fine, but one corner: `Bill_ProductionWithUft.BoundUft != null` means the UFT is *already assigned to this pawn* and *bound* to this bench. If R4 cuts in front, the bound UFT stays bound, gets re-picked next tick, and effectively the pawn ping-pongs between R4 and UFT. **Test this.** Likely safe because R4 jobs end and the next scan picks UFT, but it depends on how often R4 bills are queued vs. how the bill stack rotates.

**Options:**

- **(5A) Skip replacement when vanilla's pick is a `Bill_ProductionWithUft` with `BoundUft != null` and `BoundWorker == pawn`.** Lets the pawn finish what they started.
- **(5B) Replace freely.** Honor stack order strictly. UFT picks itself back up next tick.

**Recommendation: 5A.** Finishing a bound UFT before switching is the less-surprising behavior — even more so when "bill order" is supposed to mean "the pawn does what you asked, in order". The pawn already chose this UFT; let them finish.

---

### Pain point 6 — Modded benches without a `WorkGiver_DoBill` scanner

The proposal flags this under Remaining Question #1 but defers the audit. Some context I'd add:

`BillUtility.GetWorkgiver` iterates all WorkGiverDefs looking for *any* `WorkGiver_DoBill` whose `ThingIsUsableBillGiver(bench)` returns true. If a modded bench's bills are dispatched by a non-`WorkGiver_DoBill` WorkGiver (a custom subclass of `WorkGiver_Scanner`), then:

1. The vanilla bill scan never runs for that bench.
2. R4's `WorkGiver_DoBill.JobOnThing` postfix never fires.
3. Currently, R4 dynamically injects `WorkGiver_R4RepairBill` / `WorkGiver_R4CleanBill` to *also* scan these benches. Removing that injection orphans those benches.

The proposal's `R4WorkbenchFilterCache.BenchWorkTypes` already classifies benches by what `WorkGiver_DoBill` claims them. Use that classification:

**Options:**

- **(6A) Audit-then-delete.** Before removing dynamic bill WorkGiver injection, log every bench that gets dynamic R4 recipes but has zero `WorkGiver_DoBill` claimants. If the set is empty, the deletion is safe. If non-empty, keep a *single* fallback `WorkGiver_R4UnifiedBill` for those benches only (no need for separate repair/clean WorkGivers — one unified scanner is enough since order doesn't matter when no other DoBill exists to compete).
- **(6B) Keep a single fallback WorkGiver always.** Simplest, avoids the audit, costs one extra workgiver in the system. The fallback only acts on benches with zero `WorkGiver_DoBill` claimants (early-return otherwise).
- **(6C) Delete first, fix the report later.** Risky; will break some modded benches.

**Recommendation: 6B.** The audit (6A) is the "right" answer but it's a moving target — every new modded bench is a potential new edge case. A single always-on fallback that only scans orphan benches is cheap insurance. The performance impact is one O(1) "is this bench claimed by a vanilla DoBill" check per scan.

---

### Pain point 7 — The hidden `RRRR_BillOnly` work type touches one UI dropdown

`MechWorkUtility.AnyWorkMechCouldDo` is called from `Dialog_BillConfig.GeneratePawnRestrictionOptions` to decide whether to show "Any Mech" / "Any Non-Mech" pawn restriction options for a bill. With `requiredGiverWorkType = RRRR_BillOnly` and no mech having `RRRR_BillOnly` in `mechEnabledWorkTypes`, these options disappear for R4 repair/clean bills.

This is *correct* — R4 bills aren't actually dispatched by a `canBeDoneByMechs=true` WorkGiver, so the player shouldn't be able to assign them to mechs anyway. With the *current* design (`requiredGiverWorkType = Crafting`), the player *can* select "Any Mech" but the bill won't dispatch to mechs because the vanilla `DoBills*` WorkGivers all have `canBeDoneByMechs=false`. So the UI option is currently a dead-end footgun, and the new design accidentally fixes it.

**No action needed.** Worth a sentence in `DESIGN.md` so the change isn't surprising during testing.

---

### Pain point 8 — Save compatibility specifics

The proposal says "the hidden work type and changed `requiredGiverWorkType` are def changes only. Bills still reference the same R4 `RecipeDef`s." This is right, but here's the detail:

- `Bill` serializes a reference to `RecipeDef.defName`. On load, `RecipeDef` is re-resolved from `DefDatabase`. The recipe's `requiredGiverWorkType` field is *not* serialized on the bill — it's read from the resolved `RecipeDef` at runtime. **Changing the XML changes the behavior immediately on load. ✓**
- `Pawn_WorkSettings.priorities` is a `DefMap<WorkTypeDef, int>` serialized via `Scribe_Deep`. New work types not in the saved dictionary default to whatever `DefMap` defaults to (0, i.e., disabled). For `RRRR_BillOnly` with no WorkGivers, this is irrelevant — but worth confirming `DefMap` doesn't crash on missing entries during `PostLoadInit`.
- The 10 removed `WorkGiverDef`s — `RRRR_CleanBill_*` and `RRRR_RepairBill_*` — are not serialized anywhere (WorkGivers are def-resolved at runtime). Removing them is clean.

One thing the proposal doesn't address: **what if the player removes the R4 mod entirely from a save that has R4 bills?** That's the standing save-compatibility concern from `RIMWORLD_MODDING_REFERENCE.md`. With R4 removed:

- The `RecipeDef`s `Make_RRRRRepair` etc. no longer exist.
- Bills referencing those recipes are stripped by the `Building_WorkTable.SpawnSetup` postfix (which `RIMWORLD_MODDING_REFERENCE.md` recommends, and which R4 should already implement).
- No `Zone` subclasses or custom `IExposable` collections to worry about for R4 (repair/clean bills are just `Bill_Production` instances under custom recipes — no custom save state).

**Pre-existing concern, not introduced by this change.** Worth re-verifying the `Building_WorkTable.SpawnSetup` bill-stripping postfix is in place and tested. If it isn't, add it now — it's cheap insurance.

---

### Pain point 9 — Loss of `priorityInType` as a tuning knob

Currently, `priorityInType` tuning is what makes repair/clean visible *at all* on benches where vanilla bills swamp the scan. After the change:

- Repair/clean dispatch is gated on vanilla *first* returning something (bill job or null).
- The postfix replaces vanilla's pick only when an R4 bill is *above* vanilla's pick in the stack.
- There's no "always check R4 first" mode anymore.

For most cases this is correct. But it does mean the player has lost a tuning option: today, they can move R4 repair/clean to top of stack and have it always preferred over vanilla (because `priorityInType` ordering puts repair/clean's WorkGiver first). After the change, stack order is the only knob, which is what players actually want — but it's worth noting.

A real edge case: if a player has many vanilla bills above repair/clean and the topmost vanilla bill always succeeds, R4 will never get scheduled. This is *correct* — that's what stack order means — but it's a player-visible behavior change from "R4 sometimes interrupts" to "R4 only runs when nothing higher is available". Document it in the changelog.

**No action needed; just communicate.**

---

### Pain point 10 — `ShouldSkip` and `ListerThings` interaction

The postfix relies on running for every `WorkGiver_DoBill.JobOnThing` call. But `JobGiver_Work` calls `ShouldSkip` first, and if it returns true, `JobOnThing` is never called for that WorkGiver — meaning the postfix never fires either.

Look at `WorkGiver_DoBill.ShouldSkip`:

```csharp
public override bool ShouldSkip(Pawn pawn, bool forced = false)
{
    List<Thing> list = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver);
    for (int i = 0; i < list.Count; i++)
    {
        if (list[i] is IBillGiver billGiver && billGiver != pawn
            && ThingIsUsableBillGiver(list[i])
            && billGiver.BillStack.AnyShouldDoNow)
        {
            return false; // don't skip
        }
    }
    return true; // skip — no bench has any "should do now" bill
}
```

`AnyShouldDoNow` walks the entire bill stack and returns true if any bill returns `ShouldDoNow()`. R4 bills are bills; they return true normally. So as long as *any* bill on *any* qualifying bench is `ShouldDoNow`, the WorkGiver isn't skipped. ✓

But there's a subtle case: **`ShouldSkip` is called once per pawn per WorkGiver per scan, before the bench iteration.** If a bench has *only* suspended R4 bills (`ShouldDoNow == false`), `ShouldSkip` is true for that WorkGiver if no other bench has runnable bills. Then `JobOnThing` is never called, postfix never runs, R4 bills sit idle. **Same as vanilla behavior with all-suspended bills. ✓**

No action.

---

## Recommendations summary

| Pain | Severity | Recommendation |
|---|---|---|
| 1. `HasJobOnThing` double-call | Medium | Per-pawn/bench/tick memoization (1A) |
| 2. `PassesVanillaBenchPrechecks` drift | Medium | Single helper + version stamp + startup warning (2A+2D) |
| 3. Cancelling prefixes from other mods | Medium | Accept, document, keep config toggle in pocket (3E → 3C if needed) |
| 4. Haul-off handling in factory | Low | Check haul-off only when vanilla returned null (4A) |
| 5. UFT/Autonomous bill replacement | Low | Don't replace pawn's own bound UFT (5A) |
| 6. Orphan modded benches | Medium-high | Single fallback `WorkGiver_R4UnifiedBill` for orphan benches only (6B) |
| 7. Mech UI dropdown | None | Document the (improved) behavior |
| 8. Save compatibility | Low | Verify `Building_WorkTable.SpawnSetup` bill-stripping is in place |
| 9. Loss of priority tuning | None | Document in changelog |
| 10. `ShouldSkip` interaction | None | None |

---

## Higher-level commentary on the proposal

A few things the proposal does well that are worth keeping:

- **Vanilla-first instincts.** Approach 5 stays out of vanilla's way as much as possible: a postfix, not a prefix; vanilla owns dispatch, R4 owns one specific replacement decision; recycle stays on vanilla's path because it can; the hidden work type is a minimal-surface-area lever. This matches the standing "vanilla-first" principle from your modding reference.
- **Explicit anti-goals.** "R4 must use vanilla-style cheap skip checks" and "do not replace non-bill results" are precise contracts that let the reviewer (and future you) check correctness without reading the whole implementation.
- **Scenario traces.** The six scenarios are most of what makes the design reviewable. Add one more for "vanilla returns a UFT job for an in-progress UFT, R4 is above" once you decide between 5A/5B.

Things to consider before implementation starts:

- The proposal is correctly scoped to bill-order arbitration only. **Don't expand it.** Designation-flow overlap (mentioned under Pitfalls), float-menu missing-material display, BIS edge cases — all explicitly deferred. Hold that line.
- **R4 has 11 outstanding bugs in `R4_PENDING_CHANGES.md`** from your notes. None of them are blocked by this fix, and most of them touch `JobDriver` internals rather than dispatch. But several of them (notably `tickAction` → `tickIntervalAction`, missing `CheckForJobOverride`) interact with how often R4 jobs get re-evaluated against the bill stack. Pick one ordering: either land the dispatch fix first (and accept that the existing bugs remain) or fix the JobDriver bugs first (and accept that some will look like dispatch bugs until the dispatch fix lands). I'd land **dispatch first** — it's the more visible bug, and the JobDriver fixes won't reveal new dispatch issues.
- The "Implementation Sequence" has 10 steps. **Don't do them in one PR.** Break it into at least three commits matching your incremental-test-between-changes pattern:
  1. Add `RRRR_BillOnly` work type + change recipe `requiredGiverWorkType` (verify vanilla now skips R4 bills entirely; R4 dispatch is *broken* at this point if you also don't ship step 2 in the same release — fine for a dev branch, not for a player).
  2. Add `R4BillJobFactory` + `Patch_R4BillOrdering` (now R4 dispatch works again via the postfix).
  3. Delete old bill WorkGivers + XML defs + update cache injection.

That's the safe edit order. Doing 3 before 2 leaves a window where R4 doesn't dispatch at all.

---

## Open question worth resolving before implementation

The proposal's Remaining Question #1 ("Do any dynamically injected benches lack a `WorkGiver_DoBill` scanner?") needs an answer before the deletion step, not after. The audit is one log-scan over `R4WorkbenchFilterCache` output during a load with common bench mods (VFE, RIMMSqol, etc.). Cheap to do; expensive to discover at user-report time.

Recommendation 6B (always-on fallback for orphan benches) hedges this — but doing the audit anyway tells you whether the fallback is even needed, which affects how you describe the architecture in `DESIGN.md`.
