# R⁴: Reduce, Reuse, Recycle — Design Document

## Overview

R⁴ adds three item-management actions using existing workbenches — no new buildings, tech, or research:

- **Recycle** — destroy item, recover materials (scaled by condition, quality, skill, taint)
- **Repair** — restore HP in skill-based cycles with failure/crit mechanics
- **Clean** — remove corpse-taint from apparel, consuming materials

## Mod Identity

| Field | Value |
|---|---|
| Package ID | `CheaterEater.RRRR` |
| Harmony ID | `com.cheatereater.rrrr` |
| Namespace | `RRRR` |
| Target | RimWorld 1.6 |

## Architecture

### Core Components

| Component | Purpose |
|---|---|
| `WorkGiver_R4Recycle` | Designation-based: scans for designated items, creates recycle jobs (per work type) |
| `WorkGiver_R4Repair` | Designation-based: scans for items needing repair, creates repair jobs |
| `WorkGiver_R4Clean` | Designation-based: scans for tainted apparel, creates clean jobs |
| `WorkGiver_R4RepairBill` | Bill-based: custom WorkGiver for repair bills with material hauling; clears stale bench ingredients, removes incompletable bills, memoizes same-tick scans, and preserves the bench target with `haulMode = ToCellNonStorage` |
| `WorkGiver_R4CleanBill` | Bill-based: custom WorkGiver for clean bills with material hauling; clears stale bench ingredients, removes incompletable bills, memoizes same-tick scans, and preserves the bench target with `haulMode = ToCellNonStorage` |
| `JobDriver_R4WorkBase` | Shared base class for Repair and Clean job drivers; centralizes work toil timing, fail conditions, and ingredient handling |
| `RecipeWorker_R4Recycle` | Bill-based: defers item destruction, skill-based product calculation |
| `RecipeWorker_R4Repair` | Bill-based: prevents bill ingredient destruction; actual repair logic runs in `JobDriver_R4Repair` |
| `RecipeWorker_R4Clean` | Bill-based: removes taint, leaves item on bench |
| `JobDriver_R4Recycle` | Designation flow: haul item onto bench stack cells → delta-scaled work with interaction-cell reservation and bench `UsedThisTick` parity → spawn materials → destroy item |
| `JobDriver_R4Repair` | Designation flow: gather ingredients → haul item onto bench stack cells → work → apply repair cycle |
| `JobDriver_R4Clean` | Designation flow: gather ingredients → haul item onto bench stack cells → work → remove taint |
| `WorkGiver_R4DesignationBase` | Abstract base for all designation WorkGivers, handles bench routing by work type |
| `MaterialUtility` | All material cost/return calculations, ingredient finding, sigmoid recycle curve |
| `WorkbenchRouter` | Maps item → ordered list of valid workbench(es) via `recipeMaker.recipeUsers`, VEF bench aliases, and per-bench catch-all predicates; precomputed merged cache at startup |
| `SkillUtility` | Tech difficulty, repair success checks, failure severity |
| `R4WorkbenchFilterCache` | Startup cache: inverts recipeMaker.recipeUsers, builds per-bench filters, bench→WorkType map |
| `Designator_RecycleThing` | Orders menu designator for drag-select recycling |
| `Designator_RepairThing` | Orders menu designator for drag-select repair |
| `Designator_CleanThing` | Orders menu designator for drag-select taint cleaning |

### Harmony Patches (4 total)

| Target | Type | Purpose |
|---|---|---|
| `Thing.GetGizmos` | Postfix | Inject per-item R4 gizmo buttons (recycle, repair, clean) with rich tooltips |
| `Building_WorkTable.SpawnSetup` | Postfix | Strip stale bills (e.g. vanilla SmeltWeapon) on load/placement for save compat |
| `Toils_Haul.PlaceHauledThingInCell` | Postfix | Track placed ingredient stacks for R4 repair/clean jobs via `job.placedThings` and enable correct ingredient consumption |
| `WorkGiver_DoBill.JobOnThing` | Postfix | Null out R4 repair and clean jobs so vanilla bill search cannot run in parallel with the custom R4 bill WorkGivers |

### Designation Flow

**Designator (map-order):** Player clicks map-order designator or drag-selects → places `DesignationDef` on items → WorkGiver scans `designationManager.SpawnedDesignationsOfDef(...)` → pawn hauls to bench and works. `DesignateSingleCell` designates ALL matching items per cell for drag-select. Designators are injected into the Orders menu via `Patches/OrdersMenu.xml` (`specialDesignatorClasses`).

