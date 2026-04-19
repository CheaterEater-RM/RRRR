# R4 Workbench Integration — Manual Test Plan

## Overview

RimWorld has no automated test framework. All testing is manual, in-game, using dev mode. This document defines a structured test plan for the workbench integration fixes. Each test has a setup, action, expected result, and what to check in `Player.log`.

**Log location:** `%APPDATA%\..\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`

**Dev mode tools needed:**
- `God Mode` (instant build, no material cost for spawning)
- `Spawn thing` dialog
- `Set hitpoints` (debug action on selected thing)
- `Taint` (debug action on apparel — may need to spawn a corpse and strip it)
- `Draft/Undraft` pawn
- `Destroy` tool
- `Forbid` tool
- Game speed controls (1×, 2×, 3×)

---

## Test Environment Setup

### Minimal test colony
1. New colony, dev mode enabled, any biome
2. Spawn 3 colonists with varying Crafting skill (0, 10, 20)
3. Build powered workbenches: electric smithy, electric tailor bench, machining table, fabrication bench
4. Build one **crafting spot** (unpowered 1×1 bench — critical edge case)
5. Build one **fueled smithy** (fuel-burning bench — tests `UsedThisTick`)
6. Create stockpile with: steel ×200, cloth ×200, leather ×200, components ×10, wood ×200
7. Save as `R4_TEST_BASE`

### Item spawn list
For each test, spawn items via dev mode. Key items:

| Item | Bench route | Materials | Notes |
|---|---|---|---|
| Steel longsword | Smithy | 100 steel | Basic weapon, straightforward |
| Revolver | Machining table | 30 steel, 4 components | Multi-material, components are "rare" |
| Cloth pants | Tailor bench | 40 cloth | Basic apparel |
| Flak vest | Smithy | 60 cloth, 5 steel | Multi-material apparel |
| Club | Crafting spot | 40 wood | Neolithic, routes to crafting spot |
| Pila | Crafting spot | 25 wood | Neolithic ranged weapon |
| Marine helmet | Fabrication | 40 plasteel, 1 component | Spacer tech |

### Modded item preparation (if mods installed)

| Mod | Item | Expected behaviour | Why it's interesting |
|---|---|---|---|
| Gas Masks | Gas mask | Should route to appropriate bench | May have unusual `recipeMaker` or `thingCategories` |
| Combat Shields | Combat shield | Should route to smithy/machining | May use non-standard `IsWeapon`/`IsApparel` categorisation |

**To prepare modded items:** Load the game with both mods enabled. Use dev mode `Spawn thing` to search for the items. Note: if items don't have `recipeMaker.recipeUsers`, they'll use the techLevel fallback routing.

---

## Test Categories

### Category A: Ingredient Tracking & Consumption

These tests verify the core fix — ingredients are tracked via `job.placedThings` and consumed correctly.

#### A1: Basic repair consumes correct materials

**Setup:** Spawn steel longsword, set HP to 50%. Place near smithy. Have 200 steel in stockpile.  
**Action:** Designate for repair (right-click gizmo or Orders designator). Let pawn complete one cycle.  
**Expected:**
- Pawn picks up steel, carries to bench, places on bench cells
- Pawn picks up longsword, carries to bench, places on bench cells  
- Pawn works at bench (progress bar visible)
- After work completes: steel consumed from bench cells (exact amount = `Ceiling(100 / RepairCostDivisor)` = 10 at default settings)
- Longsword HP increased by 20% of max
- Steel count in stockpile decreased by the amount consumed

**Log check:** No `[RRRR]` warnings. No exceptions.

**Failure modes to watch for:**
- Steel still on bench after work → consumption failed
- Steel consumed but HP unchanged → `ApplyWorkResult` didn't fire
- More steel consumed than expected → consumption took from stockpile, not placed things

#### A2: Repair with multiple material types

**Setup:** Spawn flak vest, set HP to 50%.  
**Action:** Designate for repair.  
**Expected:** Both cloth AND steel are gathered, placed on bench, consumed proportionally per cycle cost.  
**Check:** Both material types consumed; amounts match formula.

#### A3: Minor mending — no materials consumed

**Setup:** Spawn steel longsword, set HP to 96% (above minor mending threshold of 95%).  
**Action:** Designate for repair.  
**Expected:**
- Pawn goes directly to bench (no ingredient gathering phase)
- Work completes
- No materials consumed
- HP restored to 100%

