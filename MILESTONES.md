# R⁴ — Milestones

## M0–M3: Core Features ✅

Recycle, Repair, Clean all functional with designator-based workflow.

---

## Material Cost & Ingredient Gathering ✅

Vanilla-compatible ingredient queue pattern using `JobDriver_DoBill.CollectIngredientsToils()`.

---

## M4: Bill-Based Automation ✅

**Architecture:** RecipeDefs with custom `RecipeWorker` subclasses, processed by vanilla's
`WorkGiver_DoBill` → `JobDriver_DoBill` pipeline. The item to process is the bill ingredient.

**RecipeWorkers:**
- `RecipeWorker_R4Recycle` — spawns recycled materials (skill-based), destroys item
- `RecipeWorker_R4Repair` — runs one repair cycle per bill iteration. Consumes repair
  materials from map stacks. Minor mending (≥95% HP) is free. Item stays on bench;
  if still damaged, the bill picks it up again.
- `RecipeWorker_R4Clean` — removes taint, item stays on bench

**RecipeDefs:**
- `RRRR_RecycleWeapon` — smelter + smithy + machining
- `RRRR_RecycleApparel` — smelter + tailor benches
- `RRRR_RepairWeapon` — smithy + machining
- `RRRR_RepairApparel` — tailor benches
- `RRRR_CleanApparel` — tailor benches

**Vanilla smelting override:** SmeltWeapon/SmeltApparel removed from smelter via XML patches.

**Repair bill design:** The damaged item is the bill ingredient. The RecipeWorker handles
material consumption from map stacks (not bill ingredients), skill checks, failure
mechanics, and destruction. The bill's quality/HP filters let players control which
items get repaired (e.g. "only repair items above Normal quality").

**Minor mending in bills:** Items at ≥95% HP are repaired without material consumption,
same threshold as designation-based repair.

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
