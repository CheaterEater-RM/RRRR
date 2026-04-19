# PlaceHauledThingInCell Patch — Technical Analysis

## Problem

Vanilla's `Toils_Haul.PlaceHauledThingInCell` has a hardcoded whitelist of job defs that trigger ingredient tracking via `job.placedThings`:

```csharp
if (curJob.def == JobDefOf.DoBill || curJob.def == JobDefOf.RecolorApparel 
    || curJob.def == JobDefOf.RefuelAtomic || curJob.def == JobDefOf.RearmTurretAtomic)
{
    placedAction = delegate(Thing th, int added) {
        HaulAIUtility.UpdateJobWithPlacedThings(curJob, th, added);
    };
}
```

R4's `RRRR_Repair` and `RRRR_Clean` are not in this list. When `CollectIngredientsToils` places ingredients on bench cells, `job.placedThings` remains null.

## Call Chain Analysis

```
CollectIngredientsToils
  → Toils_Haul.PlaceHauledThingInCell(cellInd, findPlaceTarget, storageMode: false)
    → Returns a Toil whose initAction:
      → Checks the whitelist
      → Creates placedAction if job is whitelisted
      → Calls actor.carryTracker.TryDropCarriedThing(cell, Direct, out result, placedAction)
        → Calls innerContainer.TryDrop(...)
          → Calls GenDrop.TryDropSpawn(...)
            → Calls GenPlace.TryPlaceThing(...)
              → Calls TryPlaceDirect(...)
                → If stacking: thing2.TryAbsorbStack(thing) → placedAction(thing2, count)
                → If new cell: SplitAndSpawnOneStackOnCell → placedAction(thing, count)
```

The `placedAction` callback is invoked deep in the placement chain with the **actual** `(Thing, int)` that was placed or absorbed. This is very precise — the Thing reference is the exact object that ended up on the map.

## Approach: Postfix Wrapping initAction

### How it works

```csharp
[HarmonyPatch(typeof(Toils_Haul), nameof(Toils_Haul.PlaceHauledThingInCell))]
static void Postfix(Toil __result, TargetIndex cellInd) {
    Action originalAction = __result.initAction;
    __result.initAction = delegate {
        // ... snapshot, run original, track placement for R4 jobs
    };
}
```

### Why postfix on a factory method works

`PlaceHauledThingInCell` is a **static factory method** that creates a `Toil` and sets its `initAction`. A Harmony postfix on this method receives the `Toil __result` *after* the original method has fully created it (including setting `initAction`). We then replace `initAction` with a wrapper that calls the original.

This is a well-established Harmony pattern for modifying factory-returned objects.

### Ordering with other mods

If another mod ALSO patches `PlaceHauledThingInCell` with a postfix:
- **Their postfix runs first, ours runs second** (or vice versa, depending on Harmony priority): Our wrapper captures whatever `initAction` is present at the time our postfix runs. If their postfix already wrapped `initAction`, we wrap their wrapper. This is correct — the chain unwinds in order.
- **Their postfix doesn't touch `initAction`**: Our wrapper runs the original unmodified action. No conflict.
- **They use a prefix**: Their prefix runs before the factory method. The method still produces a Toil. Our postfix wraps it. No conflict.
- **They use a transpiler on the factory body**: Our postfix still receives the final `Toil`. We wrap whatever `initAction` the transpiled factory produced. No conflict.

**Only problematic case:** Another mod replaces `initAction` in a postfix that runs AFTER ours, discarding our wrapper. This is highly unlikely — no common mod patches `PlaceHauledThingInCell`.

## Alternative Approaches Considered

### 1. Transpiler — inject job defs into whitelist

```csharp
// Would add: || curJob.def == R4DefOf.RRRR_Repair || curJob.def == R4DefOf.RRRR_Clean
```

**Pros:** Most precise — uses vanilla's own `placedAction` callback mechanism, which fires with the exact placed Thing.  
**Cons:** Transpilers are fragile across game versions. The whitelist `if` chain is IL that could change. CLAUDE.md Harmony hierarchy: transpiler is last resort.  
**Verdict:** Rejected — too fragile.

### 2. Custom toil inserted after PlaceHauledThingInCell in CollectIngredientsToils

We call `CollectIngredientsToils` as a static helper — we don't control its toil sequence. We'd need to:
- Not use `CollectIngredientsToils` at all
- Reimplement the entire collection sequence in our own code
- Or insert a custom toil into the yielded sequence somehow

**Pros:** No Harmony patch needed.  
**Cons:** Reimplementing `CollectIngredientsToils` duplicates ~50 lines of complex vanilla code that handles queue extraction, stacking, physical reservation, etc. Maintenance burden when vanilla updates.  
**Verdict:** Rejected — too much duplication, too fragile across updates.

