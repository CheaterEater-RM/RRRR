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
| `JobDriver_R4Repair` | Multi-cycle repair with skill checks (M2) |
| `JobDriver_R4Clean` | Taint removal (M3) |
| `MaterialUtility` | All material/cost calculations |
| `WorkbenchRouter` | Maps item type → valid workbench(es) using recipe presence, not hardcoded defNames |
| `SkillUtility` | Skill factor, repair success checks, failure severity |
| `R4ThingDefCache` | `[StaticConstructorOnStartup]` cache of bench lists, built once at load |

### Harmony Patches (1 total)

| Target | Type | Purpose |
|---|---|---|
| `ReverseDesignatorDatabase.InitDesignators` | Postfix | Inject gizmo designators so they appear on selected items |

> **Removed:** The original design listed `ThingDef.ResolveReferences` as a fallback comp injection patch. XML `<comps>` injection via `PatchOperationAdd` (targeting apparel/weapon base ThingDefs) is the correct and sufficient approach. A Harmony patch on `ResolveReferences` is fragile and unnecessary.

### Designation Flow

**Designator (map-order):** Player clicks map-order designator or drag-selects → places `DesignationDef` on items → `WorkGiver_R4Recycle` scans `designationManager.SpawnedDesignationsOfDef(...)` in `PotentialWorkThingsGlobal` → pawn hauls to bench and works. `DesignateSingleCell` designates ALL matching items per cell for proper drag-select support.

**Gizmo (direct select):** Select item → click gizmo on `CompRecyclable` → gizmo places designation → same WorkGiver flow handles it from there. Gizmos are added via `ReverseDesignatorDatabase.InitDesignators` postfix so they appear in the "select item" right-click context.

**Bills (automated, M4):** Standing bills at workbenches with custom `SpecialThingFilterWorker`s and `RecipeWorker` subclasses — deferred to milestone 4.

### Workbench Routing

Routing is determined by inspecting `ThingDef.AllRecipes` at startup (in `R4ThingDefCache`), not by hardcoded defName lists. This gives automatic compatibility with modded benches.

| Item Type | Criterion | Typical Result |
|---|---|---|
| Smeltable items | `thing.Smeltable` | Benches with `SmeltWeapon` or `SmeltApparel` recipe |
| Textile/leather apparel | Non-smeltable apparel | Benches with `Make_Apparel_BasicShirt` or `Make_Apparel_TribalA` recipe |
| Fallback | All else | Try smelt benches, then apparel benches |

## XML Integration

### Comp Injection: thingClass Limitation

**Critical finding:** Vanilla weapons (guns, melee) use `thingClass="ThingWithComps"`, which is shared by hundreds of unrelated defs. This means XML comp injection via `thingClass` cannot safely target weapons — only apparel (`thingClass="Apparel"`).

**Current approach:** XML injects `CompProperties_Recyclable` on apparel only. The designator works on both weapons and apparel because it checks `def.IsWeapon || def.IsApparel` directly, without requiring the comp. The comp is only needed for gizmos (M2+), at which point weapon comp injection will be handled in C# at startup after Defs load.

### The Safe Two-Step Comp Injection Pattern

Two flat, unconditional operations: first ensure `<comps>` exists, then inject the comp. No `PatchOperationSequence`, no `PatchOperationConditional`, no `PatchOperationTest`.

```xml
<Patch>
  <Operation Class="PatchOperationAdd">
    <xpath>Defs/ThingDef[thingClass="Apparel" and not(comps)]</xpath>
    <value>
      <comps/>
    </value>
  </Operation>

  <Operation Class="PatchOperationAdd">
    <xpath>Defs/ThingDef[thingClass="Apparel" and not(comps/li[@Class="RRRR.CompProperties_Recyclable"])]/comps</xpath>
    <value>
      <li Class="RRRR.CompProperties_Recyclable"/>
    </value>
  </Operation>
</Patch>
```

Each operation is atomic. If Step 1 finds no matching defs, it silently does nothing. If Step 2 finds a def that already has the comp, it skips it. Neither can corrupt the Def database.

### The Startup Crash: What Actually Happens

The observed crash pattern (confirmed by Player.log) is:

```
System.NullReferenceException at Verse.ThingCategoryNodeDatabase.FinalizeInit()
```

Followed by hundreds of missing texture errors for vanilla assets. **The textures are not the problem.** The NullReferenceException corrupts the Def database during loading. Two root causes were identified:

1. `PatchOperationSequence`/`PatchOperationConditional` chains for comp injection → half-applied patches on defs without `<comps>` nodes
2. `SpecialThingFilterDef` entries with null `parentCategory` → `ThingCategoryNodeDatabase.FinalizeInit()` unconditionally accesses `allDef3.parentCategory.childSpecialFilters` with no null check

### Texture Paths for DesignationDefs

`DesignationDef.texturePath` resolves from the mod's `Textures/` folder without the `Textures/` prefix. The PNG must exist at load time.

### Injecting Designators into the Orders Menu

`PatchOperationAdd` on `DesignationCategoryDef[defName="Orders"]/specialDesignatorClasses`. The `ReverseDesignatorDatabase.InitDesignators` Harmony postfix is also needed for gizmos on selected items. Both UI surfaces are required.

