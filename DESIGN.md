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
| `WorkGiver_R4RepairBill` | Bill-based: custom WorkGiver for repair bills with material hauling |
| `RecipeWorker_R4Recycle` | Bill-based: defers item destruction, skill-based product calculation |
| `RecipeWorker_R4Repair` | Bill-based: one repair cycle per iteration, consumes materials from map |
| `RecipeWorker_R4Clean` | Bill-based: removes taint, leaves item on bench |
| `JobDriver_R4Recycle` | Designation flow: haul item to bench → work → spawn materials → destroy item |
| `JobDriver_R4Repair` | Designation flow: gather ingredients → haul item → work → apply repair cycle |
| `JobDriver_R4Clean` | Designation flow: gather ingredients → haul item → work → remove taint |
| `WorkGiver_R4DesignationBase` | Abstract base for all designation WorkGivers, handles bench routing by work type |
| `MaterialUtility` | All material cost/return calculations, ingredient finding, sigmoid recycle curve |
| `WorkbenchRouter` | Maps item → valid workbench(es) via `recipeMaker.recipeUsers` + fallback |
| `SkillUtility` | Tech difficulty, repair success checks, failure severity |
| `R4WorkbenchFilterCache` | Startup cache: inverts recipeMaker.recipeUsers, builds per-bench filters, bench→WorkType map |
| `Designator_RecycleThing` | Orders menu designator for drag-select recycling |
| `Designator_RepairThing` | Orders menu designator for drag-select repair |
| `Designator_CleanThing` | Orders menu designator for drag-select taint cleaning |

### Harmony Patches (2 total)

| Target | Type | Purpose |
|---|---|---|
| `Thing.GetGizmos` | Postfix | Inject per-item R4 gizmo buttons (recycle, repair, clean) with rich tooltips |
| `Building_WorkTable.SpawnSetup` | Postfix | Strip stale bills (e.g. vanilla SmeltWeapon) on load/placement for save compat |

### Designation Flow

**Designator (map-order):** Player clicks map-order designator or drag-selects → places `DesignationDef` on items → WorkGiver scans `designationManager.SpawnedDesignationsOfDef(...)` → pawn hauls to bench and works. `DesignateSingleCell` designates ALL matching items per cell for drag-select. Designators are injected into the Orders menu via `Patches/OrdersMenu.xml` (`specialDesignatorClasses`).

**Designation persistence (repair):** The R4_Repair designation stays on the item until it reaches full HP or is destroyed. If the pawn is interrupted or the planned cycles finish but the item still has damage from failures, the designation remains so another job picks it up.

**Mutual exclusivity:** Recycle cancels both Repair and Clean. Repair and Clean cancel Recycle but can coexist (a tainted damaged item may need both).

**Gizmo (direct select):** Select item → click gizmo → gizmo places designation → same WorkGiver flow. Gizmos injected via `Thing.GetGizmos` Harmony postfix. Rich tooltips show bench routing, material costs, and success chance estimates (at skill 10).

**Bills (automated, M4):** Standing bills with custom `RecipeWorker` subclasses. Recycle and Clean use vanilla's `WorkGiver_DoBill` pipeline; Repair uses custom `WorkGiver_R4RepairBill` because the item must be hauled along with repair materials.

### WorkGiver Architecture

Each designation action (recycle, repair, clean) is registered under **three WorkGiverDefs** — Crafting, Smithing, Tailoring — so the work tab column matches the bench type. The shared `WorkGiver_R4DesignationBase.FindBench` filters candidates to benches whose WorkTypeDef matches the WorkGiver's own `def.workType`.

Bill-based repair uses per-bench `WorkGiver_R4RepairBill` defs with `fixedBillGiverDefs`. Repair recipes use `requiredGiverWorkType=Crafting` to block vanilla's `WorkGiver_DoBill` on smithing/tailoring benches (our custom WorkGiver bypasses this gate).

### Workbench Routing

**Primary strategy:** Each item's `recipeMaker.recipeUsers` lists the benches where it was originally crafted. R4 routes the item to those same benches for recycling/repair. This means a revolver goes to the machining table, a longsword to the smithy, and a jacket to the tailor bench — automatically correct for vanilla and modded items.

