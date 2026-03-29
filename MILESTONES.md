# R⁴ — Milestones

## M0–M3: Core Features ✅

Recycle, Repair, Clean all functional with designator-based workflow.

---

## Material Cost & Ingredient Gathering ✅

Vanilla-compatible ingredient queue pattern using `JobDriver_DoBill.CollectIngredientsToils()`.

---

## M4: Bill-Based Automation ✅

**Architecture:** RecipeDefs with custom `RecipeWorker` subclasses. Recycle and Clean use vanilla's `WorkGiver_DoBill` → `JobDriver_DoBill` pipeline. Repair uses custom `WorkGiver_R4RepairBill` → `JobDriver_R4Repair` with `CollectIngredientsToils`. The item to process is the bill ingredient.

**RecipeWorkers:**
- `RecipeWorker_R4Recycle` — defers item destruction, spawns recycled materials (skill-based) in `Notify_IterationCompleted`
- `RecipeWorker_R4Repair` — runs one repair cycle per bill iteration. Consumes repair materials from map stacks. Minor mending (≥95% HP) is free. Item stays on bench; if still damaged, the bill picks it up again.
- `RecipeWorker_R4Clean` — removes taint, item stays on bench

**RecipeDefs (per-bench):**
- CraftingSpot → Repair, Recycle, Clean
- Tailor benches → Repair, Recycle, Clean
- Smithy benches → Repair, Recycle
- Machining table → Repair, Recycle
- Fabrication bench → Repair, Recycle

**Vanilla smelting override:** SmeltWeapon/SmeltApparel/SmeltOrDestroyThing removed from smelter via XML patches. Stale bills cleaned up by `Patch_BuildingWorkTable_SpawnSetup` Harmony postfix.

**Filter system:** `R4WorkbenchFilterCache` stamps precise per-bench ThingFilters onto each RecipeDef at startup by inverting `recipeMaker.recipeUsers`. XML filters are placeholders only.

**Repair bill design:** The damaged item is the bill ingredient. `WorkGiver_R4RepairBill` handles candidate finding (region traversal), material gathering, and custom job creation. The RecipeWorker handles material consumption from map stacks (not bill ingredients), skill checks, failure mechanics, and destruction. Bill quality/HP filters let players control which items get repaired.

**Minor mending in bills:** Items at ≥95% HP are repaired without material consumption, same threshold as designation-based repair.

---

## M5: Polish & Publishing

- [ ] Replace placeholder textures with proper icons
- [ ] Full settings UI, comprehensive translation keys
- [ ] Remove all debug logging (Setup.cs, R4WorkbenchFilterCache)
- [ ] README.md, Steam Workshop assets
- [ ] Compatibility testing (CE, VE, etc.)
- [ ] Performance profiling

### Code Review Findings (Pre-Release)

**Bug fixed:**
- `RecipeWorker_R4Repair` hardcoded `0.20f` HP per cycle instead of using `RRRR_Mod.Settings.repairHpPerCycle`. Bill-based repair now respects the setting.

**Install/Uninstall safety: PASS**
- No custom ThingComps on persistent objects, no MapComponents, no WorldComponents
- All mod data (designations, jobs, bills) uses mod-defined Defs → gracefully handled on removal
- Vanilla smelting recipes restore when patch stops applying
- `Patch_BuildingWorkTable_SpawnSetup` cleans stale bills on install
- Only concern: players must re-add SmeltWeapon/SmeltApparel bills manually after uninstall

**Remaining items for release:**
- Debug logging: ~30 `Log.Message` calls in `Setup.cs` and `R4WorkbenchFilterCache` should be removed or gated behind a debug flag
- `RRRR_Clean_Smithing` WorkGiver is registered but tainted apparel never routes to smithies (harmless, low priority)
- `ConsumeIngredientsOnBench` destroys all items on bench ingredient cells indiscriminately — functional because the bench is reserved, but overly broad

---

## Lessons Learned

**SpecialThingFilterDefs are global:** `allowedByDefault=false` with a broad `parentCategory`
blocks items from ALL ThingFilters in the game. Never use `parentCategory=Root` with
`allowedByDefault=false` unless you intend to affect every bill, stockpile, and filter.

**Vanilla bill pipeline works:** Use RecipeDefs with custom `RecipeWorker` subclasses.
Override `ConsumeIngredient` to prevent destruction, `Notify_IterationCompleted` for custom
logic (has pawn reference for skill). Vanilla's `WorkGiver_DoBill` handles ingredient
finding and job creation automatically.

**PatchOperation success values:** Valid values are `Normal`, `Invert`, `Always`, `Never`.
NOT `Maybe`. Use `Always` for "don't fail if target not found".

**Repair in vanilla bill system:** The item is the ingredient. Additional repair materials
are consumed from map stacks in `Notify_IterationCompleted`, not as bill ingredients.
One cycle per bill iteration. If still damaged, the bill picks it up again automatically.

**Repair needs custom WorkGiver:** Vanilla's `WorkGiver_DoBill` can't haul both the item and
repair materials. `WorkGiver_R4RepairBill` creates jobs with both `targetQueueA` (item) and
`targetQueueB` (materials), using `JobDriver_DoBill.CollectIngredientsToils` for ingredient
gathering.

**requiredGiverWorkType blocks vanilla WorkGiver_DoBill:** Setting `requiredGiverWorkType=Crafting`
on repair recipes prevents vanilla's Smithing/Tailoring `WorkGiver_DoBill` from picking them up,
while our custom `WorkGiver_R4RepairBill` (which doesn't check `requiredGiverWorkType`) works fine.

**Comp injection is fragile — avoid it:** PatchOperationSequence/Conditional for comp injection
can corrupt the Def database if defs lack `<comps>` nodes. The cleaner solution: use
DesignationDefs + Harmony GetGizmos postfix instead of ThingComps.

**Per-bench recipe filter stamping:** XML category filters are too broad. Invert
`recipeMaker.recipeUsers` at startup to build precise per-bench filters, then stamp them
onto RecipeDef.fixedIngredientFilter in code. This ensures each bench's bills only show
items that bench could have crafted.
