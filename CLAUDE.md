# RimWorld Mod — CLAUDE.md

This is the master reference for agents working on this RimWorld mod. Read it before doing anything. It is intentionally thin: the details live in `RIMWORLD_MODDING_REFERENCE.md`, `HARMONY_PATTERNS.md`, and the other reference docs. This file exists to give orientation, enforce hard rules, and route you to the right place to look.

---

## Quick Reference

- **Target**: RimWorld 1.6 · `net48` (NOT `net472`) · Harmony 2.4.x · C# 9 language features
- **Identity**: Package ID `CheaterEater.ModName`, Harmony ID `com.cheatereater.modname`, namespace matches assembly
- **Decompiled source**: `C:\Users\AMM\Documents\Github\Rimworld Mods\Rimworld_References\Rimworld 1.6 Decompiled Source\` — organised by namespace (`Verse\`, `RimWorld\`, `Verse.AI\`, `RimWorld.Planet\`)
- **Shared build props**: `C:\Users\AMM\Documents\Github\Rimworld Mods\RimWorld.Paths.props` — defines `$(RimWorldManaged)`, `$(HarmonyAssemblies)`; never committed
- **Harmony init**: `[StaticConstructorOnStartup]` class, separate from `Verse.Mod` subclass — mixing them silently fails
- **Dependency**: declare `brrainz.harmony` in `About.xml`; never bundle `0Harmony.dll`

---

## Hard Rules — Read Before Touching Code

Twelve rules whose violation silently corrupts saves, silently skips builds, or silently breaks other mods. Every agent checks this list first.

1. **Custom `Zone`, `WorldObject`, or other polymorphic subclasses in vanilla `LookMode.Deep` collections break saves on mod removal.** `ScribeExtractor` throws `Can't load abstract class` when the mod class can't be resolved and the fallback is an abstract base. This is the most dangerous save-compat failure mode in RimWorld modding — document it loudly in the README and tell users to remove all mod zones/objects before uninstalling. *See `RIMWORLD_MODDING_REFERENCE.md` → Save/Load Safety.*

2. **Never change `Scribe_*` field names or types without a back-compat path.** Old saves will NRE or silently lose data on load.

3. **Never reorder enum values.** Saved integers map to ordinal positions; reordering silently corrupts state.

4. **`SpecialThingFilterDef` requires `parentCategory`.** Omitting it causes NRE in `ThingCategoryNodeDatabase.FinalizeInit()` which cascades into hundreds of vanilla texture errors at startup. The textures are collateral, not the cause.

5. **`RecipeDef.defaultIngredientFilter` must be set alongside `fixedIngredientFilter`.** `Bill..ctor` calls `CopyAllowancesFrom(defaultIngredientFilter)` and NREs if null when the player opens the bill menu.

6. **Default to postfix. `return false` in a prefix is a last resort.** Cancelling prefixes are the #1 source of mod conflicts. Narrow conditions aggressively and document why in a comment above the patch.

7. **`[StaticConstructorOnStartup]` must be a separate class from the `Mod` subclass.** The `Mod` constructor fires before Defs exist; `[StaticConstructorOnStartup]` fires after. Mixing them causes silent initialization failures.

8. **Never bundle `0Harmony.dll`.** Declare `brrainz.harmony` as an `About.xml` dependency instead. Bundling creates version conflicts with other mods.

9. **Target `net48`, not `net472`.** `net472` causes `CS0246` type-not-found errors at build time.

10. **`AnyCPU` vs `Any CPU` mismatch in `.sln` silently skips the build.** Both sections must use the identical string. Check the solution file after any Visual Studio edit.

11. **Never write to `underGrid`.** It is vanilla's bridge/foundation contract. Synthetic data there blocks bridge placement with "there is an under terrain there". Use your own `MapComponent` array instead.

12. **Bench belongs in `TargetA`, never `TargetB`.** `ExtractNextTargetFromQueue(TargetIndex.B)` overwrites `job.targetB` on every iteration and destroys the bench reference.

Additional rules (less silent but still important): `TerrainAt()` returns temp overlays (flood water, thin ice) — use `BaseTerrainAt()` for underlying terrain. Always specify `Type[]` argument types on overloaded Harmony patch targets or you get `AmbiguousMatchException` at startup. Never access game state from background threads — queue to the main thread via `MapComponentTick`.

---

## Domain Concepts

Non-obvious concepts that appear throughout RimWorld modding. Read the relevant section before touching that area.

