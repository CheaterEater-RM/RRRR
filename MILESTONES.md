# R⁴ — Milestones

## M0: Skeleton & XML Stability ✅

**Goal:** Prove the mod loads without crashing. No gameplay.

---

## M1: Recycle — Full Loop ✅

**Goal:** Player designates item → pawn hauls to bench → works → materials spawn → item destroyed.

---

## M2: Repair ✅

**Goal:** Multi-cycle repair with skill checks, failure mechanics, material consumption.

---

## M3: Clean (Taint Removal) ✅

**Goal:** Remove corpse taint from apparel at a workbench, consuming materials.

---

## Material Cost System (cross-cutting, added after M3)

**Goal:** Repair and cleaning consume materials from the map. Prevent clean→recycle from being more profitable than direct recycling.

**Completed:**
- [x] `MaterialUtility.GetRepairCycleCost(item)` — 10% of base materials per cycle × 1.2 × techDifficulty. Components at 50% reduced rate, probabilistically rounded.
- [x] `MaterialUtility.GetCleanCost(item)` — 20% of base materials (non-intricate only). Minimum 1 of primary material.
- [x] `MaterialUtility.HasRepairMaterials()` / `ConsumeRepairMaterials()` — check and consume materials from map, sorted by distance
- [x] `JobDriver_R4Repair` — consumes materials at end of each cycle. Aborts with message if materials unavailable.
- [x] `JobDriver_R4Clean` — consumes materials upfront before starting work. Aborts if unavailable.
- [x] Translation keys for material shortage messages

**Designation coexistence fix:**
- [x] Repair + Clean can coexist on the same item (they address different problems)
- [x] Repair only removes R4_Repair designation on completion (was using `RemoveAllDesignationsOn` which wiped R4_Clean)
- [x] Clean only removes R4_Clean designation on completion
- [x] Recycle still conflicts with both repair and clean (checked in all three designators)

**Economics verification:**
- Tainted recycle: returns ~25% of materials (50% base × 50% taint penalty), no cost
- Clean then recycle: costs 20% of materials, then returns ~50% (no taint penalty). Net = 30% return. Slightly better than tainted recycle but requires materials upfront and two jobs — fair trade.
- Repair: costs ~12% of materials per cycle (10% × 1.2), restores 10% HP per success. Over a full 0→100% repair, costs ~120% of base materials (more than crafting a new one at low tech, less at high tech due to the 1.2× multiplier being offset by higher tech difficulty). Encourages repairing lightly damaged items rather than rebuilding from scratch.

**Validation:**
- [ ] Repair consumes materials each cycle — check logs / material stacks
- [ ] Repair aborts with message when materials unavailable
- [ ] Clean consumes materials upfront
- [ ] Clean aborts with message when materials unavailable
- [ ] Repair + clean dual designation works: repair finishes, clean designation survives
- [ ] Clean→recycle pipeline returns less net material than clean cost

---

## M4: Bill-Based Automation

**Goal:** Standing bills at workbenches with filters for automated batch processing.

- [ ] `SpecialThingFilterDef` entries (**all must have `<parentCategory>`!**)
- [ ] `RecipeDef` entries for bill-based recycle/repair/clean
- [ ] `SpecialThingFilterWorker` and `RecipeWorker` subclasses

---

## M5: Polish & Publishing

- [ ] Replace placeholder textures with proper icons
- [ ] Full settings UI, comprehensive translation keys
- [ ] Remove all debug logging
- [ ] README.md, Steam Workshop assets
- [ ] Compatibility testing (CE, VE, etc.)
- [ ] Performance profiling

---

## Lessons Learned

**XML safety:**
1. Never use `PatchOperationSequence`/`PatchOperationConditional` for comp injection
2. Every `SpecialThingFilterDef` **must** have `<parentCategory>`
3. Target `thingClass` (stable) not `thingCategories` (fragile)

**Work speed:** 15% of WorkToMake (clamped). `GeneralLaborSpeed` not `WorkSpeedGlobal`.

**Quality:** `CompQuality.SetQuality()` is public — no reflection needed.

**Workbench routing:** `def.recipeMaker?.recipeUsers` for per-item routing. Fallback to smeltable scan.

**Repair designation:** Only remove the specific R4_Repair designation on completion — not `RemoveAllDesignationsOn` which wipes coexisting designations like R4_Clean.

**Material consumption:** Repair consumes per-cycle, clean consumes upfront. Both abort gracefully with a player message if materials are unavailable. Materials are found via `map.listerThings.ThingsOfDef()` sorted by distance.

**HP calculations in failure handlers:** Use `cyclesCompleted / totalCyclesNeeded` for progress. Capture labels before destruction. Guard division by MaxHitPoints.
