# R4 Bill Ordering Fix — Design Document

*RimWorld 1.6 · Harmony 2.4.x · RRRR mod*

*Source-verified against decompiled 1.6 game source and current R4 code.*

---

## Problem statement

When a player places multiple R4 and vanilla bills on the same workbench, bill ordering is not respected. A repair bill placed below a clean bill will still execute first, and R4 bills can jump ahead of vanilla bills or vice versa regardless of their position in the bill stack.

Players expect bill stack ordering to be authoritative — the bill at the top of the list should always be attempted first, exactly as vanilla behaves with its own bills.

---

## Root cause

R4 splits bill execution across **three independent WorkGivers** per bench: vanilla's `WorkGiver_DoBill`, R4's `WorkGiver_R4RepairBill`, and R4's `WorkGiver_R4CleanBill`. Each scans the same `BillStack` but only sees "its own" recipe types. Because `JobGiver_Work` iterates WorkGivers by descending `priorityInType` and selects the first valid target, the WorkGiver with the highest priority always wins — regardless of where its bills sit in the stack.

### Concrete example: machining table

Three WorkGivers scan the same bench (Smithing work type):

| WorkGiver | priorityInType | Sees |
|---|---|---|
| `DoBillsMachiningTable` (vanilla) | 75 | Vanilla recipes + R4 recycle |
| `RRRR_RepairBill_Machining` | 72 | R4 repair only |
| `RRRR_CleanBill_Machining` | 70 | R4 clean only |

Player sets bill order: Clean → Repair → Make component. Actual execution: vanilla at priority 75 checks first, skips clean/repair (wrong `requiredGiverWorkType`), finds "Make component", returns that job. Repair at 72 and clean at 70 never get asked. Even if vanilla finds nothing, repair at 72 always beats clean at 70.

The visible bill order in the UI is irrelevant — WorkGiver `priorityInType` decides.

---

## How vanilla bill ordering works

Verified against decompiled RimWorld 1.6 source.

### Dispatch chain

```
JobGiver_Work.TryIssueJobPackage
  → iterates WorkGiversInOrderNormal (sorted by descending priorityInType)
  → for each WorkGiver at the current priority tier:
      → WorkGiver_DoBill.JobOnThing(pawn, bench)
        → precondition checks (IBillGiver, AnyShouldDoNow,
                                UsableForBillsAfterFueling,
                                CanReserve, !IsBurning,
                                CanReserveSittableOrSpot)
        → CompRefuelable.HasFuel check → if no fuel, return RefuelJob
        → RemoveIncompletableBills
        → StartOrResumeBillJob(pawn, billGiver):
          → for i = 0..BillStack.Count:
              → skip: requiredGiverWorkType mismatch
              → skip: nextTickToSearchForIngredients cooldown
              → skip: !ShouldDoNow or !PawnAllowedToStartAnew
              → skip: skill requirements not met
              → skip: medical / mech / UFT / autonomous special cases
              → TryFindBestBillIngredients → if fails, set cooldown, continue
              → TryStartNewDoBillJob → return Job (JobDefOf.DoBill)
```

### Key properties

1. **Single-scanner, top-to-bottom.** `StartOrResumeBillJob` is a flat `for` loop over `BillStack` by index. No sorting, no priority weighting. First runnable bill wins.

2. **`requiredGiverWorkType` is a per-bill filter, not a per-bench filter.** Each bill's recipe can declare a `requiredGiverWorkType`. If the current `WorkGiver_DoBill`'s `def.workType` doesn't match, the bill is skipped in the loop — but the loop continues to the next bill. This is how tailoring recipes stay invisible to a smithing WorkGiver even though they share a fabrication bench.

3. **Ingredient failure sets a cooldown.** When `TryFindBestBillIngredients` fails, vanilla sets `bill.nextTickToSearchForIngredients` to `TicksGame + IntRange(500, 600).RandomInRange`. Future scans skip that bill until the cooldown expires. The write is gated on `FloatMenuMakerMap.makingFor != pawn` so the float-menu path still shows missing-material messages.

4. **`JobOnThing` is called twice per accepted target.** `WorkGiver_Scanner.HasJobOnThing` is literally `return JobOnThing(...) != null`. `JobGiver_Work` calls `HasJobOnThing` for validation, then `JobOnThing` again to actually retrieve the job. Each call runs vanilla's full dispatch *and* every patch on it. Vanilla tolerates this because cooldown writes are idempotent and the search is monotone. Any postfix on `JobOnThing` must preserve that property.