### 3. Don't track ingredients at all — use spatial consumption (current approach)

The current `ConsumeIngredientsOnBench` scans bench cells and nearby area for matching ThingDefs.

**Pros:** No patch needed.  
**Cons:** This is the root cause of all current bugs. It consumes wrong materials, consumes from stockpiles, or fails to consume at all. It can't distinguish "placed by this job" from "happened to be near the bench."  
**Verdict:** Rejected — this is what we're fixing.

### 4. Manual UpdateJobWithPlacedThings in a postfix on TryDropCarriedThing

Patch `Pawn_CarryTracker.TryDropCarriedThing` to call `UpdateJobWithPlacedThings` when the pawn is doing an R4 job.

**Pros:** Lower in the call chain, closer to the actual drop.  
**Cons:** `TryDropCarriedThing` is called in MANY contexts (not just ingredient placement). We'd need to guard against false positives. Also, this method is an instance method on a hot path — wider blast radius for conflicts.  
**Verdict:** Rejected — too broad, too risky.

### 5. Override CollectIngredientsToils entirely in R4 JobDrivers

Copy the entire `CollectIngredientsToils` method into R4 code and modify the `PlaceHauledThingInCell` call to include our job defs.

**Pros:** Complete control, no Harmony patch on vanilla code.  
**Cons:** 50+ lines of duplicated vanilla code that WILL diverge on game updates. Violates vanilla-first principle.  
**Verdict:** Rejected — maintenance burden.

## Chosen Approach: Postfix wrapping initAction (Option 1)

### Implementation Detail: Snapshot vs. Callback

There are two sub-approaches for the postfix wrapper:

**A. Snapshot approach** (what the work order specifies):
```csharp
Thing carriedBefore = actor.carryTracker.CarriedThing;
int countBefore = carriedBefore?.stackCount ?? 0;
originalAction();
// Compare before/after to determine what was placed
```

**B. Re-inject placedAction approach:**
Instead of snapshotting, we modify the original `initAction`'s captured locals by re-running the whitelist logic ourselves:
```csharp
// This is NOT feasible — we can't modify the delegate's captured variables
```

Actually, there's a better sub-approach:

**C. Pre-set placedAction before the original runs:**
The original `initAction` checks `curJob.def` to decide whether to set `placedAction`. What if we modify the job's def temporarily? No — that's extremely dangerous and would break other systems.

**D. Wrap TryDropCarriedThing call specifically within our initAction wrapper:**
In our wrapped initAction, BEFORE calling the original, we could:
1. Store the carried thing reference
2. Replace the pawn's carryTracker's TryDropCarriedThing method? No — can't do that at runtime.

The snapshot approach (A) is the most straightforward. Let me analyse its edge cases:

### Edge Cases for Snapshot Approach

#### Case 1: Normal placement (thing drops to empty cell)
- Before: carrying 6 steel
- Original runs: drops 6 steel on cell
- After: carrying nothing
- Placed = 6 steel
- Find thing on cell → reference to the spawned 6-steel stack
- `UpdateJobWithPlacedThings(job, steelStack, 6)` ✓

#### Case 2: Stacking with existing material
- Before: carrying 6 steel
- Original runs: 6 steel absorbed into existing 20-steel stack on cell → stack becomes 26
- After: carrying nothing
- Placed = 6
- Find thing on cell → reference to the 26-steel stack (the absorber)
- `UpdateJobWithPlacedThings(job, steelStack26, 6)` ✓
  
This correctly tracks that 6 of the 26 stack are "ours." Consumption later will reduce by 6.

**BUT:** If `ConsumeFromPlacedThings` tries to consume from this ThingCountClass, it needs to handle the case where `ptc.thing.stackCount` (26) > `ptc.Count` (6). It should only consume 6, not 26. This is handled correctly in the proposed `ConsumeFromPlacedThings` implementation.

#### Case 3: Partial placement (split stack)
- Before: carrying 20 steel, job needs 6
- Original runs: `StartCarryThing` already split to carry only 6 (or exactly count), then `PlaceHauledThingInCell` drops all carried
- After: carrying nothing (all 6 were dropped)
- Placed = 6
- This actually shouldn't result in partial placement. `StartCarryThing` uses `curJob.count` to limit pickup. By the time we place, the pawn carries exactly what's needed.

But what about `putRemainderInQueue: true` in `CollectIngredientsToils`? If the pawn can't carry all of one ingredient stack, the remainder goes back to the queue and another pickup cycle runs. Each placement is a complete drop of what was carried.

#### Case 4: Failed placement
- Before: carrying 6 steel
- Original runs: `TryDropCarriedThing` returns false (no valid cell)
- After: still carrying 6 steel
- Placed = 0
- No tracking needed ✓
- The original initAction jumps to `nextToilOnPlaceFailOrIncomplete` (the `findPlaceTarget` toil) to retry

