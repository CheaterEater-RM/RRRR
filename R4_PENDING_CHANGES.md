# R4 Pending Changes

All changes below are **not yet implemented** in the current codebase. Apply and test each one individually before moving to the next.

---

## 1. `Patch_PlaceHauledThingInCell` — ingredient tracking for R4 jobs

**Why:** Vanilla `PlaceHauledThingInCell` only populates `job.placedThings` (via `HaulAIUtility.UpdateJobWithPlacedThings`) for a hardcoded whitelist of job defs (`DoBill`, `RecolorApparel`, etc.). R4's custom job defs are absent, so `job.placedThings` is never populated. This means:
- `FailOnDespawnedNullOrForbiddenPlacedThings` on the work toil has nothing to watch — stolen ingredients are undetected.
- `finishToil` has no tracked references to consume from.

**How:** Postfix on `Toils_Haul.PlaceHauledThingInCell`. The postfix wraps the returned Toil's `initAction`. Before the original fires, it snapshots `pawn.carryTracker.CarriedThing` and its `stackCount`. After the original fires, if the pawn is no longer carrying the same amount, it calls `HaulAIUtility.UpdateJobWithPlacedThings` for `RRRR_Repair` and `RRRR_Clean` jobs.

**Critical companion change:** The `dropItem` toil in both `JobDriver_R4Repair` and `JobDriver_R4Clean` must shield `job.placedThings` during execution — save it, null it out before the drop, restore it after. Without this, the patch also records the *work item* (the thing being repaired/cleaned) as a tracked ingredient, causing `finishToil` to destroy it.

**`finishToil` consumption:** Once `job.placedThings` is reliably populated, replace the `ConsumeIngredientsOnBench` call with direct consumption from `job.placedThings`, mirroring vanilla's `CalculateIngredients` + `ConsumeIngredients`.

**`workToil` guard:** Add `FailOnDespawnedNullOrForbiddenPlacedThings(BenchInd)` to the work toil so the job fails cleanly if an ingredient disappears mid-work.

---

## 2. `TryMakePreToilReservations` — interaction cell not reserved

**Why:** Vanilla `JobDriver_DoBill` calls `pawn.ReserveSittableOrSpot(bench.InteractionCell, job)` when the bench has `hasInteractionCell = true`. Without this, two pawns can attempt to use the same bench simultaneously and collide at the interaction cell.

**How:** After reserving the bench, add:
```csharp
Thing bench = Bench;
if (bench != null && bench.def.hasInteractionCell)
    if (!pawn.ReserveSittableOrSpot(bench.InteractionCell, job, errorOnFailed))
        return false;
```

**Applies to:** `JobDriver_R4Repair` and `JobDriver_R4Clean`.

---

## 3. `FailOnBurningImmobile` missing

**Why:** If the bench catches fire while the pawn is working, the pawn continues indefinitely. Vanilla `JobDriver_DoBill` adds this as a global condition.

**How:** Add `this.FailOnBurningImmobile(BenchInd);` immediately after `this.FailOnDestroyedNullOrForbidden(BenchInd);` in `MakeNewToils`.

**Applies to:** `JobDriver_R4Repair` and `JobDriver_R4Clean`.

---

## 4. `CurrentlyUsableForBills()` not checked mid-job

**Why:** If the bench loses power or runs out of fuel after the pawn has started working, the pawn continues indefinitely. Vanilla checks this in the `FailOn` conditions and also in the work toil's tick.

**How:** Add to the global `FailOn` delegate:
```csharp
if (bench is IBillGiver bg && !bg.CurrentlyUsableForBills()) return true;
```
Also add `bench.Spawned` check in case the bench is destroyed mid-job after `FailOnDestroyedNullOrForbidden` has already cleared.

**Applies to:** `JobDriver_R4Repair` and `JobDriver_R4Clean`.

---

## 5. Work progression timing

**Status:** Implemented centrally in `JobDriver_R4WorkBase`.

**Current rule:** `workToil.tickAction` is only for per-tick concerns (null check, `EndJobWith`, facing, `IBillGiverWithTickAction.UsedThisTick()`). `workToil.tickIntervalAction(int delta)` is responsible for advancing work, XP, and other delta-scaled progress.