5. **Same-tier WorkGivers compete on proximity, not bill order.** `JobGiver_Work` accumulates the best (closest) target across all WorkGivers at the same `priorityInType`. If multiple WorkGivers at the same priority return jobs for the same bench, the first to set `bestTargetOfLastPriority` *and* produce a non-null job from the bottom-of-iteration `JobOnThing` call wins. There is no mechanism for cross-WorkGiver bill-order coordination.

### What this means for R4

Vanilla was designed for one `WorkGiver_DoBill` per bench (per work type). That single scanner walks the bill stack in order and returns the first runnable bill. R4 broke this by introducing parallel scanners that each see a subset of the stack. No amount of `priorityInType` tuning can restore bill ordering across separate WorkGivers — it's architecturally impossible without coordination.

---

## Approaches considered

### Approach 1: Separate WorkGivers with priority tuning (current)

Give each R4 bill type its own WorkGiver at a carefully chosen `priorityInType`.

**Why it fails:** `priorityInType` determines which *WorkGiver* goes first, not which *bill* goes first. Bill stack order is only respected within a single WorkGiver's scan. Clean at priority 70 will always lose to repair at 72, regardless of bill position.

### Approach 2: Same-priority WorkGivers with "defer to earlier bill" logic

Put all WorkGivers (vanilla, R4 repair, R4 clean) at the same `priorityInType`. Each R4 WorkGiver scans the full bill stack and returns null if it finds a runnable non-R4 bill above its own best candidate.

**Why it fails:** The "defer" check cannot cheaply determine whether vanilla will actually execute a bill that R4 defers to. Specifically:

- R4 can check `ShouldDoNow()`, `PawnAllowedToStartAnew()`, skill requirements, and `nextTickToSearchForIngredients` cooldown. These are cheap.
- R4 **cannot** check whether vanilla can find ingredients — that requires the full region-traversal ingredient search, which is expensive and would duplicate vanilla's work.
- On the first tick where a vanilla bill fails ingredients (before its cooldown is set), R4's defer check sees the bill as "runnable" and returns null. But vanilla also fails the bill in the same scan pass and continues to a lower bill. Both scanners run sequentially in `JobGiver_Work`, so vanilla sets the cooldown during its scan, but R4's scan may have already run (depending on iteration order) and made the wrong decision.
- Even when cooldowns are correctly set, `JobGiver_Work`'s same-tier logic picks based on proximity, not bill order, across WorkGivers.

### Approach 3: Prefix on `WorkGiver_DoBill.JobOnThing` — replace vanilla's scanner

Patch `JobOnThing` with a bool prefix that replaces `StartOrResumeBillJob` with an R4-aware unified scanner when R4 bills are present.

**Why it's fragile:**

- **High-conflict patch target.** `WorkGiver_DoBill.JobOnThing` is the single most commonly patched WorkGiver method in the modding ecosystem. Mods like Common Sense, Better Workbench Management, Bill Ingredient Source, and various bill-management mods all touch this code path. A bool prefix that returns `false` (skipping the original) prevents all postfixes from other mods from receiving vanilla's result.
- **Must replicate vanilla internals.** The replacement scanner must handle every check vanilla performs: refuelable, `UsableForBillsAfterFueling`, `RemoveIncompletableBills`, `CanReserveSittableOrSpot`, the `FloatMenuMakerMap.makingFor` float-menu ingredient display path, `Bill_ProductionWithUft` handling, `Bill_Autonomous` handling, `Bill_Medical` handling. If vanilla adds new bill types or checks in a future version, R4's replacement scanner silently diverges.
- **Non-vanilla JobDef from vanilla method.** Other mods' postfixes on `JobOnThing` may inspect `__result.def` or `__result.bill` assuming the result is always `JobDefOf.DoBill` or null. Returning `RRRR_Repair` or `RRRR_Clean` from a method that normally only produces `DoBill` can confuse those postfixes.
- **Replaces vanilla state management.** Vanilla sets `nextTickToSearchForIngredients` on bills that fail ingredient searches. The replacement scanner must replicate this exactly, or bills that should be on cooldown get re-scanned every tick.

### Approach 4: Postfix on `JobOnThing` with `requiredGiverWorkType=Crafting`

