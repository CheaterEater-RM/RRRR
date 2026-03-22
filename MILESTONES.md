# R⁴ — Milestones

## M0: Skeleton & XML Stability ✅

**Goal:** Prove the mod loads without crashing. No gameplay.

**Completed:**
- [x] About.xml, .csproj, .sln, .gitignore — standard template
- [x] DesignationDef `R4_Recycle` with placeholder texture
- [x] Safe two-step comp injection XML (apparel only via `thingClass`)
- [x] Orders menu designator injection via PatchOperationAdd
- [x] `Designator_RecycleThing` — places designation on weapons/apparel
- [x] `CompProperties_Recyclable` / `CompRecyclable` — minimal ThingComp
- [x] `R4DefOf` — [DefOf] for DesignationDef
- [x] `Patch_ReverseDesignatorDatabase` — postfix injects gizmo designator
- [x] `Setup.cs` — [StaticConstructorOnStartup] with diagnostic logging
- [x] Keyed translation strings for designator UI
- [x] Verified: mod loads, designator appears in Orders tab, designations placed on items

**Post-mortem of previous failed attempts (documented in REFERENCE_ADDENDUM.md):**
- Old version used `PatchOperationSequence`/`PatchOperationConditional` for comp injection → corrupted Def database on defs without `<comps>` node
- `SpecialThingFilterDef` entries with null `parentCategory` caused `NullReferenceException` in `ThingCategoryNodeDatabase.FinalizeInit()` — the exact crash site
- Harmony postfix on `ThingDef.ResolveReferences` was unnecessary and risky during loading

---

## M1: Recycle — Full Loop ✅

**Goal:** Player designates item → pawn hauls to bench → works → materials spawn → item destroyed.

**Completed:**
- [x] `JobDef` for `RRRR_Recycle`
- [x] `WorkGiverDef` for recycle (workType: Crafting)
- [x] `MaterialUtility` — full return formula with condition/quality/skill/taint/rare multipliers
  - `CostListAdjusted(thing.Stuff)` + `smeltProducts` as base
  - `GenMath.RoundRandom` for probabilistic rounding, minimum 1 guaranteed
  - `ThingDef.intricate` check for skip-components setting
- [x] `WorkbenchRouter` — routes smeltable → smelt benches, non-smeltable → apparel benches
- [x] `R4ThingDefCache` — startup recipe scan triggered via `RuntimeHelpers.RunClassConstructor`
- [x] `WorkGiver_R4Recycle` — scans designations, finds nearest usable bench, creates job
- [x] `JobDriver_R4Recycle` — goto → carry → bench → drop → work → produce → destroy
  - `TryMakePreToilReservations`: reserves both bench and item
  - `FailOnThingMissingDesignation` for clean abort on cancel
  - `ExposeData` for `workLeft`/`totalWork` (save mid-job)
  - Progress bar on work toil
- [x] `Settings.cs` — `Verse.Mod` subclass + `ModSettings` with global multiplier and intricate skip toggle
- [x] `R4DefOf` updated with `JobDef RRRR_Recycle`
- [x] `Setup.cs` updated with cache trigger and def verification
- [x] Translation keys for settings UI
- [x] Verified: designate → pawn hauls to bench → works → materials spawn → item destroyed

**Bugfixes applied after initial testing:**
- [x] Drag-select: `DesignateSingleCell` now designates ALL matching items per cell (was only first)
- [x] Work speed: reduced base work to 15% of WorkToMake (was 50%), clamped 400–2000, comparable to vanilla smelting
- [x] Work speed: switched from `WorkSpeedGlobal` to `GeneralLaborSpeed` stat for work rate

**Known limitations of M1:**
- Comp injection still targets apparel only (`thingClass="Apparel"`) — weapons use `ThingWithComps` which is too broad for XML targeting. Weapons work via designator (`def.IsWeapon` check) but don't have CompRecyclable. This is fine until M2+ when gizmos need the comp.
- No repair or clean designators yet
- Placeholder texture
- Debug logging still present

---

## M2: Repair

**Goal:** Multi-cycle repair with skill checks, failure mechanics, and material consumption.

**XML:**
- [ ] `JobDef` for `RRRR_Repair`
- [ ] `WorkGiverDef` for repair
- [ ] `DesignationDef` for `R4_Repair`
- [ ] Orders menu patch for repair designator

**C# — Designator:**
- [ ] `Designator_RepairThing` — only targets items below max HP