**Check:** `job.targetQueueB` should be empty (no ingredients to gather).

#### A4: Clean consumes correct materials

**Setup:** Spawn tainted cloth pants. Have 200 cloth in stockpile.  
**Action:** Designate for clean.  
**Expected:**
- Cloth consumed = `Ceiling(40 / CleanCostDivisor)` = 8 at default settings
- Pants no longer tainted after work
- R4_Clean designation removed

#### A5: Crafting spot (1×1 bench) ingredient placement

**Setup:** Spawn club (wooden), set HP to 50%. Have 200 wood near crafting spot.  
**Action:** Designate for repair at crafting spot.  
**Expected:**
- Wood is placed on/near crafting spot cells
- Wood is consumed after work completes
- This is the critical edge case: 1×1 benches have limited `IngredientStackCells`, so ingredients may land on adjacent cells via the radial fallback. The `PlaceHauledThingInCell` patch tracks via `job.placedThings` so this should work regardless of exact placement location.

**What to watch:** Ingredients NOT consumed → the placed thing reference may not match what's in `job.placedThings` (stacking issue on 1×1 bench).

#### A6: Ingredient stacking on bench

**Setup:** Spawn two damaged steel longswords. Repair the first one. Before repairing the second, leave leftover steel from the first repair on the bench.  
**Action:** Designate second longsword for repair.  
**Expected:**
- `HaulStuffOffBillGiverJob` should trigger first, clearing stale bench contents
- Then the normal repair flow begins
- OR: new steel stacks with existing steel on bench. `job.placedThings` should still track the placed amount correctly.

#### A7: Ingredient forbidden mid-work

**Setup:** Start a repair job. While pawn is in the work phase, forbid one of the placed ingredients on the bench.  
**Action:** Watch pawn.  
**Expected:** Job fails cleanly due to `FailOnDespawnedNullOrForbiddenPlacedThings`.  
**Check:** Pawn gets new job. No errors in log.

#### A8: Ingredient destroyed mid-work

**Setup:** Start a repair job. While pawn is in the work phase, use dev `Destroy` tool on a placed ingredient on the bench.  
**Action:** Watch pawn.  
**Expected:** Job fails cleanly.

---

### Category B: Work Timing & Game Speed

#### B1: Work duration invariant across game speeds

**Setup:** Spawn two identical steel longswords at 50% HP. Same pawn, same bench.  
**Action:**
1. Repair first at 1× speed. Time with stopwatch (wall-clock seconds to complete work phase).
2. Reload save. Repair second at 3× speed. Time with stopwatch.  
**Expected:** Wall-clock time should be approximately the same (within 10%). Before the fix, 3× speed made repairs ~3× faster in wall-clock time.

#### B2: XP gain rate invariant across game speeds

**Setup:** Note pawn's Crafting XP before repair.  
**Action:** Complete one repair cycle at 1× speed. Note XP gained. Reload, repeat at 3× speed.  
**Expected:** Same XP gained (within rounding).

---

### Category C: Bench Safety Guards

#### C1: Bench loses power mid-work

**Setup:** Start repair at powered electric smithy.  
**Action:** Uninstall the power conduit or toggle power off mid-work.  
**Expected:** Pawn stops working immediately. Job ends cleanly. Item and ingredients stay where they are.

#### C2: Bench catches fire

**Setup:** Start repair at any bench.  
**Action:** Use dev mode to set bench on fire.  
**Expected:** Pawn stops working immediately (`FailOnBurningImmobile`).

#### C3: Bench destroyed mid-work

**Setup:** Start repair.  
**Action:** Dev mode `Destroy` the bench.  
**Expected:** Job ends with `Incompletable`. No NRE in log.

#### C4: Two pawns attempt same bench

**Setup:** Two pawns with Crafting enabled. Both have repair designations pending.  
**Action:** Watch scheduling.  
**Expected:** Only one pawn works the bench at a time. The second pawn either waits or finds another bench. This is ensured by bench reservation + interaction cell reservation.

#### C5: Fueled bench consumes fuel

