# Work Order: R4 Workbench Integration — Vanilla Alignment

## Summary

R4's custom `JobDriver_R4Repair` and `JobDriver_R4Clean` diverge from vanilla's `JobDriver_DoBill` in 8 critical areas, causing bugs including free repairs, wrong ingredient consumption, work speed tied to game speed, bench fuel not consumed, and jobs continuing on burning/unpowered benches. This work order aligns both drivers with vanilla behaviour by: (1) fixing ingredient tracking via a `PlaceHauledThingInCell` postfix patch, (2) replacing spatial ingredient consumption with `job.placedThings`-based consumption, (3) splitting work toils into `tickAction`/`tickIntervalAction`, (4) adding all missing fail conditions and guards, and (5) adding `HaulStuffOffBillGiverJob` to WorkGivers.

A shared base class `JobDriver_R4WorkBase` is introduced to eliminate duplication between Repair and Clean — every fix is implemented once.

## Files

### Modified

- `Source/Jobs/JobDriver_R4Repair.cs` — refactor to inherit from `JobDriver_R4WorkBase`; move all shared logic to base
- `Source/Jobs/JobDriver_R4Clean.cs` — refactor to inherit from `JobDriver_R4WorkBase`; move all shared logic to base
- `Source/Patches/Patch_PlaceHauledThingInCell.cs` — implement the ingredient tracking postfix
- `Source/Utility/MaterialUtility.cs` — add `ConsumeFromPlacedThings` method; mark `ConsumeIngredientsOnBench` as [Obsolete] but retain for now
- `Source/Jobs/WorkGiver_R4Repair.cs` — add `HaulStuffOffBillGiverJob` call; simplify `HasJobOnThing` to delegate to `JobOnThing`; add `job.haulMode`
- `Source/Jobs/WorkGiver_R4Clean.cs` — same changes as `WorkGiver_R4Repair`
- `Source/Jobs/WorkGiver_R4RepairBill.cs` — add `HaulStuffOffBillGiverJob` call; add interaction cell reservation check
- `Source/Jobs/WorkGiver_R4CleanBill.cs` — same changes as `WorkGiver_R4RepairBill`

### New

- `Source/Jobs/JobDriver_R4WorkBase.cs` — abstract base class for shared Repair/Clean job logic

### Not touched

- `Source/Jobs/JobDriver_R4Recycle.cs` — different architecture (no ingredients, uses `TargetA`=item / `TargetB`=bench). The recycle driver should also get `tickIntervalAction`, `FailOnBurningImmobile`, and `UsedThisTick` in a separate pass — but this work order focuses on the ingredient-driven drivers.

---

## Steps

### Step 0: Create `JobDriver_R4WorkBase` abstract base class

Create `Source/Jobs/JobDriver_R4WorkBase.cs`. This class contains all shared logic between Repair and Clean. The concrete subclasses override only:

- `protected abstract Thing GetWorkItem()` — returns `RepairItem` or `CleanItem`
- `protected abstract DesignationDef WorkDesignationDef { get; }` — `R4_Repair` or `R4_Clean`
- `protected abstract float CalculateTotalWork(Thing item)` — work amount formula
- `protected abstract void ApplyWorkResult(Thing item, Pawn worker)` — repair cycle or taint removal
- `protected abstract List<ThingDefCountClass> GetCycleCost(Thing item)` — materials for this cycle
- `protected abstract bool ShouldWorkContinue(Thing item)` — e.g. repair checks HP < max; clean always false (single-shot)
- `protected abstract bool IsWorkItemStillValid(Thing item)` — custom per-subclass validity (clean checks `WornByCorpse`)
- `protected abstract float GetSkillXpPerTick()` — 0.12f for repair, 0.08f for clean
- `protected abstract float GetSkillSpeedBonus(int skillLevel)` — 1.0f for repair, `1 + skill*0.03` for clean

The base class implements ALL of `MakeNewToils`, `TryMakePreToilReservations`, `ExposeData`, and `GetReport`. This ensures every fix below is applied once.

