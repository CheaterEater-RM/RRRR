# R⁴ — Milestones

## M0–M3: Core Features ✅

Recycle, Repair, Clean all functional with designator-based workflow.

---

## Material Cost & Ingredient Gathering ✅

Vanilla-compatible ingredient queue pattern using `JobDriver_DoBill.CollectIngredientsToils()`.

---

## M4: Bill-Based Automation ✅

**Architecture:** RecipeDefs provide the bill UI (filters, quality/HP sliders). A custom
`WorkGiver_R4DoBill` scans bill stacks on specific benches, finds matching items, and
creates our existing custom jobs (not vanilla's `JobDriver_DoBill`). This follows the
pattern established by the RepairableGear reference mod.

**Key design:** RecipeDefs with empty `<products/>` and no `workerClass` — they're purely
UI definitions. The WorkGiver checks `bill.IsFixedOrAllowedIngredient(t)` against items
on the map, then creates our `RRRR_Recycle` or `RRRR_Clean` jobs.

**Vanilla smelting override:** XML patches remove SmeltWeapon/SmeltApparel from the smelter.
Our recycle bills replace them with HP/quality/skill-aware material returns.

**WorkGiver binding via workType:**
- Smelter + Machining → `Crafting` work type
- Smithy → `Smithing` work type
- Tailor benches → `Tailoring` work type

**JobDrivers updated:** Both `JobDriver_R4Recycle` and `JobDriver_R4Clean` now handle
bill-driven jobs alongside designation-driven jobs. Bill jobs skip designation checks
and call `bill.Notify_IterationCompleted` on completion.

**Minor mending (≥95% HP = free repair):** Integrated into `WorkGiver_R4Repair` —
skips material finding when item HP is at or above 95%.

**Files:**
- `WorkGiver_R4DoBill.cs` — custom WorkGiver that scans bill stacks
- `WorkGivers.xml` — 4 new bill-based WorkGiverDefs (smelter, smithy, machining, tailor)
- `Recipes.xml` — 3 RecipeDefs with empty products
- `VanillaSmelting.xml` — patches removing SmeltWeapon/SmeltApparel from smelter
- `JobDriver_R4Recycle.cs` — updated for bill support
- `JobDriver_R4Clean.cs` — updated for bill support

---

## M5: Polish & Publishing

- [ ] Replace placeholder textures with proper icons
- [ ] Full settings UI, comprehensive translation keys
- [ ] Remove all debug logging
- [ ] README.md, Steam Workshop assets
- [ ] Compatibility testing (CE, VE, etc.)
- [ ] Performance profiling
- [ ] Minor mending polish (messaging, designator tooltip)

---

## Lessons Learned

**Bill system architecture:** Don't try to use vanilla's `JobDriver_DoBill` with custom
`RecipeWorker` overrides — it's fragile and hard to debug. Instead, use RecipeDefs purely
for the bill UI (ingredient filters, quality/HP sliders), and drive work with a custom
`WorkGiver_Scanner` that reads the bill stack and creates your own custom jobs. This is
the pattern used by RepairableGear and is much more reliable.

**WorkType binding:** Each bench type has its own `WorkGiverDef` with a specific `workType`
(Crafting, Smithing, Tailoring). Bills on a bench are only processed by WorkGivers whose
`fixedBillGiverDefs` includes that bench AND whose `workType` matches the pawn's work
assignment.

**Vanilla smelting redundancy:** Vanilla's SmeltWeapon/SmeltApparel return a flat 25%
ignoring all factors. Our recycling is strictly superior. Remove the vanilla recipes
via XML patches to avoid player confusion.

**Dual-mode JobDrivers:** When a JobDriver serves both designations and bills, use
`job.bill != null` to branch behavior: skip designation checks for bill jobs, call
`bill.Notify_IterationCompleted` on completion for bill jobs.
