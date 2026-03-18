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
| `CompRecyclable` | ThingComp on all weapons/apparel. Tracks R4 designation, provides gizmos |
| `WorkGiver_R4Recycle` | Scans for designated items, creates recycle jobs (Crafting work type) |
| `WorkGiver_R4Repair` | Scans for items needing repair near benches, creates repair jobs |
| `JobDriver_R4Recycle` | Haul item to bench → work → spawn materials → destroy item |
| `JobDriver_R4Repair` | Multi-cycle repair with skill checks (M2) |
| `JobDriver_R4Clean` | Taint removal (M3) |
| `MaterialUtility` | All material/cost calculations |
| `WorkbenchRouter` | Maps item type → valid workbench(es) using recipe presence, not hardcoded defNames |
| `SkillUtility` | Skill factor, repair success checks, failure severity |
| `R4ThingDefCache` | `[StaticConstructorOnStartup]` cache of repairable/recyclable ThingDefs and their material costs, built once at load |

### Harmony Patches (1 total)

| Target | Type | Purpose |
|---|---|---|
| `ReverseDesignatorDatabase.InitDesignators` | Postfix | Inject gizmo designators so they appear on selected items |

> **Removed:** The original design listed `ThingDef.ResolveReferences` as a fallback comp injection patch. XML `<comps>` injection via `PatchOperationAdd` (targeting apparel/weapon base ThingDefs) is the correct and sufficient approach. A Harmony patch on `ResolveReferences` is fragile and unnecessary.

### Designation Flow

**Designator (map-order):** Player clicks map-order designator → places `DesignationDef` on item → `WorkGiver_R4` scans `designationManager.SpawnedDesignationsOfDef(...)` in `PotentialWorkThingsGlobal` → pawn hauls to bench and works.

**Gizmo (direct select):** Select item → click gizmo on `CompRecyclable` → gizmo places designation → same WorkGiver flow handles it from there. Gizmos are added via `ReverseDesignatorDatabase.InitDesignators` postfix so they appear in the "select item" right-click context.

**Bills (automated, M4):** Standing bills at workbenches with custom `SpecialThingFilterWorker`s and `RecipeWorker` subclasses — deferred to milestone 4.

### Workbench Routing

Routing is determined by inspecting `ThingDef.AllRecipes` at startup (in `R4ThingDefCache`), not by hardcoded defName lists. This gives automatic compatibility with modded benches.

| Item Type | Criterion | Typical Result |
|---|---|---|
| Smeltable items | `thing.def.Smeltable` (has `smeltProducts` or non-intricate `CostListAdjusted`) | Benches with `SmeltWeapon` or `SmeltApparel` recipe |
| Textile/leather apparel | Non-smeltable apparel | Benches with `Make_Apparel_BasicShirt` or `Make_Apparel_TribalA` recipe |
| Fallback | All else | Crafting spot |

> **Changed:** The original routing table used item-type categories (melee, ranged, tech level) to pick between Smithy and Machining table. The reference mods show the practical approach is a two-bucket split: smeltable → smelt bench, non-smeltable → scrap/tailor bench. Finer routing can be added later but requires careful testing.

## XML Integration

### The Startup Crash: What Actually Happens

The observed crash pattern (confirmed by Player.log) is:

```
System.NullReferenceException at Verse.ThingCategoryNodeDatabase.FinalizeInit()
Caught exception while loading play data... Resetting mods config
```

Followed by hundreds of missing texture errors for entirely vanilla assets. **The textures are not the problem.** The `NullReferenceException` in `ThingCategoryNodeDatabase.FinalizeInit()` happens during Def loading and corrupts the Def database. After that point, `ResolveIcon()` runs on broken defs, `graphicData` fails to initialize, and RimWorld reports missing textures for everything. The secondary errors are `GenStuff.DefaultStuffFor` throwing `InvalidOperationException: Sequence contains no elements` — meaning it tried to find a default material for a buildable def and found none, which happens when `stuffCategories` has been mangled.