### Step 1: Fix `TryMakePreToilReservations` — add interaction cell reservation

In the base class, after reserving the bench, add:

```csharp
Thing bench = Bench;
if (bench != null && bench.def.hasInteractionCell)
{
    if (!pawn.ReserveSittableOrSpot(bench.InteractionCell, job, errorOnFailed))
        return false;
}
```

**Vanilla reference:** `JobDriver_DoBill.TryMakePreToilReservations` — exact same pattern.

### Step 2: Fix `MakeNewToils` global fail conditions

Add these in this order at the top of `MakeNewToils`:

```csharp
// 1. End condition: bench must remain spawned (matches vanilla AddEndCondition)
AddEndCondition(delegate
{
    Thing bench = GetActor().jobs.curJob.GetTarget(BenchInd).Thing;
    return (!(bench is Building) || bench.Spawned) ? JobCondition.Ongoing : JobCondition.Incompletable;
});

// 2. Burning check (matches vanilla FailOnBurningImmobile)
this.FailOnBurningImmobile(BenchInd);

// 3. Bill validity + bench usability (matches vanilla FailOn delegate)
this.FailOn(delegate
{
    Thing benchThing = job.GetTarget(BenchInd).Thing;
    if (benchThing is IBillGiver bg && !bg.CurrentlyUsableForBills())
        return true;
    Thing item = GetWorkItem();
    if (item == null || item.Destroyed) return true;
    if (!IsWorkItemStillValid(item)) return true;
    if (IsBillDriven) return job.bill.DeletedOrDereferenced || job.bill.suspended;
    if (item.Map != null && item.Map.designationManager.DesignationOn(item, WorkDesignationDef) == null)
        return true;
    return false;
});
```

**What was wrong before:** Missing `FailOnBurningImmobile`, `AddEndCondition`, `CurrentlyUsableForBills()`. These allowed pawns to continue working on burning, unpowered, or despawned benches.

### Step 3: Add fail conditions to `gotoItem` toil

```csharp
gotoItem.AddFailCondition(() => {
    Thing item = GetWorkItem();
    return item == null || item.Destroyed || item.IsForbidden(pawn);
});
```

### Step 4: Fix work toil — split `tickAction` / `tickIntervalAction`

This is the critical timing fix. The work toil must have two delegates:

```csharp
workToil.defaultCompleteMode = ToilCompleteMode.Never;
```

Completion is driven by `cycleWorkLeft` and `ReadyForNextToil()`, not by delay expiry. Keep `workToil.defaultCompleteMode` as `ToilCompleteMode.Never`. `tickIntervalAction` is driven by `DriverTickInterval(delta)`, so all work/XP changes must scale by `delta`. If you need a different cadence, set `workToil.defaultDuration` explicitly and keep the same `delta` scaling in `tickIntervalAction`.

**`tickAction`** (fires every tick, regardless of game speed):
```csharp
workToil.tickAction = delegate
{
    Thing item = GetWorkItem();
    if (item == null || item.Destroyed) { EndJobWith(JobCondition.Incompletable); return; }
    pawn.rotationTracker.FaceTarget(Bench);
    // Fuel consumption + bench animation (matches vanilla Toils_Recipe.DoRecipeWork)
    if (Bench is IBillGiverWithTickAction tickBench)
        tickBench.UsedThisTick();
};
```

**`tickIntervalAction(int delta)`** (fires at game-speed-adjusted intervals):
```csharp
workToil.tickIntervalAction = delegate(int delta)
{
    Thing item = GetWorkItem();
    if (item == null || item.Destroyed) { EndJobWith(JobCondition.Incompletable); return; }

    float speed       = pawn.GetStatValue(StatDefOf.GeneralLaborSpeed, true);
    float benchFactor = Bench.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor, true);
    int skillLevel    = pawn?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
    float skillBonus  = GetSkillSpeedBonus(skillLevel);

    cycleWorkLeft -= speed * benchFactor * skillBonus * delta;

    pawn.skills?.Learn(SkillDefOf.Crafting, GetSkillXpPerTick() * delta);

    if (cycleWorkLeft <= 0f)
        ReadyForNextToil();

    // Periodic job override check (matches vanilla — every 1000 ticks after 3000)
    if (pawn.IsHashIntervalTick(1000, delta))
        pawn.jobs.CheckForJobOverride();
};
```