Let vanilla run first, then have an R4 postfix replace vanilla's result when an R4 repair/clean bill should win by stack order.

**Why it is not safe by itself:** R4 currently uses `requiredGiverWorkType=Crafting` to make vanilla skip repair/clean bills on smithing and tailoring benches. That works everywhere except the CraftingSpot, because `DoBillsUseCraftingSpot` also has `workType=Crafting`. Vanilla can therefore pick an R4 repair/clean bill, stop scanning lower vanilla bills, and return a `JobDefOf.DoBill`. If R4 then discards that job and cannot create the custom R4 job, the lower vanilla bill was never evaluated. Recovering from this would require reimplementing vanilla's lower-bill scan, which is exactly what this design is trying to avoid.

### Approach 5: Hidden R4 work type + conservative postfix — **CHOSEN**

Add a hidden sentinel `WorkTypeDef` used only by R4 repair/clean recipes' `requiredGiverWorkType`. No vanilla `WorkGiver_DoBill` uses that work type, so vanilla always skips R4 repair/clean bills on every bench, including the CraftingSpot. R4 then uses a conservative postfix to insert custom repair/clean jobs only when they are above the vanilla bill that vanilla actually selected.

**Why this is safer:** vanilla continues to own all vanilla and R4 recycle bill selection, ingredient failure cooldowns, refueling, haul-off jobs, unfinished things, medical/mech/autonomous bill types, and future vanilla bill behaviour. R4 only handles its two custom recipe workers.

---

## Proposed solution: hidden work type + bill-order arbitration

### Core concept

1. Add a hidden work type, `RRRR_BillOnly`.
2. Change R4 repair/clean recipes from `requiredGiverWorkType=Crafting` to `requiredGiverWorkType=RRRR_BillOnly`.
3. Keep R4 recycle in vanilla's `WorkGiver_DoBill` path.
4. Remove the separate R4 repair/clean bill WorkGivers (with one orphan-bench fallback exception, see below).
5. Add a `WorkGiver_DoBill.JobOnThing` postfix that scans only R4 repair/clean bills above vanilla's selected bill and substitutes the first runnable R4 custom job.
6. Keep an always-on fallback `WorkGiver_R4UnifiedBill` for benches not claimed by any vanilla `WorkGiver_DoBill`. It scans the bench's stack in stack order and dispatches the first runnable R4 repair/clean bill. Only fires for orphan benches.

The hidden work type is not a player-facing work category. It is `visible=false` and owns no WorkGiverDefs. Its only purpose is to make vanilla's `requiredGiverWorkType` check skip R4 repair/clean recipes on all benches.

### Sentinel work type

```xml
<WorkTypeDef>
  <defName>RRRR_BillOnly</defName>
  <label>R4 bill work</label>
  <labelShort>R4</labelShort>
  <pawnLabel>R4 worker</pawnLabel>
  <gerundLabel>doing R4 bill work</gerundLabel>
  <verb>do R4 bill work</verb>
  <visible>false</visible>
  <naturalPriority>0</naturalPriority>
  <relevantSkills>
    <li>Crafting</li>
  </relevantSkills>
</WorkTypeDef>
```

R4 recipes keep `workSkill=Crafting`, `workSpeedStat`, sounds, and effects as they do today. The sentinel work type is only a vanilla-dispatch blocker.

### Arbitration rules

- If the bench has no R4 repair/clean bills, return immediately.
- If vanilla returned a non-null result with no `bill`, preserve it. This covers refuel jobs, haul-off jobs, and unknown modded prerequisite jobs.
- If vanilla returned a bill job, use that bill's index as the lower bound. R4 may only replace it with an R4 repair/clean bill above that index. **Bill stack order is authoritative — this includes replacing a `Bill_ProductionWithUft` job that is bound to this pawn.** The pawn picks the bound UFT back up on a later scan when it becomes the top runnable bill.
- If vanilla returned null, first re-check the same bench preconditions that vanilla uses before bill scanning. Only then may R4 scan all repair/clean bills.
- R4 uses vanilla-style cheap skip checks: `ShouldDoNow`, `PawnAllowedToStartAnew`, skill requirements, and `nextTickToSearchForIngredients` unless `FloatMenuMakerMap.makingFor == pawn`.
- When an R4 repair/clean bill passes cheap checks but cannot produce a job, set `nextTickToSearchForIngredients` to `TicksGame + [500..600]` unless in the float-menu path.