**Designation persistence (repair):** The R4_Repair designation stays on the item until it reaches full HP or is destroyed. If the pawn is interrupted or the planned cycles finish but the item still has damage from failures, the designation remains so another job picks it up.

**Mutual exclusivity:** Recycle cancels both Repair and Clean. Repair and Clean cancel Recycle but can coexist (a tainted damaged item may need both).

**Gizmo (direct select):** Select item → click gizmo → gizmo places designation → same WorkGiver flow. Gizmos injected via `Thing.GetGizmos` Harmony postfix. Rich tooltips show bench routing, material costs, and success chance estimates (at skill 10).

**Bills (automated, M4):** Standing bills with custom `RecipeWorker` subclasses. Recycle uses vanilla's `WorkGiver_DoBill` pipeline. Repair and Clean use custom `WorkGiver_R4RepairBill` / `WorkGiver_R4CleanBill` because the worked item and dynamic material costs need to be handled together. A Harmony postfix on `WorkGiver_DoBill.JobOnThing` strips out R4 repair and clean jobs so vanilla bill search does not race the custom bill paths.

**Bench staging:** Ingredient hauling for R4 repair/clean uses the bench's `IngredientStackCells`, but the worked item itself must stage to a separate nearby cell that does not reuse tracked ingredient stacks. Reusing `IngredientStackCells` for the worked item can invalidate `job.placedThings` before the work toil starts.

### WorkGiver Architecture

Each designation action (recycle, repair, clean) is registered under **three WorkGiverDefs** — Crafting, Smithing, Tailoring — so the work tab column matches the bench type. The shared `WorkGiver_R4DesignationBase.FindBench` filters candidates to benches whose WorkTypeDef matches the WorkGiver's own `def.workType`.

Bill-based repair and clean use per-bench `WorkGiver_R4RepairBill` / `WorkGiver_R4CleanBill` defs with `fixedBillGiverDefs`. Their recipes use `requiredGiverWorkType=Crafting` as defense-in-depth on non-Crafting benches, but the primary de-duplication fix is the Harmony postfix on `WorkGiver_DoBill.JobOnThing`, which removes R4 repair and clean jobs from the vanilla path. Repair and Clean bill WorkGivers also clear stale bill-giver ingredients before creating a new job, call `BillStack.RemoveIncompletableBills()` to match vanilla bill hygiene, memoize same-tick scanner results to avoid duplicate full searches across `HasJobOnThing` and `JobOnThing`, and set `job.haulMode = HaulMode.ToCellNonStorage` so the bench target remains stable throughout the custom bill pipeline.

### Workbench Routing

**Primary strategy:** Each item's `recipeMaker.recipeUsers` lists the benches where it was originally crafted. R4 routes the item to those same benches for recycling/repair. This means a revolver goes to the machining table, a longsword to the smithy, and a jacket to the tailor bench — automatically correct for vanilla and modded items.

**VEF inheritance support:** If a bench inherits recipes via `VEF.Buildings.RecipeInheritanceExtension`, R4 expands the declared source bench into its aliased benches during cache build. This lets designation routing, gizmo bench labels, and dynamic R4 bill injection see benches like `VFEC_CraftingBench` even when the crafted item still only lists `CraftingSpot` in `recipeMaker.recipeUsers`.

**Catch-all predicates:** After native bills are folded in, each vanilla bench has a predicate over `def.techLevel` and `def.stuffCategories` that catches additional items. The intent: each tier's benches collectively cover everything at that tier and below, so mod-added gear is routed without needing per-mod registration. Items can land on multiple benches simultaneously — that's deliberate, and dedup is per-bench via HashSet so native bills stay authoritative for benches that already list the item.

| Bench | Catch-all predicate |
|---|---|
| CraftingSpot | `techLevel ≤ Neolithic` |
| HandTailoringBench | `(Fabric or Leathery) AND techLevel ≤ Medieval` |
| FueledSmithy | `(NOT Fabric AND NOT Leathery) AND techLevel ≤ Medieval` |
| ElectricTailoringBench | `(Fabric or Leathery) AND techLevel ≤ Industrial` |
| ElectricSmithy | `((NOT Fabric AND NOT Leathery) AND techLevel ≤ Medieval) OR ((Metallic or Woody) AND techLevel ≤ Industrial)` |
| TableMachining | `(NOT Fabric AND NOT Leathery AND NOT Metallic AND NOT Woody) AND techLevel ≤ Industrial` |
| FabricationBench | `techLevel ≤ Archotech` |