### Save Compatibility Gate
Every change that touches persistent state must pass four questions before it ships:
1. **Adds/removes/renames a `Scribe_*` field?** → Handle null on load; provide a back-compat converter for renames.
2. **Changes an enum's ordinal values?** → Never. New values go at the end only.
3. **Can the mod be added mid-save?** → Verify what breaks and is it recoverable. Usually safe for designation-only and comp-free mods; unsafe if it requires a `MapComponent` that must read state that doesn't exist.
4. **Can the mod be removed mid-save?** → Verify concretely. Custom `Zone`/`WorldObject` subclasses are the most dangerous case (rule #1). Custom `ThingComp` data on persistent items is lost permanently. Custom designations drop silently. Jobs fail and pawns get new ones. Bills with missing recipes need a `Building_WorkTable.SpawnSetup` postfix to strip them.

If any answer is "I don't know", the change is not ready. The Engineer agent owns this gate; the Foreman agent enforces it in work orders.

### Vanilla-First Principle
Before implementing anything, ask: does vanilla do this already? If yes, use it. If partial, extend it. Only reimplement when vanilla cannot do what the mod needs and a targeted Harmony patch cannot bridge the gap.

Concrete application: `JobDriver_DoBill.CollectIngredientsToils` before writing custom ingredient gathering; `recipeMaker.recipeUsers` before writing custom bench routing; vanilla `RecipeWorker` subclasses before writing a full pipeline; `GenTemperature.PushHeat` before writing custom heat distribution; `FreezeManager` before writing custom ice logic; `Widgets.*` helpers before writing custom GUI elements.

Vanilla is battle-tested over a decade. Assume it's correct until you have concrete evidence otherwise.

### Harmony Patch Hierarchy
Always use the safest option that works. In order: postfix → void prefix → bool prefix (returning false) → transpiler. Each step down is more fragile and more likely to conflict with other mods. See `HARMONY_PATTERNS.md` for full patterns and pitfalls.

### Def Loading Order
```
XML Defs load → XPath patches apply → C# assemblies load → ResolveReferences → [StaticConstructorOnStartup]
```
`Mod` constructor fires during assembly load (before Defs exist). `[StaticConstructorOnStartup]` fires after `ResolveReferences`. Dynamic Defs added via `DefDatabase<T>.Add()` in static ctors have skipped `ResolveReferences` — manually set cross-references and call `def.ResolveDefNameHash()` before adding.

### XML Comp Injection
Use the flat two-step `PatchOperationAdd` pattern for adding `<comps>` entries to defs. Never use `PatchOperationSequence`/`PatchOperationConditional` against `<comps>` nodes that may not exist — they half-apply and corrupt the def database. *See `RIMWORLD_MODDING_REFERENCE.md` → XML Pitfalls.*

### Terrain Grid Layers
Four layers in priority order: `tempGrid` (temporary overlays like flood water, thin ice), `topGrid` (placed terrain), `underGrid` (bridge/foundation reservation), `foundationGrid`. `TerrainAt(cell)` returns `tempGrid` first; use `BaseTerrainAt(cell)` for the real underlying terrain. Never write to `underGrid` — that's rule #11.

### Job Target Slots
`TargetA` = bench (stable, never overwritten). `TargetQueueA[0]` = primary item. `TargetB`/`TargetQueueB` = ingredient stacks (overwritten by `ExtractNextTargetFromQueue`). `TargetC` = placement cell. Never put the bench in `TargetB` — that's rule #12.

---

## Before You Touch X

| Task area | Read first |
|---|---|
| Any Harmony patch (new or modified) | `HARMONY_PATTERNS.md` + decompiled source of the target method |
| XML Def authoring or patching | `RIMWORLD_MODDING_REFERENCE.md` → XML Pitfalls |
| Bill / recipe / workbench work | `RIMWORLD_MODDING_REFERENCE.md` → RecipeDef Pitfalls + Bill System |
| JobDriver with ingredient gathering | `RIMWORLD_MODDING_REFERENCE.md` → Bill System / JobDriver Target Slots |
| Terrain, phase transitions, ice/water | `RIMWORLD_MODDING_REFERENCE.md` → Terrain Grid |
| Caravans, world-travel, pather | `RIMWORLD_MODDING_REFERENCE.md` → Caravan API |
| Map-wide grid overlays (temperature, pollution, custom) | `RIMWORLD_GRID_OVERLAYS.md` |
| Hotkeys, designators, command gizmos | `RIMWORLD_HOTKEYS_REFERENCE.md` |
| Save compatibility of any persistent-state change | `RIMWORLD_MODDING_REFERENCE.md` → Save/Load Safety + this file's Save Compatibility Gate |
| Mod settings / configuration UI | Vanilla `Listing_Standard` patterns; look at existing mods in this repo or in `C:\Users\AMM\Documents\Github\Rimworld Mods\` for examples |
| Mod-specific architecture, mechanics, formulas | `DESIGN.md` (this mod) + `MILESTONES.md` if present |

When a task crosses multiple areas, read them all before starting. Skipping the reference doc in favour of "I'll figure it out from the source" is how rule violations happen.

---

## MCP Tool Discipline

This project uses the `rimworld-source` MCP server for navigating decompiled vanilla code, and the `Filesystem` MCP for reading/writing mod files.

**`rimworld-source` usage:**
- `find_class` returns a path → read it with `read_source_file` using that path (format: `Namespace/ClassName.cs`)
- `find_method` always with `class_name` specified — unqualified searches are slow
- `search_symbol` for cross-cutting lookups (finding all callers of a method, all subclasses of a type)
- `list_source_tree` for orientation when exploring an unfamiliar namespace
- `mod_manage` must call `set_active` before any mod-directory read/write

**`Filesystem` fallback:** When MCP is unavailable, read decompiled source directly from `C:\Users\AMM\Documents\Github\Rimworld Mods\Rimworld_References\Rimworld 1.6 Decompiled Source\<Namespace>\<ClassName>.cs`.

**Mod file I/O:**
- `Filesystem:read_multiple_files` for batch reads (faster than sequential)
- `Filesystem:edit_file` requires exact whitespace/CRLF match — use `write_file` for full rewrites when uncertain
- `Filesystem:move_file` to append `.old` suffix instead of deleting (deletion is not available; `EnableDefaultCompileItems` picks up all `*.cs`, so `.old` suffix retires a file safely)
- Always use `encoding="utf-8"` for Windows file I/O

**Never guess API signatures.** If a patch targets `GenTemperature.TryGetTemperatureForCell`, read `Verse\GenTemperature.cs` first. The vanilla source is the authoritative reference.

---

## Anti-Patterns

Short form. Full details in `RIMWORLD_MODDING_REFERENCE.md` and `HARMONY_PATTERNS.md`.

- Static fields for state (no lifecycle, no save/load — use `ThingComp`/`MapComponent`/`WorldComponent`/`GameComponent`)
- Static ctor fields in the Harmony init class (triggers early initialization — keep it dedicated)
- Absolute paths in committed files (`.csproj`, `.sln`) — all machine-specific paths live in `RimWorld.Paths.props`
- Deleting `.cs` files instead of renaming to `.old` (risks duplicate compilation under `EnableDefaultCompileItems`)
- Swallowing exceptions silently — every caught exception must produce a log entry, even if the recovery path is a no-op
- Accessing game state from a background thread — queue to main thread via `MapComponentTick` or similar
- `modClass` in `About.xml` (unnecessary; RimWorld scans all assemblies for `Mod` subclasses automatically)
- Hardcoded `KeyCode` values instead of `KeyBindingDef.KeyDownEvent` (breaks player rebinding)
- `qta.icon()` at module level equivalent — in RimWorld, don't call `ContentFinder<Texture2D>.Get` from static field initialisers; use `[StaticConstructorOnStartup]` and cache in a class field

---

## Testing Procedure

No formal test suite. Integration testing is the primary verification.

1. **Build cleanly** — Visual Studio build (Ctrl+Shift+B) with zero errors and zero warnings in your own code. Ignore vanilla/Harmony warnings from referenced assemblies.
2. **Launch RimWorld** — the mod loads from the repo folder (symlinked into the game's `Mods\` folder).
3. **Check `Player.log`** — at `%APPDATA%\..\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`. Zero errors at startup is the bar. Warnings need to be understood, not ignored.
4. **Load a test save** — one with the mod active and one without, then add the mod mid-save. Verify no errors in either case. This is the save-compat check from the Hard Rules #1–#3.
5. **Exercise the feature** — in dev mode, spawn the relevant things/pawns and exercise the code path. Dev mode log window catches runtime errors that don't appear in `Player.log`.
6. **Test mid-save removal** if the mod may be uninstalled — load save, remove mod, reload, verify the save still loads and the removal behaves as documented (graceful degradation or documented breakage with a user-facing warning in README).

---

## Reference Documents

Read these for specifics. This file only routes.

- **`RIMWORLD_MODDING_REFERENCE.md`** — project conventions, init order, state management, save/load safety, XML pitfalls, RecipeDef, JobDriver slots, terrain grid, performance, caravan API, common errors
- **`HARMONY_PATTERNS.md`** — patch hierarchy, parameter injection, annotation patterns, conditional patching, re-entry guards, priority/ordering, `AccessTools`/`Traverse`/`FieldRefAccess`, debugging, common errors
- **`RIMWORLD_GRID_OVERLAYS.md`** — `ICellBoolGiver` + `CellBoolDrawer` pattern for map-wide overlays
- **`RIMWORLD_HOTKEYS_REFERENCE.md`** — `KeyBindingDef`, command/designator hotkey plumbing, `[DefOf]` pattern
- **`DESIGN.md`** — mod-specific architecture, mechanics, formulas
- **`MILESTONES.md`** (if present) — mod-specific progress log; condensed prose for completed milestones, full detail for in-progress/future

Agent definitions (Foreman, Builder, Engineer, Technician, Auditor, Reviewer, Clerk) live alongside this file and follow its rules.