**Setup:** Build fueled smithy. Set fuel low (5 units). Start repair.  
**Action:** Watch fuel gauge.  
**Expected:** Fuel decreases during work (via `UsedThisTick`). If fuel runs out, `CurrentlyUsableForBills()` returns false and job ends.

#### C6: Bench animation plays

**Setup:** Start repair at any bench with a working mote emitter (smithy sparks, tailor bench animation).  
**Action:** Observe bench during work.  
**Expected:** Animation plays (via `UsedThisTick` → `CompMoteEmitter`).

---

### Category D: Work Item Validity

#### D1: Work item destroyed during goto

**Setup:** Pawn is walking to pick up the item (Phase 2, `gotoItem` toil).  
**Action:** Dev mode `Destroy` the item while pawn is en route.  
**Expected:** Job ends cleanly (fail condition on `gotoItem`). No NRE.

#### D2: Work item forbidden during goto

**Setup:** Same as D1.  
**Action:** Forbid the item.  
**Expected:** Job ends cleanly.

#### D3: Repair designation removed during work

**Setup:** Start repair (designation flow, not bill-driven).  
**Action:** Remove the R4_Repair designation manually while pawn is working.  
**Expected:** Job ends via the `FailOn` delegate that checks designation presence.

#### D4: Clean item no longer tainted mid-work

This shouldn't happen in practice (taint isn't removed until our finish toil), but another mod could theoretically clear it.

**Setup:** Start clean job.  
**Action:** If possible, use dev actions to remove taint mid-work (may not be feasible without console commands).  
**Expected:** Job fails via `IsWorkItemStillValid` check.

---

### Category E: Repair Multi-Cycle & Persistence

#### E1: Multiple repair cycles needed

**Setup:** Spawn longsword at 10% HP. At default settings (20% HP/cycle), this needs 5 cycles to full.  
**Action:** Let pawn work.  
**Expected:**
- First cycle: pawn completes, HP increases by ~20%
- Designation persists (HP < max)
- Pawn picks up the job again for cycle 2
- Materials consumed each cycle
- After 5 cycles: item at 100% HP, designation removed

#### E2: Repair failure and persistence

**Setup:** Spawn longsword at 30% HP. Use a skill-0 pawn.  
**Action:** Let pawn attempt repair. With low skill, failures are likely.  
**Expected:**
- On failure: message appears, HP decreases slightly
- Designation persists (HP still < max)
- Pawn tries again
- On critical failure (below 50% HP): quality degrades, HP drops 15%
- If item reaches 0 HP: destroyed, partial reclaim materials spawned, message shown

#### E3: Interrupted repair resumes correctly

**Setup:** Start repair. Draft pawn mid-cycle (during work phase).  
**Action:** Undraft pawn.  
**Expected:**
- Pawn picks up repair job again
- Work progress is saved via `ExposeData` (`cycleWorkLeft`)
- Materials from the interrupted cycle are on the bench — `HaulStuffOffBillGiverJob` should clear them before the new job starts

---

### Category F: Bill-Driven Path

#### F1: Bill-driven repair

**Setup:** Set up a repair bill on the smithy ("repair item", count=1). Spawn damaged steel longsword within search radius.  
**Action:** Let the bill WorkGiver pick up the job.  
**Expected:** Same behaviour as designation-driven. `Notify_IterationCompleted` called. Bill count decremented.

#### F2: Bill-driven clean

**Setup:** Set up a clean bill on the tailor bench. Spawn tainted cloth pants.  
**Action:** Let the bill WorkGiver pick up the job.  
**Expected:** Taint removed. Bill count decremented.

#### F3: Bill with suspended state

**Setup:** Start bill-driven repair. Suspend the bill mid-work.  
**Action:** Watch pawn.  
**Expected:** Job ends cleanly via `FailOn` delegate checking `bill.suspended`.

---

### Category G: WorkGiver Correctness

#### G1: HaulStuffOffBillGiverJob clears stale ingredients

**Setup:** Start a repair, draft pawn mid-ingredient-gathering (after steel is on bench but before work starts). Undraft pawn.  
**Action:** Watch what happens.  
**Expected:** Before starting a new repair job, pawn hauls the stale steel off the bench first (via `HaulStuffOffBillGiverJob`).

#### G2: Designation WorkGiver routes to correct bench type

