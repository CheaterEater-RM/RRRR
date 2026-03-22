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

**Known limitations of M0:**
- Comp injection targets `thingClass="Apparel"` only — weapons not yet covered
- Designating an item does nothing (no WorkGiver/JobDriver to act on it)
- Placeholder texture (green rings) — not final art
- Debug logging still present

---

## M1: Recycle — Full Loop

**Goal:** Player designates item → pawn hauls to bench → works → materials spawn → item destroyed.

**XML:**
- [ ] Expand comp injection to weapons (verify `thingClass` values used by vanilla guns/melee)
- [ ] `JobDef` for `RRRR_Recycle`
- [ ] `WorkGiverDef` for recycle (workType: Crafting)
- [ ] Add repair/clean DesignationDefs (texture placeholders OK for now)

**C# — Utility Layer:**
- [ ] `MaterialUtility` — `GetRecycleReturn(thing, skillLevel)` using the formula from DESIGN.md
  - `CostListAdjusted(thing.Stuff)` + `smeltProducts` as base
  - Condition, quality, skill, taint, rare material multipliers
  - `GenMath.RoundRandom` for probabilistic rounding, minimum 1
- [ ] `WorkbenchRouter` — scan `DefDatabase<ThingDef>` at startup for bench lists
  - Smeltable → benches with `SmeltWeapon`/`SmeltApparel` recipe
  - Non-smeltable → benches with `Make_Apparel_BasicShirt` recipe
  - Fallback → crafting spot
- [ ] `R4ThingDefCache` — static constructor triggered from Setup.cs via `RuntimeHelpers.RunClassConstructor`

**C# — Job System:**
- [ ] `WorkGiver_R4Recycle`
  - `ShouldSkip`: early-out via `AnySpawnedDesignationOfDef`
  - `PotentialWorkThingsGlobal`: iterate `SpawnedDesignationsOfDef(R4_Recycle)`
  - `HasJobOnThing`: find closest reachable bench via `GenClosest.ClosestThingReachable`
  - Check `UsableForBillsAfterFueling()` on bench
- [ ] `JobDriver_R4Recycle`
  - Toil sequence: reserve item → goto → carry → goto bench → place → work → produce → destroy
  - `TryMakePreToilReservations`: reserve both bench and item
  - Work toil with `tickIntervalAction`, pawn skill × bench speed factor
  - `ExposeData` for `workLeft`/`totalNeededWork` (save mid-job)
  - Completion: spawn materials at pawn position, destroy item, remove designation
  - `FailOnThingMissingDesignation` for clean abort on cancel

**C# — Settings (basic):**
- [ ] `Settings.cs` — `Verse.Mod` subclass + `ModSettings`
- [ ] Global recycle multiplier slider
- [ ] Toggle: skip intricate components (components, adv. components)

**Validation:**
- [ ] Drop a weapon/apparel on the ground
- [ ] Designate it for recycling
- [ ] Observe pawn haul it to a bench, work, and produce materials
- [ ] Verify material counts match expected formula output
- [ ] Save/load mid-job — verify work progress persists
- [ ] Cancel designation mid-job — verify pawn aborts cleanly
- [ ] Remove debug logging from M0

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
