# R⁴ Integration Notes & Crash Postmortem

Findings from analyzing working mods and the confirmed crash log. Covers the root cause of the startup crash, correct RimWorld API patterns, and architectural decisions for R4's implementation.

---

## 1. The Startup Crash — Confirmed Root Cause

The Player.log confirms the exact failure chain:

```
System.NullReferenceException at Verse.ThingCategoryNodeDatabase.FinalizeInit()
Caught exception while loading play data... Resetting mods config and trying again.
```

This is followed immediately by hundreds of errors like:
```
Could not load Texture2D at 'Terrain/Surfaces/...' in any active mod or in base resources.
System.InvalidOperationException: Sequence contains no elements at GenStuff.DefaultStuffFor
```

The textures are entirely vanilla — they are not the problem. The `NullReferenceException` in `ThingCategoryNodeDatabase.FinalizeInit()` corrupts the Def database during loading. After that:
- `ResolveIcon()` runs on broken defs
- `graphicData` may not initialize properly
- `GenStuff.DefaultStuffFor` can't find stuffCategories on mangled BuildableDefs
- RimWorld reports missing textures for everything

**The root cause is the comp-injection XML patch**. The previous version used `PatchOperationSequence` / `PatchOperationConditional` to check for and inject `CompProperties_Recyclable`. Many vanilla ThingDefs have no `<comps>` node. When the inner XPath resolves to zero nodes inside a Sequence, the patch system can leave defs in a half-applied state, which is enough to corrupt `ThingCategoryNodeDatabase` on finalization.

The fix is to replace the entire complex chain with two flat, deterministic operations (see DESIGN.md, XML Integration section).

---

## 2. Two Distinct UI Surfaces for Designations

There are two separate mechanisms for designations, and both are needed:

**Map-order designators** (the architect menu "Orders" tab):
- Declared via `PatchOperationAdd` on `DesignationCategoryDef[defName="Orders"]/specialDesignatorClasses`
- Appear as an icon in the bottom-left Orders menu
- Player drags across the map to designate multiple items at once

**Item gizmos** (the button that appears when you select a specific item):
- Injected via a `ReverseDesignatorDatabase.InitDesignators` postfix
- `__instance.AllDesignators.Add(new Designator_RecycleThing())` inside the postfix
- This is how the same `Designator` class appears as a button on selected items

Both mechanisms are needed. The design already had the Harmony postfix correct. The designator constructors call `ContentFinder<Texture2D>.Get(...)` — these must only fire from `InitDesignators` (which runs after textures are loaded), not from static constructors or XML patch-time code.

---

## 3. Workbench Discovery: Recipe Scanning at Startup

The correct approach is to build bench lists at startup by scanning `DefDatabase<ThingDef>.AllDefs` and checking `thingDef.AllRecipes` for the presence of known recipe defNames (`SmeltWeapon`, `SmeltApparel`, `Make_Apparel_BasicShirt`, etc.). This is called from a `[StaticConstructorOnStartup]` static constructor, so Defs are available.

Key detail: `RuntimeHelpers.RunClassConstructor(typeof(R4ThingDefCache).TypeHandle)` should be called explicitly from the main startup class to guarantee the cache's static constructor runs at the right time. Without this, the static constructor might fire lazily at first use, which could be in the wrong context.

For R4, `R4ThingDefCache` should build bench lists and repairable-def sets in a static constructor, triggered explicitly from `Setup.cs`.

---

## 4. Closest Reachable Workbench: The Right API

The correct API for finding the nearest usable bench is `GenClosest.ClosestThingReachable`:

```csharp
GenClosest.ClosestThingReachable(
    thing.Position, thing.Map,
    ThingRequest.ForGroup(ThingRequestGroup.Undefined),
    PathEndMode.InteractionCell,
    traverseParms,
    9999f,
    validator,
    candidateList);
```

`ThingRequest.ForGroup(ThingRequestGroup.Undefined)` with an explicit candidate list is the correct form when you have your own pre-filtered list of benches. The validator lambda checks `pawn.CanReserve(bench)` and `((IBillGiver)bench).UsableForBillsAfterFueling()`.

`TraverseParms` must be constructed manually with `mode = TraverseMode.ByPawn`, `maxDanger = Danger.Unspecified`. Do not use the default struct value.

---

## 5. JobDriver Structure: Haul → Bench → Work → Produce

The correct toil sequence for a recycle job (haul item to bench, work, produce materials) is:

1. `Toils_Reserve.Reserve(ThingToDestroyInd)`
2. `Toils_Goto.GotoThing(ThingToDestroyInd, PathEndMode.ClosestTouch)` with `FailOnSomeonePhysicallyInteracting`
3. `Toils_Haul.StartCarryThing(ThingToDestroyInd)`
4. `Toils_Goto.GotoThing(WorkBenchInd, PathEndMode.InteractionCell)`
5. `Toils_JobTransforms.SetTargetToIngredientPlaceCell(WorkBenchInd, ThingInd, CellInd)` — places the item on the bench surface
6. `Toils_Haul.PlaceHauledThingInCell(WorkBenchInd, null, false)`
7. Re-reserve the item (it's now at the bench)
8. Custom work toil with `tickIntervalAction` and `ToilCompleteMode.Never`
9. Completion toil: spawn products, destroy item, remove designation

`FailOnThingMissingDesignation(ThingToDestroyInd, Designation)` should be added as a setup failure condition on the first toil. This aborts the job cleanly if the designation is removed (e.g. player cancels) while the pawn is en route.

The work toil should use `tickIntervalAction` (which receives a delta tick count) rather than `tickAction`. This is the correct form for work toils in 1.6.

**`TryMakePreToilReservations`** must reserve both the bench (`WorkBenchInd`) and the item (`ThingToDestroyInd`). Both reservations are required, or another pawn can steal the bench between job creation and arrival.

---

## 6. Work Speed Calculation in the Work Toil

Check `Target.def?.recipeMaker?.workSpeedStat` and `Workbench.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor)` to scale work speed. If either is null/zero, fall back to `statValue = 1f`. Never assume `recipeMaker` is non-null — many modded items don't have it.

The bill-based pattern (`StatExtension.GetStatValue(pawn, StatDefOf.WorkToMake, true)`) is an alternative but is designed for the bill/recipe pipeline. For a custom JobDriver not derived from `JobDriver_DoBill`, multiplying (pawn stat × bench factor × delta) in `tickIntervalAction` is the cleaner approach.

---

## 7. Material Recovery: The Canonical Pattern

The canonical source of material costs is `thing.CostListAdjusted(thing.Stuff)`. Additionally, `def.smeltProducts` provides extra outputs for items that have them (e.g. guns that yield steel on smelting). The pattern:

```
for each entry in CostListAdjusted:
    skip if intricate AND settings.skipComponents is true
    count = GenMath.RoundRandom(entry.count × returnPercent × efficiency)
    if count > 0: spawn Thing with stackCount = count

for each entry in def.smeltProducts:
    spawn Thing with stackCount = entry.count
```

`GenMath.RoundRandom` is the correct probabilistic rounding function — it randomly rounds up or down such that the expected value equals the float input. This avoids always rounding down, which would bias against the player on small counts.

Spawned things should be placed at `pawn.Position` via `GenSpawn.Spawn` or `GenPlace.TryPlaceThing` in the completion toil — not on the bench surface, since the pawn has already moved away.

---

## 8. Repair: Two Architectural Patterns

**Pattern A — Bench-centric radius scan:**
- A dedicated building has a configurable radius and item/ingredient filters (via a ThingComp)
- `WorkGiver` scans buildings for the comp, then uses `GenRadial.RadialDistinctThingsAround` to find damageable items within radius
- Ingredients are gathered as a queue (`job.targetQueueB`, `job.countQueue`) using `WorkGiver_DoBill.TryFindBestFixedIngredients`
- The `JobDriver` collects ingredient toils then does a `ToilCompleteMode.Delay` work toil
- Repair is instantaneous at the end: `item.HitPoints = item.MaxHitPoints`

**Pattern B — Bill-based, cycle-per-tick:**
- Uses a dedicated `Building_WorkTable` with `ITab_Bills`
- Item is placed on the bench as a bill ingredient
- A custom `RecipeWorker` subclass redirects the bill job to a custom `JobDriver`
- The job driver ticks through work cycles, adding HP each cycle, with failure chance per cycle
- If HP reaches 0 during repair, falls back to recycle output

For R4, Pattern A (designation + WorkGiver) is closer to our design for M1/M2. The cycle-based repair from Pattern B is the right model for the skill/failure mechanics, adapted for our designation flow rather than the bill flow.

---

## 9. Taint Clearing

`Apparel.WornByCorpse` has a **public setter in RimWorld 1.6**. No reflection required — set it directly: `apparel.WornByCorpse = false`. After setting it, call `apparel.Notify_ColorChanged()` to force the render update. The older pattern of accessing `wornByCorpseInt` via `BindingFlags.NonPublic` reflection is no longer necessary and should not be used.

---

## 10. The `Smeltable` Property

`ThingDef.Smeltable` is a vanilla property that returns true if the item has `smeltProducts` OR has at least one non-intricate item in its cost list with a non-zero 25% yield. This is the correct property to use as the primary routing branch in `WorkbenchRouter` — smeltable items go to the smelt bench, everything else to the scrap/tailor bench.

---

## 11. `ExposeData` in JobDrivers

Save job progress in `ExposeData` on any `JobDriver` with mutable state:

```csharp
public override void ExposeData() {
    base.ExposeData();
    Scribe_Values.Look(ref workLeft, "workLeft");
    Scribe_Values.Look(ref totalNeededWork, "totalNeededWork");
}
```

Without this, save/load mid-job resets the work counter to zero. Always implement `ExposeData` on custom JobDrivers.

---

## 12. `WorkTypeDef` Priority Notes

If defining a custom `WorkTypeDef` for R4 (rather than using the existing `Crafting` type), set `naturalPriority` to slot appropriately among existing work types. It may also be necessary to patch `DoBillsSmelter` workType to the new type if the smelter work should roll into the same work column. For R4 using the existing `Crafting` work type, no priority patching is needed — all R4 `WorkGiverDef`s should set `workType` to `Crafting`.

---

## 13. What the Previous Crash Was (Confirmed)

The Player.log confirms the failure sequence precisely. The `ThingCategoryNodeDatabase.FinalizeInit()` NullReferenceException is the single root event. Everything else — the hundreds of texture errors, the `GenStuff.DefaultStuffFor` failures, the `MatFrom with null sourceTex` lines — are all downstream symptoms of that one crash corrupting the Def database.

The proximate cause was the comp-injection XML patch using `PatchOperationSequence` / `PatchOperationConditional` against defs that had no `<comps>` node. The fix — documented in DESIGN.md — is two flat unconditional `PatchOperationAdd` operations targeting `thingClass` instead of `thingCategories`, with an explicit "create node if missing" step first.