### Pseudocode

```csharp
[HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
static class Patch_R4BillOrdering
{
    static void Postfix(
        WorkGiver_DoBill __instance,
        ref Job __result,
        Pawn pawn,
        Thing thing,
        bool forced)
    {
        if (!(thing is IBillGiver billGiver) || !HasAnyR4RepairOrCleanBill(billGiver))
            return;

        // Refuel, haul-off, and modded prerequisite jobs are not bill choices.
        // Preserve them.
        if (__result != null && __result.bill == null)
            return;

        int vanillaIndex = int.MaxValue;
        if (__result?.bill != null)
        {
            vanillaIndex = billGiver.BillStack.IndexOf(__result.bill);
            if (vanillaIndex < 0)
                return; // another mod returned a bill not in this stack; do not guess
        }
        else if (!R4BillJobFactory.PassesVanillaBenchPrechecks(__instance, pawn, thing, forced))
        {
            return;
        }

        // Per-tick memoization handles HasJobOnThing/JobOnThing double-call:
        // the factory returns the same result for (pawn, bench, tick, forced).
        Job r4Job = R4BillJobFactory.TryDispatchAboveIndex(
            pawn, thing, billGiver, vanillaIndex, forced);

        if (r4Job != null)
            __result = r4Job;
    }
}
```

`TryDispatchAboveIndex` walks `BillStack[0 .. vanillaIndex)`, applies cheap-check filters, calls the per-operation candidate search (repair or clean), and returns the first job it can create. Cooldown writes happen inside this method exactly as vanilla writes them.

### Scenario traces

#### Scenario 1: all bills valid

Bill stack: `[Vanilla A, R4 Repair B, Vanilla C]`

1. Vanilla returns `DoBill` for A at index 0.
2. R4 scans bills above index 0: none.
3. Pawn does A.

#### Scenario 2: upper vanilla bill lacks ingredients

Bill stack: `[Vanilla A, R4 Repair B, Vanilla C]`

1. Vanilla tries A, fails ingredients, sets A cooldown, skips B via `RRRR_BillOnly`, and returns C at index 2.
2. R4 scans indices 0..1, skips A (cooldown), creates repair job for B.
3. Pawn does B.

#### Scenario 3: upper R4 bill cannot run

Bill stack: `[Vanilla A, R4 Repair B, Vanilla C]`

1. Vanilla fails A, skips B, returns C.
2. R4 tries B, cannot find candidate/materials, sets B cooldown.
3. Pawn does C.

#### Scenario 4: only R4 repair/clean bills

Bill stack: `[R4 Clean A, R4 Repair B]`

1. Vanilla skips both via `RRRR_BillOnly` and returns null.
2. R4 re-checks bench preconditions, then scans the full stack.
3. Pawn does the first runnable R4 bill.

#### Scenario 5: CraftingSpot

Bill stack: `[R4 Repair A, Vanilla B]` on CraftingSpot.

1. Vanilla skips A because `requiredGiverWorkType=RRRR_BillOnly` does not match `DoBillsUseCraftingSpot.workType=Crafting`.
2. Vanilla returns B if B is runnable, or null if no lower bill is runnable.
3. R4 scans A before B and creates the custom repair job if A is truly runnable.

This removes the unsafe `vanillaReturnedR4` recovery path entirely.

#### Scenario 6: vanilla prerequisite job

Vanilla returns a refuel job or haul-off job, so `__result.bill == null`. R4 preserves it. The pawn clears the vanilla prerequisite first, and a later scan can choose the correct bill by stack order.

#### Scenario 7: bound UFT below R4 repair

Bill stack: `[R4 Repair A, UFT bill B with B.BoundUft != null and B.BoundWorker == pawn]`

1. Vanilla enters the UFT branch, returns `FinishUftJob` for B at index 1 with `__result.bill = B`.
2. R4 scans index 0..0, finds A is runnable, replaces the result with the R4 repair job.
3. Pawn does A. On the next scan, B is back at the top of "runnable" — the UFT is still bound, vanilla picks it up again, R4 has nothing above it.

This means the pawn may walk back and forth if A keeps producing more candidates. That is the intended consequence of "stack order is authoritative." Players who want to finish a UFT before R4 cuts in can move the UFT bill to the top of the stack.

---

## Resolved design questions

