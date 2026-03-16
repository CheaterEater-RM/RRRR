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
| `CompRecyclable` | ThingComp on all weapons/apparel. Tracks R4 designation, provides gizmos, renders overlay |
| `WorkGiver_R4` | Scans for designated items, creates jobs (Crafting work type) |
| `JobDriver_R4Recycle` | Haul item to bench → work → spawn materials → destroy item |
| `JobDriver_R4Repair` | Multi-cycle repair with skill checks (M2) |
| `JobDriver_R4Clean` | Taint removal (M3) |
| `MaterialUtility` | All material/cost calculations |
| `WorkbenchRouter` | Maps item type → valid workbench(es) |
| `SkillUtility` | Skill factor, repair success checks, failure severity |

### Harmony Patches (1 total)

| Target | Type | Purpose |
|---|---|---|
| `ThingDef.ResolveReferences` | Postfix | Fallback comp injection for modded items missed by XML |

### Workflows

**Gizmo (direct-to-job):** Select item → click gizmo → sets designation → `WorkGiver_R4` creates job → pawn hauls to bench and works.

**Bills (automated, M4):** Standing bills at workbenches with custom `SpecialThingFilterWorker`s and `RecipeWorker` subclasses.

### Workbench Routing

| Item Type | Workbench |
|---|---|
| Textile/leather apparel | Tailoring benches |
| Metal/plate apparel | Smithy, Machining table |
| Melee weapons (≤Medieval) | Smithy |
| Melee weapons (>Medieval) | Machining table |
| Ranged weapons | Machining table |
| Fallback | Crafting spot |

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
- **Clamped** to base cost; probabilistic rounding; minimum 1 guaranteed

### Repair (M2)
- Cycles of 10% maxHP per cycle
- Cost: `(cycleHP/maxHP) × baseCost × 1.2 × techDifficulty`
- Tech difficulty: Neolithic 0.80 → Archotech 2.00
- Success: `(0.50 + skill×0.025) / techDifficulty`
- Failure: 5% HP loss (minor) or 15% HP / quality drop (critical, below 50% HP)

### Clean (M3)
- Cost: `baseCost × 0.15` (flat, HP-independent)
- Always succeeds; skill reduces work time

## Key Decompiled Source Findings

- `Apparel.WornByCorpse` has public setter in 1.6 — no reflection needed
- `CompQuality.qualityInt` is private — use `AccessTools.Field` for degradation
- `GenRecipe.MakeRecipeProducts` → `ConsumeIngredients` execution order confirmed
- `RecipeWorker.Notify_IterationCompleted` receives pawn — usable for material spawning

## Settings

Comprehensive `Listing_Standard` UI with sliders for all tuning values, organized by section (General, Recycle, Repair, Clean). Reset to defaults button.

## File Layout

```
RRRR\
├── About\About.xml
├── 1.6\
│   ├── Assemblies\          ← build output
│   ├── Defs\                ← JobDefs, WorkGiverDefs, SpecialThingFilterDefs
│   └── Patches\             ← XML comp injection
├── Languages\English\Keyed\Keys.xml
└── Source\
    ├── RRRR.sln / .csproj
    ├── Setup.cs, Settings.cs
    ├── Comps\               ← CompRecyclable, CompProperties_Recyclable
    ├── Jobs\                ← JobDrivers, WorkGiver_R4
    ├── RecipeWorkers\       ← M4: bill-based workers
    ├── Utility\             ← MaterialUtility, WorkbenchRouter, SkillUtility
    ├── Filters\             ← SpecialThingFilterWorkers
    ├── Patches\             ← Harmony patches
    └── Defs\                ← R4DefOf
```
