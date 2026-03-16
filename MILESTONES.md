# R⁴ — Milestones

## M1: Foundation + Recycle [IN PROGRESS]

### Completed
- [x] Project scaffold (directories, csproj, sln, About.xml, .gitignore)
- [x] Setup.cs — Harmony init
- [x] Settings.cs — RRRRSettings skeleton with recycle tuning values
- [x] CompRecyclable.cs — ThingComp with designation state, recycle gizmo, overlay, save/load
- [x] CompProperties_Recyclable.cs
- [x] MaterialUtility.cs — full recycle return calculation
- [x] WorkbenchRouter.cs — item → workbench routing
- [x] SkillUtility.cs — skill factor calculation
- [x] R4DefOf.cs — [DefOf] references for JobDefs
- [x] WorkGiver_R4.cs — scans for designated items, creates jobs
- [x] JobDriver_R4Recycle.cs — haul to bench, do work, spawn materials, destroy item
- [x] Patches.cs — fallback comp injection on ThingDef.ResolveReferences
- [x] SpecialThingFilters.cs — Tainted, Damaged, MarkedRecycle filters (stubs for M4)
- [x] XML: JobDefs, WorkGiverDefs, comp injection patches
- [x] XML: RecipeDef stubs for bill-based recycle (M4 full implementation)
- [x] Languages/English/Keyed/Keys.xml — all translation keys

### To Test
- [ ] Build compiles without errors
- [ ] Mod loads in-game without red errors
- [ ] Recycle gizmo appears on ground items (apparel + weapons)
- [ ] Clicking gizmo sets designation; clicking again clears it
- [ ] Pawn picks up designated item and hauls to correct workbench
- [ ] Work animation plays at bench
- [ ] Materials spawn on completion; item destroyed
- [ ] Material amounts match expected formula output
- [ ] Tainted items get penalty applied
- [ ] Save/load preserves designation state

---

## M2: Repair System [PLANNED]
- [ ] JobDriver_R4Repair — multi-cycle repair with material consumption
- [ ] Skill check per cycle with failure/critical failure outcomes
- [ ] Tech level difficulty multiplier
- [ ] Quality degradation on critical failure below 50% HP
- [ ] Repair gizmo on CompRecyclable
- [ ] Letter notification for quality loss
- [ ] Settings: repair tuning values

## M3: Clean System [PLANNED]
- [ ] JobDriver_R4Clean — taint removal
- [ ] Material consumption (flat fraction, HP-independent)
- [ ] Clean gizmo on CompRecyclable
- [ ] Settings: clean tuning values

## M4: Bill System [PLANNED]
- [ ] RecipeWorker_Recycle, RecipeWorker_Repair, RecipeWorker_Clean
- [ ] Full RecipeDef XML for all workbenches
- [ ] SpecialThingFilterWorker classes activated
- [ ] Bill filtering integration with gizmo marks

## M5: Polish [PLANNED]
- [ ] Full settings UI with all sections
- [ ] Tooltip improvements (expected returns/costs)
- [ ] Balance tuning pass
- [ ] Taint economy validation (clean+recycle ≤ direct tainted recycle)

## M6: Compatibility [PLANNED]
- [ ] MayRequire patches for popular mods
- [ ] Steam Workshop preparation