| Question | Decision |
|---|---|
| Should the bill stack be authoritative even over bound UFT jobs? | **Yes.** This matches vanilla's "stack order is gospel" model. |
| Audit-then-delete orphan-bench WorkGivers, or keep an always-on fallback? | **Always-on fallback.** Single `WorkGiver_R4UnifiedBill` for benches not claimed by any vanilla `WorkGiver_DoBill`. Cheap insurance against new modded benches. |
| Land dispatch fix first or `R4_PENDING_CHANGES.md` JobDriver fixes first? | **Dispatch first.** More visible bug; JobDriver fixes won't expose new dispatch issues. |
| Pre-emptive compatibility guard / settings toggle for cancelling-prefix conflicts? | **No.** Bug reports get compatibility patches as needed. |
| Should the sentinel work type be visible to players? | **No.** Hidden. `MechWorkUtility.AnyWorkMechCouldDo` is the only consumer outside dispatch, and the change actually fixes a pre-existing UI footgun (the Mech pawn-restriction dropdown was previously a dead-end). |

---

## Implementation architecture

### Files to create

| File | Purpose |
|---|---|
| `1.6/Defs/WorkTypes.xml` | Defines hidden `RRRR_BillOnly` work type |
| `Source/Patches/Patch_R4BillOrdering.cs` | Conservative `JobOnThing` postfix |
| `Source/Jobs/R4BillJobFactory.cs` | Shared repair/clean bill job creation and bench precheck helpers |
| `Source/Jobs/WorkGiver_R4UnifiedBill.cs` | Fallback WorkGiver for orphan modded benches |

### Files to remove

| File | Reason |
|---|---|
| `Source/Jobs/WorkGiver_R4CleanBill.cs` | Replaced by `R4BillJobFactory` + postfix + fallback |
| `Source/Jobs/WorkGiver_R4RepairBill.cs` | Replaced by `R4BillJobFactory` + postfix + fallback |
| `Source/Patches/Patch_WorkGiver_DoBill_Repair.cs` | No longer needed once vanilla always skips R4 repair/clean |

Remove all 10 XML bill WorkGiverDefs from `1.6/Defs/WorkGivers.xml`:

- `RRRR_CleanBill_CraftingSpot`, `RRRR_CleanBill_Tailor`, `RRRR_CleanBill_Smithy`, `RRRR_CleanBill_Machining`, `RRRR_CleanBill_Fabrication`
- `RRRR_RepairBill_CraftingSpot`, `RRRR_RepairBill_Tailor`, `RRRR_RepairBill_Smithy`, `RRRR_RepairBill_Machining`, `RRRR_RepairBill_Fabrication`

The designation-based WorkGivers (`RRRR_Repair_Crafting`, `RRRR_Clean_Crafting`, etc.) are unaffected.

### Files to modify

| File | Change |
|---|---|
| `1.6/Defs/Recipes.xml` | Change R4 repair/clean `requiredGiverWorkType` to `RRRR_BillOnly`; update comments |
| `1.6/Defs/WorkGivers.xml` | Remove bill-based repair/clean WorkGiverDefs only |
| `Source/Cache/R4WorkbenchFilterCache.cs` | Stop injecting per-bench `WorkGiver_R4RepairBill` / `WorkGiver_R4CleanBill`. Keep dynamic recipe injection. Add orphan-bench detection: register orphan benches with the fallback WorkGiver's `fixedBillGiverDefs`. |
| `Source/RecipeWorkers/*.cs` | Update comments that mention old bill WorkGivers |
| `DESIGN.md` | Update architecture tables, Harmony patch list, and workgiver sections |

`InjectRecipeDef` already copies `requiredGiverWorkType` from the template, so dynamic modded-bench recipe clones inherit `RRRR_BillOnly` automatically.

### Factory contract

```csharp
static class R4BillJobFactory
{
    /// Vanilla bench-precondition mirror. Returns true when vanilla's
    /// preconditions would pass and StartOrResumeBillJob would run.
    /// MUST mirror the checks at the top of WorkGiver_DoBill.JobOnThing
    /// up to (but NOT including) the CompRefuelable refuel-job branch —
    /// because if vanilla had a refuel job to issue, the postfix
    /// preserves __result and never reaches this helper.
    public static bool PassesVanillaBenchPrechecks(
        WorkGiver_DoBill workGiver, Pawn pawn, Thing bench, bool forced);

    /// Per-pawn / per-bench / per-tick memoized dispatch.
    /// Walks BillStack[0..upperExclusiveIndex), applies cheap-check filters,
    /// invokes the per-operation candidate search, writes cooldowns on
    /// failed candidates, returns the first valid job (or haul-off) or null.
    public static Job TryDispatchAboveIndex(
        Pawn pawn, Thing bench, IBillGiver billGiver,
        int upperExclusiveIndex, bool forced);
}
```