## Formulas

### Recycle Work Amount
```
work = clamp(WorkToMake × 0.15, 400, 2000)
```
- 15% of original crafting time, clamped for consistency
- For reference: vanilla SmeltWeapon is a flat 1600
- Work rate uses `GeneralLaborSpeed` stat × `WorkTableWorkSpeedFactor`

### Recycle Return
```
return = baseCost × condition × quality × skill × taintPenalty × rarePenalty × globalMult
```
- **Condition:** `(HP/maxHP) ^ 1.8` — nonlinear, punishes damage heavily
- **Quality:** Awful 0.60 → Legendary 1.20
- **Skill:** `0.10 + 0.90 × (skill/20)^0.6` — nonlinear, skill 0=10%, skill 10≈74%, skill 20=100%
- **Taint:** 0.50 for tainted items
- **Rare materials:** Components 0.25, Adv. components 0.15, Chemfuel 0.30
- **Target:** ~50% return at skill 10, Good quality, 80% HP
- **Clamped** to base cost; probabilistic rounding (`GenMath.RoundRandom`); minimum 1 guaranteed

Material recovery uses `def.CostListAdjusted(thing.Stuff)` as the base, with `def.smeltProducts` added on top for items that have them. Intricate components (components, advanced components) are filtered out by default but exposed as a player-facing setting.

### Repair (M2)
- Cycles of 10% maxHP per cycle
- Cost: `(cycleHP/maxHP) × baseCost × 1.2 × techDifficulty`
- Tech difficulty: Neolithic 0.80 → Archotech 2.00
- Success: `(0.50 + skill×0.025) / techDifficulty`
- Failure: 5% HP loss (minor) or 15% HP / quality drop (critical, below 50% HP)
- If item reaches 0 HP during repair, it is destroyed and partial materials are reclaimed

### Clean (M3)
- Cost: `baseCost × 0.15` (flat, HP-independent)
- Always succeeds; skill reduces work time
- Sets `Apparel.WornByCorpse = false` — public setter in RimWorld 1.6, no reflection required

## Key API Notes

### Material Cost Lookup
- `thing.def.CostListAdjusted(thing.Stuff)` — extension method in `CostListCalculator`, gives adjusted cost list accounting for stuff type
- `thing.def.smeltProducts` — additional products from smelting (e.g. steel from guns)
- `ThingDef.intricate` — vanilla field, true for components/advanced components

### Workbench Usability Check
- `((IBillGiver)bench).UsableForBillsAfterFueling()` — checks power and fuel. Use in `WorkGiver`.
- `((IBillGiver)bench).CurrentlyUsableForBills()` — checks power only. Use in `JobDriver` tick.

### Designation Management
- `map.designationManager.AnySpawnedDesignationOfDef(def)` — fast early-out in `WorkGiver.ShouldSkip`
- `map.designationManager.SpawnedDesignationsOfDef(def)` — iterate for work targets
- `map.designationManager.RemoveAllDesignationsOn(thing)` — clean up after job completes

### Work Speed
- `GeneralLaborSpeed` is the correct stat for non-recipe manual work (higher base values than `WorkSpeedGlobal`)
- `WorkTableWorkSpeedFactor` provides the bench multiplier
- `WorkSpeedGlobal` is too low for recycling — results in jobs taking 100+ seconds

### Taint
- `Apparel.WornByCorpse` — public getter and **public setter** in 1.6. No reflection needed.

### Quality Degradation
- `CompQuality.qualityInt` is **private** — use `AccessTools.Field(typeof(CompQuality), "qualityInt")` or `Traverse` to read/write for failure penalties.

## Settings

`Listing_Standard` UI with sliders for tuning values. Reset to defaults button. Uses `Verse.Mod` subclass for `ModSettings` registration — keep separate from `[StaticConstructorOnStartup]` class, never reference Defs from the `Mod` constructor.

## File Layout

```
RRRR\
├── About\About.xml
├── 1.6\
│   ├── Assemblies\          ← build output
│   ├── Defs\                ← JobDefs, WorkGiverDefs, DesignationDefs
│   └── Patches\             ← XML comp injection, Orders menu designators
├── Textures\
│   └── UI\Designators\      ← PNG files for DesignationDefs (MUST exist at load time)
├── Languages\English\Keyed\Keys.xml
└── Source\
    ├── RRRR.sln / .csproj
    ├── Setup.cs              ← [StaticConstructorOnStartup] — Harmony init + R4ThingDefCache trigger
    ├── Settings.cs           ← Verse.Mod subclass + ModSettings
    ├── Cache\                ← R4ThingDefCache (bench lists)
    ├── Comps\                ← CompRecyclable, CompProperties_Recyclable
    ├── Designators\          ← Designator_RecycleThing, (future: Repair, Clean)
    ├── Jobs\                 ← JobDrivers, WorkGivers
    ├── RecipeWorkers\        ← M4: bill-based workers
    ├── Utility\              ← MaterialUtility, WorkbenchRouter, SkillUtility
    ├── Filters\              ← SpecialThingFilterWorkers (M4)
    ├── Patches\              ← Harmony patches (ReverseDesignatorDatabase postfix)
    └── Defs\                 ← R4DefOf
```