**The root cause is a broken XML patch, not missing textures.** Do not attempt to fix the texture errors directly.

### The Unsafe Comp Injection Pattern (What Broke It)

The previous version used a `PatchOperationSequence` / `PatchOperationConditional` chain to check for and inject `CompProperties_Recyclable`. This pattern is fragile because:

1. Many vanilla ThingDefs have no `<comps>` node at all — apparel, utility items, grenades, etc.
2. When the XPath in a `PatchOperationConditional` resolves to zero nodes, the patch system can leave defs in a half-applied state.
3. A partially-applied patch during Def loading can corrupt the `ThingCategoryNodeDatabase`, which crashes with exactly the `NullReferenceException` seen in the log.

### The Safe Two-Step Comp Injection Pattern

The correct approach is two flat, unconditional operations: first ensure `<comps>` exists, then inject the comp. No `PatchOperationSequence`, no `PatchOperationConditional`, no `PatchOperationTest`.

```xml
<Patch>
  <!-- Step 1: Create <comps> node on any matching def that lacks it -->
  <Operation Class="PatchOperationAdd">
    <xpath>Defs/ThingDef[
      (thingClass="Apparel"
       or thingClass="Gun"
       or thingClass="MeleeWeapon")
      and not(comps)
    ]</xpath>
    <value>
      <comps/>
    </value>
  </Operation>

  <!-- Step 2: Inject the comp, guarded against duplicates -->
  <Operation Class="PatchOperationAdd">
    <xpath>Defs/ThingDef[
      (thingClass="Apparel"
       or thingClass="Gun"
       or thingClass="MeleeWeapon")
      and not(comps/li[@Class="RRRR.CompProperties_Recyclable"])
    ]/comps</xpath>
    <value>
      <li Class="RRRR.CompProperties_Recyclable"/>
    </value>
  </Operation>
</Patch>
```

**Why `thingClass` and not `thingCategories`:** `thingClass` is a stable structural property — almost no mod changes it. `thingCategories` is frequently modified by other mods, and targeting it makes patches fragile to load-order interactions.

Each operation is atomic. If Step 1 finds no matching defs, it silently does nothing. If Step 2 finds a def that already has the comp (from another patch), it skips it. Neither can corrupt the Def database.

### Texture Paths for DesignationDefs

`DesignationDef.texturePath` is resolved from the `Textures/` folder of the mod, **without** the `Textures/` prefix in the path string. The PNG must exist at load time.

```xml
<!-- Correct — resolves to Textures/UI/Designators/R4RecycleDesignation.png -->
<DesignationDef>
  <defName>R4_Recycle</defName>
  <texturePath>UI/Designators/R4RecycleDesignation</texturePath>
  <targetType>Thing</targetType>
</DesignationDef>
```

### Injecting Designators into the Orders Menu

Use `PatchOperationAdd` on `DesignationCategoryDef[defName="Orders"]/specialDesignatorClasses`. This is safe and does not require a Harmony patch:

```xml
<Operation Class="PatchOperationAdd">
  <xpath>*/DesignationCategoryDef[defName="Orders"]/specialDesignatorClasses</xpath>
  <value>
    <li>RRRR.Designator_RecycleThing</li>
    <li>RRRR.Designator_RepairThing</li>
  </value>
</Operation>
```

The `ReverseDesignatorDatabase.InitDesignators` Harmony postfix is **also** needed to make designators appear as gizmos on selected items (the right-click context). These serve different UI surfaces and both are required.

### JobDef / WorkGiverDef

- `WorkTypeDef` and `WorkGiverDef` can live in the same XML file, or separate files — RimWorld does not care, but separate files per Def type is cleaner.
- `WorkGiverDef.workType` must exactly match the `WorkTypeDef.defName` in the same or another file.
- `allowOpportunisticPrefix` on `JobDef` enables pawns to pick up the job while passing by, which is appropriate for haul-then-work jobs like recycle.