The candidate-search internals (BIS fast path, region traversal, damaged-item filter, tainted-apparel filter) are lifted directly from the existing `WorkGiver_R4RepairBill` and `WorkGiver_R4CleanBill`. The two paths share everything except the per-operation eligibility predicate and the JobDef.

### Orphan-bench fallback

`R4WorkbenchFilterCache.BuildBenchWorkTypes` already does a two-pass classification: pass 1 maps benches by vanilla `WorkGiver_DoBill` only; pass 2 fills gaps from modded WorkGiver types. The orphan check is the difference between these sets.

`WorkGiver_R4UnifiedBill` is registered at startup with `fixedBillGiverDefs` set to the orphan benches (benches in `BenchCraftables` but not claimed by any vanilla `WorkGiver_DoBill`). Its `JobOnThing` mirrors the postfix logic but starts from a clean slate: it scans the full bill stack and returns the first runnable R4 repair/clean bill. There's no "above vanilla index" notion because vanilla never gets to run on these benches.

Because the fallback only acts on orphan benches, it never competes with the postfix path. Two scanners can't both fire for the same bench.

The fallback uses the *same* `R4BillJobFactory` as the postfix, so behaviour and caching are unified.

---

## Performance considerations

### Happy path: no R4 repair/clean bills

The postfix does one small bill-stack scan to detect R4 repair/clean bills and returns. Negligible for normal bill stacks.

### Normal mixed stack

Vanilla performs its normal scan and returns the first runnable vanilla/recycle bill. R4 then checks only R4 repair/clean bills above that bill. Vanilla bills above the selected bill are not re-evaluated.

### Worst case: only R4 repair/clean bills

Vanilla skips them and returns null. R4 re-checks bench preconditions and scans the full stack. Equivalent to the current bill WorkGivers, but without three independent WorkGiver queues fighting each other.

### Avoidable double work

Because vanilla always skips R4 repair/clean via `RRRR_BillOnly`, there is no CraftingSpot double ingredient search and no double cooldown write for those bills.

### `JobOnThing` called twice per accepted target

`WorkGiver_Scanner.HasJobOnThing` calls `JobOnThing`. A postfix that searches R4 candidates/materials therefore runs during both target validation and job creation in the same tick. **`R4BillJobFactory.TryDispatchAboveIndex` must memoize per `(pawn.thingIDNumber, bench.thingIDNumber, tick, forced)`** so the candidate search, BIS lookup, and cooldown writes happen exactly once per tick.

The existing `WorkGiver_R4RepairBill` / `WorkGiver_R4CleanBill` already implement this exact caching pattern. The factory keeps it.

---

## Compatibility considerations

### Other mods patching `WorkGiver_DoBill.JobOnThing`

The patch is a postfix, not a bool prefix. Vanilla and other patches run first.

Harmony 2.x semantics: a `false`-returning prefix from another mod skips the original and any remaining prefixes, but postfixes still run with whatever `__result` the cancelling prefix set. R4's postfix runs in all cases.

**Known compatibility risk:** if another mod's prefix sets `__result = null` to *block* work at a bench (rather than just "no bill found"), R4's postfix sees null, runs the precheck branch, passes, and creates an R4 job — overriding the mod's block. This is unavoidable in the general case without a registry of "known blocking prefixes". V1 accepts this; if real conflicts surface during testing or after release, add a settings toggle in a patch release.

### Bill Ingredient Source (BIS)

Carry over the existing BIS fast path from `WorkGiver_R4RepairBill` and `WorkGiver_R4CleanBill`. The ordering change should not alter BIS candidate selection semantics. BIS only narrows the candidate item search; it does not interact with the bill stack walk.

### Mods adding bill types

R4 recognizes only repair/clean by `RecipeWorker_R4Repair` and `RecipeWorker_R4Clean`. All other bill types remain vanilla/mod-owned.

### Modded benches without a `WorkGiver_DoBill` scanner