`stuffCategories` semantics: null/empty means a fixed-cost item — it satisfies all "NOT X" clauses (contains none of those) and fails all "X or Y" clauses (contains neither). `TechLevel.Undefined` is treated as Industrial via `EffectiveTechLevel`, preserving the prior Undefined→machining/fabrication routing. FabricationBench's `≤ Archotech` ceiling deviates from the original `≤ Spacer` framing so Ultra/Archotech gear without `recipeMaker` is still covered; it is the true universal fallback.

**Designation routing is closest-wins.** `WorkbenchRouter.GetValidBenches` returns an ordered list (recipeMaker first, then catch-all in tier order, then any other modded benches) for tooltip stability, but `WorkGiver_R4DesignationBase.FindBench` pools all matching benches and lets `GenClosest.ClosestThingReachable` pick the closest reachable one. Order does NOT decide routing — a Medieval longsword routes to a nearby FabricationBench in preference to a far FueledSmithy, by design.

**Eligibility:** Bench routing uses a broad gear predicate: `useHitPoints && (IsWeapon || IsApparel)`. Repair and recycle both use that same check. Clean uses the apparel subset of that rule. `smeltable` is not used for R4 eligibility. Explicit exclusions live in `1.6/Defs/EligibilityExclusions.xml` so outliers can be blocked without hardcoding them into the predicate.

`R4WorkbenchFilterCache` builds all mappings at startup:
1. Inverts `recipeMaker.recipeUsers` → `BenchCraftables[bench] = {items}` (native bills)
2. Applies catch-all predicates per bench (tier + stuff-category) so each tier collectively covers everything ≤ that tier
3. Stamps per-bench ThingFilters onto every RRRR RecipeDef's `fixedIngredientFilter`
4. Builds `BenchWorkTypes[bench] = WorkTypeDef` for designation WorkGiver routing
5. Builds `WorkbenchRouter.MergedBenchCache` from the inverse of `BenchCraftables`, ordered (recipeMaker → catch-all tier → other) for tooltip stability

## XML Integration

### Comp Injection: No Longer Needed

The original design used `CompRecyclable` for tracking designations and gizmos. This was replaced by a simpler approach: designations are managed via `DesignationDef`s (no comp needed), and gizmos are injected via a `Thing.GetGizmos` Harmony postfix. This eliminates all comp injection XML patches and their associated crash risks.

### Vanilla Smelting Override

`VanillaSmelting.xml` removes `SmeltWeapon`, `SmeltApparel`, and `SmeltOrDestroyThing` from the electric smelter. R4's per-bench recycle bills replace these with skill-based recycling. `DestroyWeapon`/`DestroyApparel` and `ExtractMetalFromSlag` are kept intact.

`Patch_BuildingWorkTable_SpawnSetup` (Harmony postfix) strips stale bills from saved workbenches whose recipe is no longer in the bench's `AllRecipes` list, ensuring clean save transitions without unconditional release-log spam.

### Orders Menu

`OrdersMenu.xml` adds the three designator classes to the Orders menu via `specialDesignatorClasses`.

## Formulas

### Recycle Work Amount
```
work = clamp(WorkToMake × 0.15, 400, 2000)
```
Work rate uses `GeneralLaborSpeed` stat × `WorkTableWorkSpeedFactor`.

### Recycle Return (Sigmoid Model)

Inputs normalised to [0,1]:
- **Skill:** `s = skill / 20`
- **HP:** `h = HitPoints / MaxHitPoints`
- **Quality:** `q = QualityScores[quality]` (Awful=0.0, Normal=0.35, Legendary=1.0)

Weighted score: `x = 0.35·s + 0.40·h + 0.25·q`

Logistic sigmoid: `σ(x) = 1 / (1 + e^{-7(x - 0.70)})`

Renormalised to [0.05, 1.0]: `return = 0.05 + 0.95 × (σ(x) - σ(0)) / (σ(1) - σ(0))`

Post-sigmoid modifiers:
- **Taint:** ×0.60 (floored at 0.05)
- **Rare materials:** Components ×0.25, Adv. components ×0.15, Chemfuel ×0.30
- **Global multiplier:** `Settings.recycleGlobalMult`

Probabilistic rounding (`GenMath.RoundRandom`); minimum 1 guaranteed.

### Repair
- **HP per cycle:** `Settings.repairHpPerCycle` (default 20%, configurable)
- **Cycles to full:** `Ceiling(1 / repairHpPerCycle)` (default 5)
- **Cost per cycle per material:** `Ceiling(baseCost / RepairCostDivisor)` where `RepairCostDivisor = RepairCyclesFull × 2`
  - Only included if `costPerCycle × RepairCyclesFull < baseCost` (total < make cost)
  - Fallback: 1 unit of highest-count material if nothing passes