**Fallback** (for items without `recipeMaker`, e.g. quest rewards, trader goods, loot): Route by `techLevel` to an appropriate bench (Animal/Neolithic→CraftingSpot, Medieval→Smithy, Industrial→Machining, Spacer+→Fabrication). Last resort → machinining table.

**Eligibility:** Bench routing uses a broad gear predicate: `useHitPoints && (IsWeapon || IsApparel)`. Repair and recycle both use that same check. Clean uses the apparel subset of that rule. `smeltable` is not used for R4 eligibility. Explicit exclusions live in `1.6/Defs/EligibilityExclusions.xml` so outliers can be blocked without hardcoding them into the predicate.

`R4WorkbenchFilterCache` builds all mappings at startup:
1. Inverts `recipeMaker.recipeUsers` → `BenchCraftables[bench] = {items}`
2. Assigns fallback items by `techLevel`
3. Stamps per-bench ThingFilters onto every RRRR RecipeDef's `fixedIngredientFilter`
4. Builds `BenchWorkTypes[bench] = WorkTypeDef` for designation WorkGiver routing

## XML Integration

### Comp Injection: No Longer Needed

The original design used `CompRecyclable` for tracking designations and gizmos. This was replaced by a simpler approach: designations are managed via `DesignationDef`s (no comp needed), and gizmos are injected via a `Thing.GetGizmos` Harmony postfix. This eliminates all comp injection XML patches and their associated crash risks.

### Vanilla Smelting Override

`VanillaSmelting.xml` removes `SmeltWeapon`, `SmeltApparel`, and `SmeltOrDestroyThing` from the electric smelter. R4's per-bench recycle bills replace these with skill-based recycling. `DestroyWeapon`/`DestroyApparel` and `ExtractMetalFromSlag` are kept intact.

`Patch_BuildingWorkTable_SpawnSetup` (Harmony postfix) strips stale bills from saved workbenches whose recipe is no longer in the bench's `AllRecipes` list, ensuring clean save transitions.

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
- Tech difficulty from `recipeMaker.researchPrerequisite.techLevel`: Neolithic 0.80 → Archotech 2.00 (× `Settings.repairTechDifficultyMult`)
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
- `def.recipeMaker?.recipeUsers` — `List<ThingDef>` of benches where the item is crafted (primary routing)
- Fallback: `R4WorkbenchFilterCache.BenchCraftables` (techLevel-based assignment)
- Last resort: `TableMachining`

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
- Recycle/Clean bills: vanilla `WorkGiver_DoBill` → `JobDriver_DoBill` → custom `RecipeWorker`
- Repair bills: custom `WorkGiver_R4RepairBill` → custom `JobDriver_R4Repair` (uses `JobDriver_DoBill.CollectIngredientsToils`)
- `RecipeWorker.ConsumeIngredient` overridden to prevent item destruction
- `RecipeWorker.Notify_IterationCompleted` handles actual R4 logic (has pawn reference for skill)

## Settings

`RRRR_Mod` extends `Verse.Mod`, `RRRR_Settings` extends `ModSettings`. Settings UI uses `Listing_Standard` with sliders and reset button.

| Setting | Default | Range | Effect |
|---|---|---|---|
| `recycleGlobalMult` | 1.0 | 0.1–2.0 | Global scalar on all recycle yields |
| `skipIntricateComponents` | true | — | Exclude components from recycle returns |
| `repairHpPerCycle` | 0.20 | 0.05–0.50 | HP fraction restored per cycle (drives cycle count) |
| `repairTechDifficultyMult` | 1.0 | 0.5–3.0 | Scalar on tech difficulty for repair success |
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
    ├── Jobs\                 ← JobDrivers (Recycle, Repair, Clean), WorkGivers (designation + bill)
    ├── Patches\              ← Harmony patches (Thing.GetGizmos, Building_WorkTable.SpawnSetup)
    ├── RecipeWorkers\        ← RecipeWorker_R4Recycle, RecipeWorker_R4Repair, RecipeWorker_R4Clean
    └── Utility\              ← MaterialUtility, WorkbenchRouter, SkillUtility
```