Handled by the orphan-bench fallback (see above). The two-pass classification in `R4WorkbenchFilterCache.BuildBenchWorkTypes` cleanly identifies which benches need the fallback.

### Mech work UI

`MechWorkUtility.AnyWorkMechCouldDo` checks whether any mech-enabled work type contains the recipe's `requiredGiverWorkType`. With `RRRR_BillOnly` (and no mech having it in `mechEnabledWorkTypes`), the "Any Mech" / "Any Non-Mech" pawn-restriction options no longer appear for R4 repair/clean bills. This is **correct** — those bills are not dispatched by a `canBeDoneByMechs=true` WorkGiver, so the option was previously a dead-end footgun (player could select "Any Mech" but no mech ever picked the bill up). The new design accidentally fixes this.

### Save compatibility

The hidden work type and changed `requiredGiverWorkType` are def changes only. Bills still reference the same R4 `RecipeDef`s. Existing saves load and use the updated recipes. Removed WorkGiverDefs are not serialized as bill state.

`Pawn_WorkSettings.priorities` is a `DefMap<WorkTypeDef, int>` and defaults to 0 for new entries — adding `RRRR_BillOnly` to the def database does not require save migration.

**Mod removal:** `Patch_BuildingWorkTable_SpawnSetup` (already in R4 today) strips bills whose recipe is no longer in `bench.def.AllRecipes`. When R4 is removed, R4 recipes disappear from `AllRecipes` and any saved R4 bills get stripped on next bench load. No custom `Zone` subclasses or persistent `IExposable` collections to worry about.

---

## Pitfalls and edge cases

### Null is overloaded

Vanilla returning null can mean "no bill found", but it can also mean a bench precondition failed. R4 re-checks bench preconditions before creating a job from a null vanilla result — `PassesVanillaBenchPrechecks` handles this.

### Non-bill results are prerequisites

Refuel and haul-off jobs are produced by `WorkGiver_DoBill.JobOnThing` but have no `job.bill`. The postfix preserves them (`__result != null && __result.bill == null` → return).

### Recycle ordering

R4 recycle remains vanilla `DoBill`. If recycle is above repair/clean, vanilla returns recycle and R4 scans no lower R4 bills. If repair/clean is above recycle and runnable, R4 replaces the recycle result. If the upper R4 bill fails, R4 leaves the recycle job in place.

### Float-menu missing ingredient display

R4 repair/clean still will not fully participate in vanilla's `FloatMenuMakerMap.makingFor` missing-ingredient display path. This is a pre-existing gap and not required for bill ordering correctness. Defer to a separate change.

### Designation flow

Designation WorkGivers stay separate. Reservations prevent duplicate work between bill-targeted and designation-targeted items, but this is not solved by the bill-order patch.

### Bound UFT replacement

By design, the postfix may replace a `FinishUftJob` result with a higher R4 bill (Scenario 7). This is the intended trade-off of "stack order is authoritative". Document in the changelog.

### Cancelling prefixes

See Compatibility section above. Accepted risk for v1.

---

## Known correctness improvements (folded into this work)

The existing `WorkGiver_R4RepairBill` / `WorkGiver_R4CleanBill` have two pre-existing bugs worth fixing as part of the factory extraction:

1. **Uses `CurrentlyUsableForBills()` instead of `UsableForBillsAfterFueling()`.** `CurrentlyUsableForBills` fails on `HasFuel == false`, which means the current code silently returns null for unfueled benches *without* issuing a refuel job. Pawns never get the signal to refuel an R4 bench. The new design fixes this as a side effect: vanilla handles the refuel branch correctly (returns a refuel `Job` with `__result.bill == null`), the postfix preserves it, and `R4BillJobFactory.PassesVanillaBenchPrechecks` mirrors vanilla's `UsableForBillsAfterFueling()` for the null-result branch.