- **Minor mending:** Items at ≥95% HP are repaired without material consumption
- Tech difficulty from `recipeMaker.researchPrerequisite.techLevel`: Neolithic 0.80 → Archotech 2.00
- Success: `(0.50 + skill×0.025) / techDifficulty`, clamped [0.05, 1.0]
- Minor failure: 5% HP loss
- Critical failure (below 50% HP, 20% of failures): 15% HP loss + quality drop via `CompQuality.SetQuality()`
- Item destroyed at 0 HP: partial material reclaim (25% of normal recycle yield)
- Designation persists until full HP or destruction

### Clean
- **Cost:** `Ceiling(baseCost / CleanCostDivisor)` where `CleanCostDivisor = Round(1 / Settings.cleanCostFraction)` (default 5)
- Same threshold guard as repair; fallback: 1 of highest-count material
- Always succeeds; skill reduces work time via `1 + (skillLevel × 0.03)` bonus
- `Apparel.WornByCorpse = false` — public setter in 1.6, followed by `Notify_ColorChanged()`

## Key API Notes

### Material Cost Lookup
- `thing.def.CostListAdjusted(thing.Stuff, errorOnNullStuff: false)` — extension method in `CostListCalculator`
- `thing.def.smeltProducts` — additional smelting products (spawned at full count during recycle)
- `ThingDef.intricate` — true for components/advanced components

### Workbench Routing
- `WorkbenchRouter.GetValidBenches(item)` — returns the precomputed merged list (recipeMaker order → catch-all tier order → other). Tooltip-stable; routing is closest-wins via `GenClosest`.
- `R4WorkbenchFilterCache.BenchCraftables` — bench → items, source of truth for both bill filters and merged router cache
- `R4WorkbenchFilterCache.OrderedCatchAllBenches` — vanilla bench list in tier-ascending order used to make merged-list ordering deterministic
- Last-resort safety: `TableMachining` when neither merged cache nor `recipeMaker` covers the item

### Workbench Usability
- `((IBillGiver)bench).UsableForBillsAfterFueling()` — WorkGiver check (power + fuel)
- `((IBillGiver)bench).CurrentlyUsableForBills()` — bill WorkGiver check

### Designation Management
- `AnySpawnedDesignationOfDef` — fast early-out in `WorkGiver.ShouldSkip`
- `SpawnedDesignationsOfDef` — iterate for work targets
- `RemoveAllDesignationsOn` — clean up on completion or destruction only
- `DesignationOn(thing, def)` — check for specific designation on a thing

### Work Speed
- `GeneralLaborSpeed` for all R4 work toils
- `WorkTableWorkSpeedFactor` for bench multiplier
- Repair work per cycle: `clamp(WorkToMake × 0.05, 200, 800)`
- Clean work: `clamp(WorkToMake × 0.15, 300, 1500)` with skill bonus `1 + (skill × 0.03)`

### Quality Degradation
- `CompQuality.SetQuality(QualityCategory, ArtGenerationContext?)` — public method, no reflection needed

### Bill Pipeline
- Recycle bills: vanilla `WorkGiver_DoBill` → `JobDriver_DoBill` → custom `RecipeWorker_R4Recycle`
- Repair bills: custom `WorkGiver_R4RepairBill` → custom `JobDriver_R4Repair` (uses `JobDriver_DoBill.CollectIngredientsToils`)
- Clean bills: custom `WorkGiver_R4CleanBill` → custom `JobDriver_R4Clean` (uses `JobDriver_DoBill.CollectIngredientsToils`)
- Vanilla `WorkGiver_DoBill` is allowed to search normally but any resulting R4 repair or clean job is nulled out by Harmony so only the custom bill paths execute
- `RecipeWorker.ConsumeIngredient` overridden to prevent item destruction
- `JobDriver_R4Repair` and `JobDriver_R4Clean` now inherit from `JobDriver_R4WorkBase`, aligning their work loop with vanilla `JobDriver_DoBill` and centralizing `tickAction`/`tickIntervalAction` semantics
- `RecipeWorker.Notify_IterationCompleted` handles recycle/clean completion logic; repair completion is handled in `JobDriver_R4Repair`

## Settings

`RRRR_Mod` extends `Verse.Mod`, `RRRR_Settings` extends `ModSettings`. Settings UI uses `Listing_Standard` with sliders and reset button.

