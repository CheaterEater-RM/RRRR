# R⁴ — Milestones

## M0–M3: Core Features ✅

Recycle, Repair, Clean all functional with designator-based workflow.

---

## Material Cost & Ingredient Gathering Rework ✅

**Problems fixed:**
1. Drag-select didn't work — designators lacked `DrawStyleCategory` override
2. Materials consumed remotely from map without pawns physically hauling them
3. Repair infinite loop when materials unavailable — pawn kept retrying with no materials
4. Revolver worked without materials because steel was consumed from elsewhere on map

**Solution: Vanilla-compatible ingredient queue pattern:**
- WorkGiver calculates cost, finds material stacks, populates `job.targetQueueB` + `job.countQueue`
- JobDriver Phase 1: extract from queue → goto → carry → place on bench → loop
- JobDriver Phase 2: haul item to bench
- JobDriver Phase 3: work
- JobDriver Phase 4: consume ingredients from bench cells, apply result
- If materials unavailable, WorkGiver simply doesn't create the job (no infinite loop)

**Repair:** One cycle per job. Each cycle gathers materials → works → skill check. If item still damaged, designation stays and WorkGiver creates a new job with fresh ingredients for the next cycle.

**Clean:** All materials gathered upfront → work → consume → remove taint.

**Drag-select fix:** Added `DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle` to all three designators. This enables the vanilla drag-rectangle rendering.

**Material costs (deterministic, ceiling-rounded):**
- Repair cycle: 10% of base materials × 1.2 × techDifficulty per cycle. Components at 50% rate.
- Clean: 20% of base materials (non-intricate). Minimum 1 of primary material.

**Files changed:**
- `MaterialUtility.cs` — added `TryFindIngredients()`, `ConsumeIngredientsOnBench()`, switched to ceiling rounding
- `WorkGiver_R4Repair.cs` — finds ingredients, populates job queues
- `WorkGiver_R4Clean.cs` — finds ingredients, populates job queues
- `JobDriver_R4Repair.cs` — full rewrite with ingredient hauling phase
- `JobDriver_R4Clean.cs` — full rewrite with ingredient hauling phase
- All three `Designator_*.cs` — added `DrawStyleCategory` override

---

## M4: Bill-Based Automation

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

**XML safety:** No Sequence/Conditional for comp injection. Every SpecialThingFilterDef needs parentCategory.

**Designator drag-select:** Must override `DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle`. Without this, the drag rectangle doesn't render and multi-cell designation doesn't work properly.

**Ingredient gathering:** Never consume materials remotely from the map. Use the vanilla queue pattern: WorkGiver finds stacks → populates `targetQueueB`/`countQueue` → JobDriver hauls to bench surface → consumes from `IngredientStackCells` after work. This is how `JobDriver_DoBill` handles it.

**Material costs should be deterministic:** Use `Mathf.CeilToInt` not `GenMath.RoundRandom` for costs. Players need to know exactly what's required. Probabilistic rounding is fine for returns (recycle products) but not for costs.

**One cycle per job for repair:** Rather than multi-cycle within a single job, do one cycle per job attempt. The WorkGiver gathers fresh materials each time. This is simpler, avoids mid-job material shortages, and naturally handles interruptions — the designation persists until full HP.

**Designation specificity:** Use `RemoveDesignation(specific)` not `RemoveAllDesignationsOn` — repair and clean can coexist on the same item.

**Workbench routing:** `def.recipeMaker?.recipeUsers` for per-item routing. Revolver→machining, sword→smithy, jacket→tailor.
