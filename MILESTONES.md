# R⁴ — Milestones

## M0: Skeleton & XML Stability ✅

**Goal:** Prove the mod loads without crashing. No gameplay.

---

## M1: Recycle — Full Loop ✅

**Goal:** Player designates item → pawn hauls to bench → works → materials spawn → item destroyed.

---

## M2: Repair ✅

**Goal:** Multi-cycle repair with skill checks, failure mechanics.

---

## M3: Clean (Taint Removal) ✅

**Goal:** Remove corpse taint from apparel at a workbench.

**Completed:**
- [x] `DesignationDef` for `R4_Clean` with placeholder texture
- [x] `JobDef` for `RRRR_Clean`
- [x] `WorkGiverDef` for clean (Crafting work type, priority 45)
- [x] `Designator_CleanThing` — targets tainted apparel only, blocks if designated for recycle, drag-select
- [x] `WorkGiver_R4Clean` — scans R4_Clean designations, finds bench via WorkbenchRouter
- [x] `JobDriver_R4Clean` — haul → bench → work → remove taint
  - Flat work: 15% of WorkToMake, clamped 300–1500
  - Always succeeds — no failure mechanics
  - Skill bonus: crafting skill speeds up work (skill 10 = 1.3x, skill 20 = 1.6x)
  - `Apparel.WornByCorpse = false` (public setter)
  - `Apparel.Notify_ColorChanged()` to update render
  - `ExposeData` for `workLeft`/`totalWork` (save mid-job)
  - Progress bar
- [x] `R4DefOf` updated with `R4_Clean` and `RRRR_Clean`
- [x] Orders menu, ReverseDesignatorDatabase updated with clean designator
- [x] Translation keys for clean UI
- [x] Setup.cs verifies clean defs at startup
- [x] `generate_textures.py` updated with all three textures

**Validation checklist:**
- [ ] Designate tainted apparel → pawn hauls to bench and works
- [ ] Verify taint removed after job completes (WornByCorpse = false)
- [ ] Verify apparel color updates (no longer corpse-tinted)
- [ ] Non-tainted apparel cannot be designated for cleaning
- [ ] Clean and recycle designators don't conflict
- [ ] Save/load mid-clean

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

**Weapons:** Vanilla weapons use `thingClass="ThingWithComps"` — can't XML-target. Designators check `def.IsWeapon` directly.

**Work speed:** 15% of WorkToMake (clamped) for recycling/cleaning. `GeneralLaborSpeed` not `WorkSpeedGlobal`.

**Quality:** `CompQuality.SetQuality()` is public — no reflection needed.

**Workbench routing:** `def.recipeMaker?.recipeUsers` is the per-item crafting bench list — use as primary routing. Fallback to smeltable/non-smeltable for items without recipeMaker.

**Repair designation persistence:** Only remove on full HP or destruction.

**HP calculations in failure handlers:** Use `cyclesCompleted / totalCyclesNeeded` for progress, not HP math. Capture labels before destruction. Guard division by MaxHitPoints.