**C# — Job System:**
- [ ] `WorkGiver_R4Repair` — similar to recycle but checks `HitPoints < MaxHitPoints`
- [ ] `JobDriver_R4Repair`
  - Cycle-based: each cycle restores 10% maxHP
  - Material cost per cycle: `(cycleHP/maxHP) × baseCost × 1.2 × techDifficulty`
  - Skill check per cycle: success = `(0.50 + skill×0.025) / techDifficulty`
  - Failure: minor (5% HP loss) or critical (<50% HP: 15% HP loss + quality drop)
  - If HP reaches 0: destroy item, reclaim partial materials
  - Tech difficulty scaling: Neolithic 0.80 → Archotech 2.00
  - `ExposeData` for cycle progress

**C# — Utility:**
- [ ] `SkillUtility` — success chance calculation, failure severity rolls
- [ ] Quality degradation via `AccessTools.Field(typeof(CompQuality), "qualityInt")`

**Settings:**
- [ ] Repair section: tech difficulty multiplier, failure chance modifier

**Validation:**
- [ ] Repair a damaged item, verify HP restoration per cycle
- [ ] Trigger failures at low skill, verify HP loss / quality drop
- [ ] Destroy item via repair failure, verify partial material reclaim
- [ ] Save/load mid-repair

---

## M3: Clean (Taint Removal)

**Goal:** Remove corpse taint from apparel at a workbench, consuming materials.

**XML:**
- [ ] `JobDef` for `RRRR_Clean`
- [ ] `WorkGiverDef` for clean
- [ ] `DesignationDef` for `R4_Clean`
- [ ] Orders menu patch for clean designator

**C# — Designator:**
- [ ] `Designator_CleanThing` — only targets tainted apparel (`Apparel.WornByCorpse`)

**C# — Job System:**
- [ ] `WorkGiver_R4Clean` — checks `WornByCorpse` on apparel
- [ ] `JobDriver_R4Clean`
  - Flat cost: `baseCost × 0.15`
  - Always succeeds; skill reduces work time
  - Set `apparel.WornByCorpse = false` (public setter in 1.6)
  - Call `apparel.Notify_ColorChanged()` to force render update

**Settings:**
- [ ] Clean section: cost multiplier, work speed modifier

**Validation:**
- [ ] Designate tainted apparel for cleaning
- [ ] Verify taint removed after job completes
- [ ] Verify apparel color updates (no longer corpse-tinted)

---

## M4: Bill-Based Automation

**Goal:** Standing bills at workbenches with filters for automated batch processing.

**XML:**
- [ ] `SpecialThingFilterDef` entries (**all must have `<parentCategory>`!**)
  - Tainted, Damaged, MarkedRecycle, MarkedRepair, MarkedClean
- [ ] `RecipeDef` entries for bill-based recycle/repair/clean

**C# — Filters:**
- [ ] `SpecialThingFilterWorker` subclasses for each filter type

**C# — Recipe Workers:**
- [ ] `RecipeWorker` subclasses that redirect to custom JobDrivers

**Validation:**
- [ ] Create standing bill → pawns automatically process matching items
- [ ] Filter combinations work correctly
- [ ] Bills persist across save/load

---

## M5: Polish & Publishing

**Goal:** Final quality pass, proper art, and Steam Workshop readiness.

- [ ] Replace placeholder textures with proper icons
- [ ] Full settings UI with all tuning values and reset button
- [ ] Comprehensive translation keys
- [ ] Remove all debug logging
- [ ] README.md with features, install instructions, compatibility notes
- [ ] Steam Workshop description and preview image
- [ ] Test with popular mod combinations (CE, VE, etc.)
- [ ] Performance profiling with Dub's Performance Analyzer

---

## Lessons Learned

**XML safety rules (from the crash post-mortem):**
1. Never use `PatchOperationSequence`/`PatchOperationConditional` for comp injection — use flat two-step `PatchOperationAdd`
2. Every `SpecialThingFilterDef` **must** have a `<parentCategory>` — null crashes `ThingCategoryNodeDatabase.FinalizeInit()`
3. Target `thingClass` (stable) not `thingCategories` (fragile, other mods modify it)
4. No Harmony patches on `ThingDef.ResolveReferences` for comp injection — XML is sufficient
5. Test XML changes in isolation before adding C# complexity

**Weapon comp injection:**
6. Vanilla weapons use `thingClass="ThingWithComps"`, same as hundreds of other things — cannot safely target by thingClass in XML. The designator works without the comp (checks `def.IsWeapon` directly). Comp injection for weapons will need a different approach when gizmos are added in M2+ (likely C# startup injection after Defs load, in `[StaticConstructorOnStartup]`).

**Work speed tuning:**
7. `WorkToMake * 0.5` is way too much work for recycling — a bolt-action rifle (12000 WorkToMake) would take 6000 ticks. Use 15% with a 400–2000 clamp to keep it comparable to vanilla smelting (flat 1600).
8. `GeneralLaborSpeed` is the correct stat for non-recipe manual work, not `WorkSpeedGlobal` (which is lower).