**What this fixes:**
- Work speed no longer scales with game speed (multiply by `delta`)
- XP gain no longer scales with game speed
- Fuel is consumed during work (`UsedThisTick`)
- Bench animations play
- Higher-priority jobs can interrupt long work

### Step 5: Add `FailOnDespawnedNullOrForbiddenPlacedThings` to work toil

After `job.placedThings` is reliably populated (Step 6), add to the work toil:

```csharp
workToil.FailOnDespawnedNullOrForbiddenPlacedThings(BenchInd);
```

This makes the job fail cleanly if an ingredient disappears mid-work (stolen, decayed, forbidden).

### Step 6: Implement `Patch_PlaceHauledThingInCell` — ingredient tracking

**This is the most critical and most complex change.** See detailed implementation notes below.

The patch is a **Harmony postfix** on `Toils_Haul.PlaceHauledThingInCell`. The method is a **static factory** that returns a `Toil`. The postfix receives the returned `Toil` and wraps its `initAction`.

```csharp
[HarmonyPatch(typeof(Toils_Haul), nameof(Toils_Haul.PlaceHauledThingInCell))]
public static class Patch_PlaceHauledThingInCell
{
    static void Postfix(Toil __result, TargetIndex cellInd)
    {
        // Capture the original initAction delegate
        Action originalAction = __result.initAction;

        __result.initAction = delegate
        {
            Pawn actor = __result.actor;
            Job curJob = actor.jobs.curJob;

            // Snapshot what the pawn is carrying BEFORE the original action fires
            Thing carriedBefore = actor.carryTracker.CarriedThing;
            int countBefore = carriedBefore?.stackCount ?? 0;

            // Run the original placement logic
            originalAction();

            // If this is an R4 job, manually track what was placed
            if (curJob.def != R4DefOf.RRRR_Repair && curJob.def != R4DefOf.RRRR_Clean)
                return;

            // After the original action, check what was actually placed:
            // If the pawn is no longer carrying the same thing (or at all),
            // something was placed. We need to call UpdateJobWithPlacedThings.
            Thing carriedAfter = actor.carryTracker.CarriedThing;
            int countAfter = carriedAfter?.stackCount ?? 0;

            if (carriedBefore == null)
                return; // Nothing was being carried — shouldn't happen but guard

            int placed;
            if (carriedAfter == null || carriedAfter != carriedBefore)
            {
                // The entire carried thing was placed (or it was absorbed into a stack)
                placed = countBefore;
            }
            else
            {
                // Partial placement (split off)
                placed = countBefore - countAfter;
            }

            if (placed > 0)
            {
                // Find the placed thing — it's at the target cell
                IntVec3 cell = curJob.GetTarget(cellInd).Cell;
                Thing placedThing = FindPlacedThing(actor.Map, cell, carriedBefore.def);
                if (placedThing != null)
                {
                    HaulAIUtility.UpdateJobWithPlacedThings(curJob, placedThing, placed);
                }
            }
        };
    }

    // Find the thing of matching def at the target cell
    private static Thing FindPlacedThing(Map map, IntVec3 cell, ThingDef def)
    {
        List<Thing> things = map.thingGrid.ThingsListAt(cell);
        for (int i = things.Count - 1; i >= 0; i--)
        {
            if (things[i].def == def && things[i].def.category == ThingCategory.Item)
                return things[i];
        }
        return null;
    }
}
```

**Why this approach (postfix on factory, wrapping initAction)?**

The alternative approaches and why they were rejected:

1. **Prefix on `PlaceHauledThingInCell`**: Can't modify the returned Toil — the prefix fires before the Toil is created.

2. **Transpiler on `PlaceHauledThingInCell`**: Could inject R4 job defs into the whitelist check. This is the cleanest approach conceptually, but transpilers are fragile and the #1 source of mod conflicts (CLAUDE.md rule #6 hierarchy). The whitelist is a simple `if` chain that could change between game versions.

3. **Postfix on `PlaceHauledThingInCell` wrapping `initAction`**: This is what we do. The postfix receives `Toil __result` and replaces its `initAction` with a wrapper that snapshots carry state, runs the original, then calls `UpdateJobWithPlacedThings` for R4 jobs. **This is safe because:**
   - The factory method creates a fresh `Toil` each call
   - The original `initAction` is captured by closure reference
   - The wrapper adds behaviour without removing any
   - Other mods patching the same method get the same Toil — our wrapper runs their modifications too since we capture the final delegate

4. **Alternative: Don't patch at all — use `job.placedThings` manually**: We could manually call `UpdateJobWithPlacedThings` in a custom "afterPlace" toil inserted into the `CollectIngredientsToils` sequence. But `CollectIngredientsToils` is a static method we call but don't control — we can't inject toils into its sequence without a transpiler.

**Critical implementation note:** The `placedAction` callback in vanilla's code is more precise — it fires directly from `TryPlaceDirect`/`TryAbsorbStack` with the exact `(Thing, count)` that was placed or absorbed. Our snapshot-based approach is slightly less precise for stacking scenarios (if the placed material stacks with pre-existing material on the cell, the `Thing` reference may point to the combined stack). However, `UpdateJobWithPlacedThings` handles this correctly — it just needs a `Thing` reference and a count, and it accumulates via `ThingCountClass`.

**Edge cases to test:**
- Material stacking with pre-existing identical material on bench cell
- Split stacks (pawn carries more than needed, remainder goes back to queue)
- Multiple ingredient types placed in sequence
- 1×1 benches (crafting spot) where ingredients may not fit on the bench cell

### Step 7: Replace `ConsumeIngredientsOnBench` with `job.placedThings`-based consumption

Add a new method to `MaterialUtility`:

```csharp
public static void ConsumeFromPlacedThings(Job job, List<ThingDefCountClass> expectedCosts)
{
    if (job.placedThings == null || job.placedThings.Count == 0)
    {
        // Fallback: log a warning but don't silently skip
        Log.Warning("[RRRR] ConsumeFromPlacedThings: job.placedThings is null or empty. " +
                    "Ingredients may not be consumed. Job: " + job);
        return;
    }

    // Build a remaining-count table from expected costs
    var remaining = new Dictionary<ThingDef, int>();
    for (int i = 0; i < expectedCosts.Count; i++)
    {
        var entry = expectedCosts[i];
        if (entry.thingDef == null || entry.count <= 0) continue;
        if (remaining.TryGetValue(entry.thingDef, out int existing))
            remaining[entry.thingDef] = existing + entry.count;
        else
            remaining[entry.thingDef] = entry.count;
    }

    // Consume from placed things, matching by ThingDef
    for (int i = 0; i < job.placedThings.Count; i++)
    {
        ThingCountClass ptc = job.placedThings[i];
        if (ptc.thing == null || ptc.Count <= 0) continue;
        if (!remaining.TryGetValue(ptc.thing.def, out int need)) continue;

        int toConsume = Mathf.Min(need, ptc.Count);
        need -= toConsume;

        if (need <= 0)
            remaining.Remove(ptc.thing.def);
        else
            remaining[ptc.thing.def] = need;

        // Actually destroy or reduce the thing
        if (toConsume >= ptc.thing.stackCount)
            ptc.thing.Destroy();
        else
            ptc.thing.stackCount -= toConsume;

        // Update the placed count tracker
        ptc.Count -= toConsume;
    }

    if (remaining.Count > 0)
    {
        Log.Warning("[RRRR] ConsumeFromPlacedThings: not all expected ingredients were found. " +
                    "Missing: " + string.Join(", ",
                        remaining.Select(kv => $"{kv.Key.defName}x{kv.Value}")));
    }
    else
    {
        job.placedThings = null;
    }
}
```

