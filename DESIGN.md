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
| `CompRecyclable` | ThingComp on apparel. Tracks R4 designation, provides gizmos |
| `WorkGiver_R4Recycle` | Scans for designated items, creates recycle jobs (Crafting work type) |
| `WorkGiver_R4Repair` | Scans for items needing repair near benches, creates repair jobs |
| `JobDriver_R4Recycle` | Haul item to bench → work → spawn materials → destroy item |
| `JobDriver_R4Repair` | Multi-cycle repair with skill checks |
| `JobDriver_R4Clean` | Taint removal (M3) |
| `MaterialUtility` | All material/cost calculations |
| `WorkbenchRouter` | Maps item → valid workbench(es) via `recipeMaker.recipeUsers` |
| `SkillUtility` | Tech difficulty, repair success checks, failure severity |
| `R4ThingDefCache` | `[StaticConstructorOnStartup]` cache of fallback bench lists |

### Harmony Patches (1 total)

| Target | Type | Purpose |
|---|---|---|
| `ReverseDesignatorDatabase.InitDesignators` | Postfix | Inject gizmo designators so they appear on selected items |

### Designation Flow

**Designator (map-order):** Player clicks map-order designator or drag-selects → places `DesignationDef` on items → WorkGiver scans `designationManager.SpawnedDesignationsOfDef(...)` → pawn hauls to bench and works. `DesignateSingleCell` designates ALL matching items per cell for drag-select.

**Designation persistence (repair):** The R4_Repair designation stays on the item until it reaches full HP or is destroyed. If the pawn is interrupted or the planned cycles finish but the item still has damage from failures, the designation remains so another job picks it up.

**Gizmo (direct select):** Select item → click gizmo → gizmo places designation → same WorkGiver flow. Gizmos injected via `ReverseDesignatorDatabase.InitDesignators` postfix.

**Bills (automated, M4):** Standing bills with custom `SpecialThingFilterWorker`s — deferred.

### Workbench Routing

**Primary strategy:** Each item's `recipeMaker.recipeUsers` lists the benches where it was originally crafted. R4 routes the item to those same benches for recycling/repair. This means a revolver goes to the machining table, a longsword to the smithy, and a jacket to the tailor bench — automatically correct for vanilla and modded items.

**Fallback** (for items without `recipeMaker`, e.g. quest rewards, trader goods, loot): Route by `Smeltable` → smelt benches, or `IsApparel` → apparel crafting benches, or last resort → any available bench from the cache.

`R4ThingDefCache` builds the fallback bench lists at startup by scanning `DefDatabase<ThingDef>` for benches with `SmeltWeapon`/`SmeltApparel` or `Make_Apparel_BasicShirt` recipes.

## XML Integration

### Comp Injection: thingClass Limitation

Vanilla weapons use `thingClass="ThingWithComps"`, shared by hundreds of unrelated defs. XML comp injection targets apparel only (`thingClass="Apparel"`). The designator checks `def.IsWeapon || def.IsApparel` directly without requiring the comp.

### The Safe Two-Step Comp Injection Pattern

Two flat `PatchOperationAdd` operations: ensure `<comps>` exists, then inject the comp. No Sequence/Conditional/Test.

### The Startup Crash: Root Causes (historical)

1. `PatchOperationSequence`/`PatchOperationConditional` for comp injection → half-applied patches on defs without `<comps>` nodes
2. `SpecialThingFilterDef` with null `parentCategory` → `ThingCategoryNodeDatabase.FinalizeInit()` NPE

## Formulas

### Recycle Work Amount
```
work = clamp(WorkToMake × 0.15, 400, 2000)
```
Work rate uses `GeneralLaborSpeed` stat × `WorkTableWorkSpeedFactor`.

### Recycle Return
```
return = baseCost × condition × quality × skill × taintPenalty × rarePenalty × globalMult
```
- **Condition:** `(HP/maxHP) ^ 1.8`
- **Quality:** Awful 0.60 → Legendary 1.20
- **Skill:** `0.10 + 0.90 × (skill/20)^0.6`
- **Taint:** 0.50 for tainted items
- **Rare materials:** Components 0.25, Adv. components 0.15, Chemfuel 0.30
- Probabilistic rounding (`GenMath.RoundRandom`); minimum 1 guaranteed

### Repair (M2)
- Cycles of 10% maxHP per cycle
- Tech difficulty from `recipeMaker.researchPrerequisite.techLevel`: Neolithic 0.80 → Archotech 2.00
- Success: `(0.50 + skill×0.025) / techDifficulty`, clamped [0.05, 1.0]
- Minor failure: 5% HP loss
- Critical failure (below 50% HP, 20% of failures): 15% HP loss + quality drop via `CompQuality.SetQuality()`
- Item destroyed at 0 HP: partial material reclaim scaled by `cyclesCompleted / totalCyclesNeeded`
- Designation persists until full HP or destruction

### Clean (M3)
- Cost: `baseCost × 0.15` (flat, HP-independent)
- Always succeeds; skill reduces work time
- `Apparel.WornByCorpse = false` — public setter in 1.6

## Key API Notes

### Material Cost Lookup
- `thing.def.CostListAdjusted(thing.Stuff)` — extension method in `CostListCalculator`
- `thing.def.smeltProducts` — additional smelting products
- `ThingDef.intricate` — true for components/advanced components

### Workbench Routing
- `def.recipeMaker?.recipeUsers` — `List<ThingDef>` of benches where the item is crafted (primary routing)
- `thing.Smeltable` — fallback routing criterion

### Workbench Usability
- `((IBillGiver)bench).UsableForBillsAfterFueling()` — WorkGiver check (power + fuel)

### Designation Management
- `AnySpawnedDesignationOfDef` — fast early-out in `WorkGiver.ShouldSkip`
- `SpawnedDesignationsOfDef` — iterate for work targets
- `RemoveAllDesignationsOn` — clean up on completion or destruction only

### Work Speed
- `GeneralLaborSpeed` for non-recipe manual work
- `WorkTableWorkSpeedFactor` for bench multiplier

### Quality Degradation
- `CompQuality.SetQuality(QualityCategory, ArtGenerationContext?)` — public method, no reflection needed

## Settings

`Listing_Standard` UI with sliders. Reset to defaults. `Verse.Mod` subclass for `ModSettings` — separate from `[StaticConstructorOnStartup]`, never reference Defs from constructor.

## File Layout

```
RRRR\
├── About\About.xml
├── 1.6\
│   ├── Assemblies\          ← build output
│   ├── Defs\                ← JobDefs, WorkGiverDefs, DesignationDefs
│   └── Patches\             ← XML comp injection, Orders menu designators
├── Textures\UI\Designators\ ← PNG files (MUST exist at load time)
├── Languages\English\Keyed\Keys.xml
└── Source\
    ├── RRRR.sln / .csproj
    ├── Setup.cs              ← [StaticConstructorOnStartup] + cache trigger
    ├── Settings.cs           ← Verse.Mod subclass + ModSettings
    ├── Cache\                ← R4ThingDefCache (fallback bench lists)
    ├── Comps\                ← CompRecyclable, CompProperties_Recyclable
    ├── Designators\          ← Designator_RecycleThing, Designator_RepairThing
    ├── Jobs\                 ← JobDrivers, WorkGivers
    ├── Utility\              ← MaterialUtility, WorkbenchRouter, SkillUtility
    ├── Patches\              ← Harmony patches (ReverseDesignatorDatabase postfix)
    └── Defs\                 ← R4DefOf
```