2. **`RemoveIncompletableBills` runs before `AnyShouldDoNow` check.** Vanilla calls `AnyShouldDoNow` first (as part of `JobOnThing`'s precondition block), then `RemoveIncompletableBills` after the refuel branch. The existing R4 code does the opposite. Probably harmless because `RemoveIncompletableBills` is idempotent and the only `CompletableEver`-overriding bill types (medical, mech) don't appear on workbenches, but the new factory should match vanilla's order to avoid future drift.

`R4BillJobFactory.PassesVanillaBenchPrechecks` is the single source of truth for this precondition mirror. It should be version-stamped (a comment noting which RimWorld version it was authored against) and there should be a startup warning if `VersionControl.CurrentVersion` no longer matches.

---

## Implementation sequence

Split into three commits, in order. Steps 1 and 2 must ship together — step 1 alone breaks R4 dispatch.

### Commit 1: introduce the hidden work type

1. Add `RRRR_BillOnly` in a new `1.6/Defs/WorkTypes.xml`.
2. Change R4 repair/clean recipe templates to `requiredGiverWorkType=RRRR_BillOnly`; keep recycle unchanged.

R4 dispatch is broken between this commit and commit 2. Do not release between them.

### Commit 2: install the new dispatch path

3. Add `R4BillJobFactory` (extract from existing repair/clean bill WorkGivers).
4. Add `WorkGiver_R4UnifiedBill` (orphan-bench fallback, uses the factory).
5. Add `Patch_R4BillOrdering` (postfix on `WorkGiver_DoBill.JobOnThing`).
6. Update `R4WorkbenchFilterCache.InjectModdedBenchBills`:
   - Stop injecting per-bench `WorkGiver_R4RepairBill` / `WorkGiver_R4CleanBill`.
   - Add orphan-bench detection (bench in `BenchCraftables` but not claimed by vanilla `WorkGiver_DoBill` in pass 1 of `BuildBenchWorkTypes`).
   - Add orphan benches to `WorkGiver_R4UnifiedBill.fixedBillGiverDefs` via the existing `RegisterDynamicWorkGiver` helper.

After this commit, dispatch works again. Test thoroughly here.

### Commit 3: delete old code

7. Remove `Patch_WorkGiver_DoBill_Repair.cs`.
8. Remove repair/clean bill `WorkGiverDef`s from `1.6/Defs/WorkGivers.xml`.
9. Delete `WorkGiver_R4RepairBill.cs` and `WorkGiver_R4CleanBill.cs`.
10. Update comments and `DESIGN.md` references.

---

## Test plan

### Behavioural correctness

1. Interleaved vanilla/R4 stack: `[Vanilla, Clean, Repair, Recycle]` with all bills runnable.
2. Upper vanilla bill missing ingredients, lower R4 bill runnable.
3. Upper R4 bill missing candidate/materials, lower vanilla/recycle runnable.
4. CraftingSpot with R4 repair/clean above vanilla bill; verify vanilla never creates `JobDefOf.DoBill` for repair/clean.
5. Bound UFT below R4 repair; verify R4 replaces and pawn returns to UFT on next scan.

### Bench / job lifecycle

6. Refuelable bench with no fuel; verify R4 does not override refuel/no-refuel; verify pawn refuels and resumes.
7. Ingredient-stack cell occupied; verify haul-off job is preserved.
8. Burning bench; verify no jobs created.
9. Bench reserved by another pawn; verify R4 yields.

### Mod ecosystem

10. BIS storage source configured for clean and repair bills; verify BIS fast path still routes correctly.
11. Modded bench with dynamically injected R4 recipes (test with VEF Mechanoids/Production).
12. Modded bench that has no vanilla `WorkGiver_DoBill` claim; verify `WorkGiver_R4UnifiedBill` dispatches.
13. Compatibility smoke test with Common Sense, Better Workbench Management, Pick Up And Haul loaded.

### Save / startup

14. Float-menu / right-click bill failure path does not spam errors.
15. Existing save with old R4 bills on benches loads and jobs dispatch correctly.
16. Remove R4 mod from a save with R4 bills; verify benches load and bills are stripped by `Patch_BuildingWorkTable_SpawnSetup`.

### Audit (before commit 3)

17. Log `R4WorkbenchFilterCache` output on a load with VEF Mechanoids, VEF Production, RIMMSqol, and any other large modpack on hand. Note which benches end up needing the orphan fallback. If the orphan set is empty across all tested modpacks, the fallback is cosmetic insurance and that's fine; if it's non-empty, those specific benches should be added to test scenarios 11–12.

---

## Open items (deferred, not blocking)

- **Float-menu missing-material display.** R4 repair/clean don't participate in vanilla's `FloatMenuMakerMap.makingFor` path. Pre-existing gap; address separately.
- **Settings toggle for cancelling-prefix conflicts.** Held in reserve. Add only if real conflicts surface.
- **`R4_PENDING_CHANGES.md` JobDriver bugs.** Independent of this work. Land dispatch first.