In the `finishToil.initAction` of the base class, replace:
```csharp
// OLD:
MaterialUtility.ConsumeIngredientsOnBench(Bench, pawn.Map, cycleCost);

// NEW:
MaterialUtility.ConsumeFromPlacedThings(job, cycleCost);
```

### Step 8: Add `HaulStuffOffBillGiverJob` to designation WorkGivers

In `WorkGiver_R4Repair.JobOnThing` and `WorkGiver_R4Clean.JobOnThing`, after finding the bench but before creating the R4 job:

```csharp
if (bench is IBillGiver bg)
{
    Job haulOff = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, bg, null);
    if (haulOff != null) return haulOff;
}
```

Same in `WorkGiver_R4RepairBill.JobOnThing` and `WorkGiver_R4CleanBill.JobOnThing`.

### Step 9: Simplify `HasJobOnThing` in designation WorkGivers

Change from duplicating the ingredient search to:

```csharp
public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
{
    return JobOnThing(pawn, t, forced) != null;
}
```

This eliminates the double `TryFindIngredients` call per tick.

### Step 10: Add `job.haulMode` to WorkGiver job creation

In all 4 WorkGivers, when creating the R4 job, add:

```csharp
job.haulMode = HaulMode.ToCellNonStorage;
```

This matches vanilla's `TryStartNewDoBillJob`.

### Step 11: Add `RecordsUtility.Notify_BillDone` to finish toil

In the base class `finishToil`, after applying results:

```csharp
RecordsUtility.Notify_BillDone(pawn, new List<Thing> { item });
```

### Step 12: Add `bill.Notify_IterationCompleted` in finish toil (bill-driven path)

Already present in current code — verify it remains after the refactor.

---

## Interfaces and Signatures

### New abstract base class

```csharp
public abstract class JobDriver_R4WorkBase : JobDriver
{
    protected const TargetIndex BenchInd      = TargetIndex.A;
    protected const TargetIndex IngredientInd = TargetIndex.B;
    protected const TargetIndex CellInd       = TargetIndex.C;

    protected float cycleWorkLeft;
    protected float cycleWorkTotal;

    protected Thing Bench        => job.GetTarget(BenchInd).Thing;
    protected bool  IsBillDriven => job.bill != null;

    // ── Abstract hooks for subclasses ──
    protected abstract Thing GetWorkItem();
    protected abstract DesignationDef WorkDesignationDef { get; }
    protected abstract float CalculateTotalWork(Thing item);
    protected abstract void ApplyWorkResult(Thing item, Pawn worker);
    protected abstract List<ThingDefCountClass> GetCycleCost(Thing item);
    protected abstract bool ShouldContinueWorking(Thing item);
    protected abstract bool IsWorkItemStillValid(Thing item);
    protected abstract float GetSkillXpPerTick();
    protected abstract float GetSkillSpeedBonus(int skillLevel);
    protected abstract string GetJobReportKey();
    protected abstract void OnItemDestroyed(Thing item);
}
```

### Patch target (vanilla)

```csharp
// Verse.AI.Toils_Haul
public static Toil PlaceHauledThingInCell(
    TargetIndex cellInd,
    Toil nextToilOnPlaceFailOrIncomplete,
    bool storageMode,
    bool tryStoreInSameStorageIfSpotCantHoldWholeStack = false)
```

### Vanilla methods used (all confirmed via MCP)