**Do not regress:** Never call `AdvanceWork` from both delegates. Work progression must live only in `tickIntervalAction`, with every progress/XP change multiplied by `delta`.

**Applies to:** Shared repair/clean flow in `JobDriver_R4WorkBase`.

---

## 6. `UsedThisTick()` not called

**Why:** `Building_WorkTable.UsedThisTick()` drives `CompRefuelable.Notify_UsedThisTick()` (fuel consumption) and `CompMoteEmitter` (bench animations). Without it, fuel-burning benches (campfire, fueled smithy) consume no fuel during R4 jobs, and bench working-animations don't play.

**How:** In the work toil's tick action, add:
```csharp
if (Bench is Building_WorkTable wt) wt.UsedThisTick();
```

**Applies to:** `JobDriver_R4Repair` and `JobDriver_R4Clean`.

---

## 7. `CheckForJobOverride` missing

**Why:** Vanilla's work toil periodically calls `pawn.jobs.CheckForJobOverride()` so higher-priority jobs (fire-fighting, medical emergencies) can interrupt a long work cycle. Without this, a pawn working a long repair cycle won't respond to emergencies until the cycle finishes.

**How:** In the work toil's tick action, add a periodic check:
```csharp
if (Find.TickManager.TicksGame % 1000 == 0)
    pawn.jobs.CheckForJobOverride();
```

**Applies to:** `JobDriver_R4Repair` and `JobDriver_R4Clean`.

---

## 8. `gotoItem` has no fail conditions

**Why:** If the work item (the item being repaired/cleaned) is destroyed or forbidden while the pawn is walking toward it, the pawn walks the full distance and only then discovers the item is gone. This wastes time and causes an `Incompletable` end rather than a clean early exit.

**How:** Add fail conditions to the `gotoItem` toil:
```csharp
gotoItem.AddFailCondition(() => {
    Thing item = RepairItem; // or CleanItem
    return item == null || item.Destroyed || item.IsForbidden(pawn);
});
```

**Applies to:** `JobDriver_R4Repair` and `JobDriver_R4Clean`.

---

## 9. `HaulStuffOffBillGiverJob` not called in WorkGivers

**Why:** If a prior job was interrupted mid-way (e.g. pawn drafted, power cut), ingredients may remain staged on the bench's `IngredientStackCells`. The next job then tries to stage its own ingredients on already-occupied cells. Vanilla `WorkGiver_DoBill.JobOnThing` calls `WorkGiverUtility.HaulStuffOffBillGiverJob` to detect and clear stale bench contents before issuing a new job.

**How:** In `JobOnThing`, after finding a valid bench, add:
```csharp
if (bench is IBillGiver bg) {
    Job haulOff = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, bg, null);
    if (haulOff != null) return haulOff;
}
```

**Applies to:** `WorkGiver_R4Repair.JobOnThing` and `WorkGiver_R4Clean.JobOnThing`.

---

## 10. `RecordsUtility.Notify_BillDone` missing

**Why:** Vanilla calls `RecordsUtility.Notify_BillDone(pawn, products)` after each completed recipe to update crafting statistics and trigger tale recording. R4 skips this, so pawn crafting records are never updated.

**How:** In `finishToil.initAction`, after applying the result, add:
```csharp
var completionList = new List<Thing> { item };
if (IsBillDriven)
    job.bill.Notify_IterationCompleted(pawn, completionList);
RecordsUtility.Notify_BillDone(pawn, completionList);
```

**Applies to:** `JobDriver_R4Repair` and `JobDriver_R4Clean`.

---

## 11. Double `TryFindIngredients` call in WorkGivers

**Why:** `HasJobOnThing` and `JobOnThing` are called back-to-back by the scanner for the same `(pawn, item)` pair in the same tick. Both call `TryFindIngredients`, running the full ingredient search twice.

**How:** Add a per-instance cache keyed on `(pawn, item, tick)` on the WorkGiver. If the same pair is queried again in the same tick, return the cached result.

**Applies to:** `WorkGiver_R4Repair` and `WorkGiver_R4Clean`.