#### Case 5: Multiple placements for same material
- Multiple pickup-place cycles for different stacks of same material (e.g., 6 steel from stack A, 6 steel from stack B)
- First cycle: `UpdateJobWithPlacedThings(job, thingA, 6)` → `placedThings = [{thingA, 6}]`
- Second cycle: `UpdateJobWithPlacedThings(job, thingB, 6)` → `placedThings = [{thingA, 6}, {thingB, 6}]`
- Unless thingA and thingB stacked on the same cell, in which case:
  - First cycle places thingA → `placedThings = [{thingA, 6}]`
  - Second cycle: thingB is absorbed into thingA → FindPlacedThing finds thingA (same reference)
  - `UpdateJobWithPlacedThings(job, thingA, 6)` → thingA's count becomes 12
  - ✓ Correct total

#### Case 6: FindPlacedThing finds wrong thing
- What if there's already an identical ThingDef on the cell that ISN'T our placed ingredient?
- Scenario: stockpile zone overlaps bench cell, pre-existing steel stack on bench cell
- Our `FindPlacedThing` returns the pre-existing stack
- `UpdateJobWithPlacedThings` tracks the pre-existing stack as "ours"
- When consuming: we consume from the pre-existing stack, which is... actually fine? We're consuming steel, and steel is steel.
- The total count tracked matches what we placed. The reference might point to a merged stack, but consumption will reduce by the right amount.

**Risk:** If the pre-existing stack is consumed by another job between placement and consumption, `FailOnDespawnedNullOrForbiddenPlacedThings` will fire and the job will fail cleanly. Acceptable.

### Confidence Assessment

| Scenario | Confidence | Notes |
|---|---|---|
| Normal placement, no stacking | 95% | Straightforward — snapshot works perfectly |
| Stacking with existing material | 85% | FindPlacedThing returns merged stack — tracked count is correct, reference is valid |
| Multiple ingredient types | 95% | Each placement cycle is independent — each type tracked separately |
| Save/load mid-job | 95% | `job.placedThings` IS saved (confirmed: `Scribe_Collections.Look` with `IExposable`) |
| Failed placement | 95% | Original handles retry; snapshot detects no change |
| Other mods patching same method | 80% | Delegate wrapping is ordered — but if another mod replaces initAction AFTER us, our wrapper is lost |
| 1×1 benches | 85% | Ingredients may land on adjacent cells via radial fallback — FindPlacedThing searches only the target cell. If placement redirected to a different cell, we won't find the thing. |

**The 1×1 bench case deserves more attention.** In vanilla's `CollectIngredientsToils`, the flow is:
1. `SetTargetToIngredientPlaceCell` — finds a valid cell near the bench
2. `PlaceHauledThingInCell(cellInd, ...)` — drops at that cell

`SetTargetToIngredientPlaceCell` tries `IngredientStackCells` first, then radial fallback. It sets `job.GetTarget(cellInd)` to the chosen cell. `PlaceHauledThingInCell` then drops at that cell.

If the drop succeeds via `ThingPlaceMode.Direct`, the thing is at exactly the target cell → `FindPlacedThing` finds it. ✓

If `Direct` fails and the original code uses `nextToilOnPlaceFailOrIncomplete` (which is `findPlaceTarget`) to retry with a different cell, the loop continues. On retry, `SetTargetToIngredientPlaceCell` picks a new cell, and the next `PlaceHauledThingInCell` call uses that new cell. ✓

**BUT:** In the original `PlaceHauledThingInCell` code, if `TryDropCarriedThing(cell, Direct)` fails AND `storageMode` is false AND `nextToilOnPlaceFailOrIncomplete` is not null, it jumps to the retry toil. It does NOT try `ThingPlaceMode.Near`. So the item stays carried and the loop retries.

This means the thing is ALWAYS placed at exactly the cell in `cellInd` when `storageMode: false` (which is what `CollectIngredientsToils` uses). If it can't place there, it retries with a different cell. **`FindPlacedThing` at the target cell will always work for `CollectIngredientsToils`.** ✓

### Revised Confidence: 90% overall

The remaining 10% is:
- 5%: Other mods patching `PlaceHauledThingInCell` in a way that conflicts
- 3%: Unexpected edge case in stacking/merging that causes `FindPlacedThing` to miss
- 2%: Unknown unknowns

### Recommendation

Proceed with the postfix-wrapping approach. It is well-understood, follows established Harmony patterns, and handles all identified edge cases correctly. The `FindPlacedThing` lookup is the one area to watch during testing — the test plan's category A tests (especially A5: crafting spot) will exercise this.