```csharp
// Verse.AI.ReservationUtility — extension method on Pawn
public static bool ReserveSittableOrSpot(this Pawn pawn, IntVec3 pos, Job job, bool errorOnFailed = true)

// Verse.AI.HaulAIUtility
public static void UpdateJobWithPlacedThings(Job curJob, Thing th, int added)

// Verse.AI.ToilFailConditions — extension method on Toil
public static Toil FailOnDespawnedNullOrForbiddenPlacedThings(this Toil toil, TargetIndex containerIndex = TargetIndex.None)

// RimWorld.IBillGiverWithTickAction
void UsedThisTick()

// RimWorld.WorkGiverUtility
public static Job HaulStuffOffBillGiverJob(Pawn pawn, IBillGiver giver, Thing thingToIgnore)

// RimWorld.RecordsUtility
public static void Notify_BillDone(Pawn billDoer, List<Thing> products)

// RimWorld.Bill_Production
public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
```

---

## Save Compatibility

1. **Adds/removes/renames a `Scribe_*` field?** — No. `cycleWorkLeft` and `cycleWorkTotal` keep the same field names. No new persistent state.
2. **Changes an enum's ordinal values?** — No.
3. **Can the mod be added mid-save?** — Yes. Same as before — no new persistent state.
4. **Can the mod be removed mid-save?** — Same risks as before (designations drop, bills with missing recipes need cleanup). No new risks from this change.

**Net impact:** Zero save-compat risk. This is a pure logic fix.

---

## Constraints

- **CLAUDE.md #6:** Default to postfix. The `PlaceHauledThingInCell` patch is a postfix — no cancelling prefix needed.
- **CLAUDE.md #7:** `[StaticConstructorOnStartup]` separate from `Mod` subclass — already correct.
- **CLAUDE.md #12:** Bench in `TargetA` — already correct.
- **Vanilla-first:** Every fix moves toward vanilla alignment. No custom solutions where vanilla mechanisms exist.
- **`Patch_PlaceHauledThingInCell`:** Must handle the case where `__result.initAction` is null (shouldn't happen in practice but defensive). Must not assume the Toil's actor exists at patch time (it's set later when the toil runs).

---

## Verification

1. Build cleanly — zero errors, zero warnings in the mod's own code.
2. Launch RimWorld with the mod enabled.
3. Check `Player.log` — zero errors at startup.
4. Load test save and exercise features per the test plan in `TESTING.md`.

### Observability

**When working correctly:**
- `Player.log` should show no warnings from `[RRRR]` prefix
- Pawn walks to ingredient → carries to bench → drops on bench cell → walks to item → carries to bench → drops → works → finishes
- Progress bar advances at consistent rate regardless of game speed
- Bench animation plays during work (mote emitter)
- Fuel-burning benches visibly consume fuel
- After finish, ingredients should be gone from bench cells

**When something goes wrong:**
- `[RRRR] ConsumeFromPlacedThings: job.placedThings is null or empty` — the patch didn't fire or `CollectIngredientsToils` didn't use `PlaceHauledThingInCell`
- `[RRRR] ConsumeFromPlacedThings: not all expected ingredients were found` — stacking or splitting edge case in placement
- Pawn completes work but ingredients remain on bench — consumption logic failed
- Pawn completes work but item HP unchanged — repair logic skipped (check `ApplyWorkResult`)

### Key Manual Tests (see `TESTING.md` for full plan)

1. **Repair with materials** — designate damaged steel longsword, verify steel consumed
2. **Minor mending** — designate item at 96% HP, verify no materials consumed
3. **Clean tainted apparel** — verify materials consumed, taint removed
4. **Game speed invariance** — repair at 1× and 3× speed, verify same wall-clock duration
5. **Bench power loss** — unpower bench mid-work, verify job ends cleanly
6. **Bench fire** — set bench on fire mid-work, verify pawn exits
7. **Ingredient stolen** — forbid ingredients mid-work, verify job fails
8. **Interrupted job** — draft pawn mid-work, verify next pawn clears stale ingredients
9. **1×1 bench** — repair at crafting spot, verify ingredients consumed correctly
10. **Modded items** — test with gas mask and combat shield (see `TESTING.md`)