| Setting | Default | Range | Effect |
|---|---|---|---|
| `recycleGlobalMult` | 1.0 | 0.1–2.0 | Global scalar on all recycle yields |
| `skipIntricateComponents` | false | — | Exclude components from recycle returns |
| `repairHpPerCycle` | 0.20 | 0.05–0.50 | HP fraction restored per cycle (drives cycle count) |
| `cleanCostFraction` | 0.20 | 0.05–0.50 | Material cost as fraction of make cost |

Derived: `RepairCyclesFull = Ceil(1/repairHpPerCycle)`, `RepairCostDivisor = RepairCyclesFull × 2`, `CleanCostDivisor = Round(1/cleanCostFraction)`.

## Install/Uninstall Safety

**Safe to add mid-save:** No custom ThingComps on persistent objects, no custom MapComponents or WorldComponents. Designations and bills are created dynamically. The `Patch_BuildingWorkTable_SpawnSetup` postfix strips stale vanilla smelt bills on first load.

**Safe to remove mid-save:** All mod-specific data (designations, jobs, bills) references mod-defined Defs. RimWorld's save system gracefully handles missing defs: designations are silently removed, active jobs fail and pawns get new ones, bills with missing recipes are stripped. Vanilla smelting recipes return when the XML patch stops applying. Settings file is left unused. No save corruption occurs.

**Caution on removal:** Players will need to manually re-add vanilla SmeltWeapon/SmeltApparel bills to their electric smelters after uninstalling.

## File Layout

```
RRRR\
├── About\About.xml
├── 1.6\
│   ├── Assemblies\          ← build output
│   ├── Defs\                ← JobDefs, WorkGiverDefs, DesignationDefs, RecipeDefs, SpecialThingFilters
│   └── Patches\             ← Orders menu injection, Vanilla smelting override
├── Textures\UI\Designators\ ← PNG files (menu + designation icons)
├── Languages\English\Keyed\Keys.xml
└── Source\
    ├── RRRR.sln / .csproj
    ├── Setup.cs              ← [StaticConstructorOnStartup] + Harmony init + cache trigger
    ├── Settings.cs           ← Verse.Mod subclass + ModSettings
    ├── Cache\                ← R4WorkbenchFilterCache (bench→item mapping, bench→WorkType, recipe filter stamping)
    ├── Defs\                 ← R4DefOf
    ├── Designators\          ← Designator_RecycleThing, Designator_RepairThing, Designator_CleanThing
    ├── Filters\              ← SpecialThingFilterWorker_Damaged, SpecialThingFilterWorker_Tainted
    ├── Jobs\                 ← JobDrivers (Recycle, Repair, Clean, shared Repair/Clean base), WorkGivers (designation + bill)
    ├── Patches\              ← Harmony patches (Thing.GetGizmos, Building_WorkTable.SpawnSetup, PlaceHauledThingInCell)
    ├── RecipeWorkers\        ← RecipeWorker_R4Recycle, RecipeWorker_R4Repair, RecipeWorker_R4Clean
    └── Utility\              ← MaterialUtility, WorkbenchRouter, SkillUtility
```

  ## In Progress

  ### Repair/Clean Staging Invariant

  Repair and clean jobs must not use the same staging contract for both the worked item and the consumed ingredients. The stable pattern across comparable mods is that either the item itself is the bill/job target and ingredients are separate, or the job operates directly on the item without separate bench staging. R4's current failure mode comes from mixing both models: ingredients are tracked through `job.placedThings` on bench staging cells, then the worked item is also dropped onto that same staging surface. That lets the worked-item drop invalidate tracked ingredient references before the work toil starts.

  Any follow-up implementation should preserve one clear ownership model:

  - ingredient stacks may be staged and tracked on bench cells, with the worked item kept on a separate nearby cell, or
  - the worked item remains the primary job target and is not independently re-staged onto ingredient cells at all.

  What must not happen is a second placement step that can merge with, replace, or despawn tracked ingredient stacks belonging to the same job.

  For R4 specifically, the preferred long-term model is:

  - the work item remains the authoritative job object throughout the repair/clean cycle
  - ingredients alone use the placed-things tracking contract
  - the work item may still be shown at the bench for player feedback, but only in a dedicated display cell that is excluded from ingredient staging for that job

  This display cell should be derived from the bench at runtime rather than stored as new persistent state. On benches with multiple occupied cells, one deterministic bench cell can be reserved as the display surface and removed from ingredient placement candidates. On 1x1 benches, where no separate occupied cell exists or the occupied cell is not spawnable, the display position must fall back to a deterministic adjacent cell near the interaction point. The user-facing requirement is "visibly at the bench," not "always on an occupied bench tile," because the latter is not physically valid for every vanilla bench shape.