## Formulas

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
- If item reaches 0 HP during repair, it is destroyed and partial materials are reclaimed (same reclaim logic as recycle, scaled to progress)

### Clean (M3)
- Cost: `baseCost × 0.15` (flat, HP-independent)
- Always succeeds; skill reduces work time
- Sets `Apparel.WornByCorpse = false` — this field has a **public setter** in RimWorld 1.6, no reflection required

## Key API Notes

### Material Cost Lookup
- `thing.def.CostListAdjusted(thing.Stuff)` — gives the adjusted cost list accounting for stuff type. This is the canonical way to get what an item cost to make.
- `thing.def.smeltProducts` — additional products that appear only on smelting (e.g. steel from guns). Should be included in recycle output.
- Do **not** use `GetStatValueAbstract(StatDefOf.Nutrition)` — unrelated. Use `CachedNutrition` for food only.

### Workbench Usability Check
- `((IBillGiver)bench).UsableForBillsAfterFueling()` — checks power and fuel. Use this in `WorkGiver` before creating a job.
- `((IBillGiver)bench).CurrentlyUsableForBills()` — checks power only, no fuel check. Use this in the `JobDriver` tick to abort if bench loses power mid-job.

### Designation Management
- `map.designationManager.AnySpawnedDesignationOfDef(def)` — fast early-out in `WorkGiver.ShouldSkip`.
- `map.designationManager.SpawnedDesignationsOfDef(def)` — iterate to get work targets in `PotentialWorkThingsGlobal`. Yield `item.target.Thing`.
- `map.designationManager.RemoveAllDesignationsOn(thing)` — clean up after job completes.

### Smeltable Check
- `thing.def.Smeltable` — vanilla property. True if the item has smelt products or non-intricate materials in its cost list. Used to route to smelter vs. tailor bench.

### Taint
- `Apparel.WornByCorpse` — public getter and **public setter** in 1.6. No reflection needed.

### Quality Degradation
- `CompQuality.qualityInt` is **private** — use `AccessTools.Field(typeof(CompQuality), "qualityInt")` or `Traverse` to read/write it for failure penalties.

## Settings

Comprehensive `Listing_Standard` UI with sliders for all tuning values, organized by section (General, Recycle, Repair, Clean). Reset to defaults button. Uses `Verse.Mod` subclass for `ModSettings` registration — keep separate from `[StaticConstructorOnStartup]` class, never reference Defs from the `Mod` constructor.

## File Layout

```
RRRR\
├── About\About.xml
├── 1.6\
│   ├── Assemblies\          ← build output
│   ├── Defs\                ← JobDefs, WorkGiverDefs, WorkTypeDef, DesignationDefs
│   └── Patches\             ← XML comp injection, Orders menu designators, WorkType integrations
├── Textures\
│   └── UI\Designators\      ← PNG files for DesignationDefs (MUST exist at load time)
├── Languages\English\Keyed\Keys.xml
└── Source\
    ├── RRRR.sln / .csproj
    ├── Setup.cs              ← [StaticConstructorOnStartup] — Harmony init + R4ThingDefCache trigger
    ├── Settings.cs           ← Verse.Mod subclass + ModSettings
    ├── Cache\                ← R4ThingDefCache (bench lists, repairable def sets)
    ├── Comps\                ← CompRecyclable, CompProperties_Recyclable
    ├── Designators\          ← Designator_RecycleThing, Designator_RepairThing
    ├── Jobs\                 ← JobDrivers, WorkGivers
    ├── RecipeWorkers\        ← M4: bill-based workers
    ├── Utility\              ← MaterialUtility, WorkbenchRouter, SkillUtility
    ├── Filters\              ← SpecialThingFilterWorkers
    ├── Patches\              ← Harmony patches (ReverseDesignatorDatabase postfix)
    └── Defs\                 ← R4DefOf
```