**Setup:** Spawn damaged longsword (routes to smithy) and damaged cloth pants (routes to tailor). Have both benches.  
**Action:** Designate both for repair.  
**Expected:** Longsword goes to smithy, pants go to tailor bench.

#### G3: No bench available

**Setup:** Spawn damaged revolver (routes to machining table). Don't have a machining table.  
**Action:** Designate for repair.  
**Expected:** No job created. Designation stays on item. No errors.

---

### Category H: Modded Item Testing

These tests require the Gas Masks and Combat Shields mods to be enabled.

#### H1: Gas mask repair

**Setup:** Spawn gas mask via dev mode. Damage it to 50%.  
**Investigate first:**
- Check `gas_mask_def.recipeMaker?.recipeUsers` — does it list a bench?
- Check `gas_mask_def.techLevel` — what's the fallback bench?
- Check `gas_mask_def.costList` / `CostListAdjusted` — what materials does it need?

**Action:** Designate for repair.  
**Expected:**
- Item routes to appropriate bench
- Materials consumed match the gas mask's cost list
- Repair succeeds normally

**What could go wrong:**
- Gas mask might not have `recipeMaker.recipeUsers` → uses techLevel fallback → may route to unexpected bench
- Gas mask might have unusual material types (filters, chemicals?) → `GetRepairCycleCost` may not find matching materials
- Gas mask might use non-standard `thingCategories` → R4 eligibility check may not recognise it

#### H2: Combat shield repair

**Setup:** Spawn combat shield via dev mode. Damage it to 50%.  
**Investigate first:**
- Check `combat_shield_def.IsWeapon` vs `combat_shield_def.IsApparel` — which is it?
- Check `combat_shield_def.useHitPoints` — does it use hitpoints?
- Check `combat_shield_def.recipeMaker` — does it have one?

**Action:** Designate for repair.  
**Expected:** Item routes to appropriate bench, materials consumed, repair succeeds.

**What could go wrong:**
- Shield might not be `IsWeapon` or `IsApparel` → R4 eligibility check rejects it
- Shield might be in a custom `thingCategory` that doesn't descend from Weapons or Apparel → bill filter doesn't include it
- Shield might have unusual stuff type (e.g., plasteel, not standard stuff) → ingredient search finds nothing

#### H3: Gas mask / shield clean

**Setup:** If either item can be tainted (must be apparel), create a tainted version.  
**Action:** Designate for clean.  
**Expected:** Same as vanilla clean flow.

**What could go wrong:** If item is categorised as weapon, not apparel → clean flow won't accept it (correct behaviour for weapons).

---

## Regression Tests

After all fixes are applied, also verify:

### R1: Recycle still works (unchanged code path)

Recycle uses a completely different job driver and should not be affected. Verify:
- Designate item for recycle
- Pawn hauls to bench, works, materials spawn, item destroyed
- No changes to recycle flow

### R2: Gizmos still appear

- Select damaged item → repair gizmo visible
- Select tainted apparel → clean gizmo visible
- Select any item → recycle gizmo visible

### R3: Orders designators still work

- Open Orders menu → repair, clean, recycle designators present
- Drag-select items → designations placed correctly

### R4: Save/load during repair

- Start repair, save mid-cycle
- Reload: `cycleWorkLeft` / `cycleWorkTotal` restored from `ExposeData`
- Repair continues from where it left off
- `job.placedThings` IS saved by vanilla (`Scribe_Collections.Look` with `LookMode.Undefined`; `ThingCountClass` implements `IExposable` with `Scribe_References.Look` for the thing reference). After reload, placed ingredient references should resolve correctly and consumption should work normally.

**Verify:** After save/load mid-cycle, ingredients are still consumed correctly on completion. Check `Player.log` for any warnings about null thing references in `placedThings`.

---

## Test Recording Template

For each test, record:

```
Test: [ID]
Date: [date]
Build: [commit hash or version]
Result: PASS / FAIL / PARTIAL
Notes: [what happened, any unexpected behaviour]
Log errors: [any errors/warnings from Player.log]
```

---

## Known Limitations

1. **No automated tests** — RimWorld has no test framework. All testing is manual in dev mode.
2. **Mod interaction** — Gas mask and combat shield mods may update and change their defs. Tests should re-check def properties before each test session.
3. **Multiplayer** — Not tested. R4 does not claim multiplayer compatibility.
