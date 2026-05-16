using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Startup cache that builds dynamic ThingFilters for each RRRR bill recipe
    /// by inverting the recipeMaker.recipeUsers relationship, expanding VEF bench
    /// aliases, and auto-detecting modded workbenches to inject R4 bill coverage
    /// and strip smelter recipes.
    ///
    /// MOD COMPATIBILITY:
    ///
    /// Smelter recipe stripping:
    ///   Any ThingDef whose recipe list contains SmeltWeapon, SmeltApparel, or
    ///   SmeltOrDestroyThing has those removed at startup. This is done in code
    ///   rather than XML so it catches all smelters regardless of mod, not just
    ///   the vanilla ElectricSmelter. The SpawnSetup patch cleans up any
    ///   pre-existing saved bills on these benches.
    ///
    /// New workbench detection:
    ///   After the cache is built, benches in BenchCraftables are checked for
    ///   missing R4 recipe coverage and missing custom bill WorkGivers. This lets
    ///   modded benches inherit existing R4 recipes (for example via VEF) while
    ///   still receiving the repair/clean bill WorkGivers those recipes require.
    ///
    /// The approach relies on two sources of bench-item relationships:
    ///   1. item.recipeMaker.recipeUsers  — items listing their benches (primary)
    ///   2. recipe.AllRecipeUsers         — recipes listing benches directly OR
    ///      benches owning the recipe in ThingDef.recipes (secondary, matches vanilla)
    /// </summary>
    public static class R4WorkbenchFilterCache
    {
        // ── Public data ────────────────────────────────────────────────────────

        /// <summary>
        /// bench ThingDef → set of R4-routable gear items it can craft.
        /// This is the broad bench-routing superset; per-operation filters narrow
        /// it further for recycle, repair, and clean bills.
        /// </summary>
        public static readonly Dictionary<ThingDef, HashSet<ThingDef>> BenchCraftables
            = new Dictionary<ThingDef, HashSet<ThingDef>>();

        /// <summary>bench ThingDef → canonical WorkTypeDef servicing it.</summary>
        public static readonly Dictionary<ThingDef, WorkTypeDef> BenchWorkTypes
            = new Dictionary<ThingDef, WorkTypeDef>();

        private static readonly HashSet<ThingDef> ExplicitlyExcludedItems
            = new HashSet<ThingDef>();

        // Vanilla smelter recipes we always strip from any bench that has them.
        private static readonly HashSet<string> SmelterRecipesToStrip = new HashSet<string>
        {
            "SmeltWeapon",
            "SmeltApparel",
            "SmeltOrDestroyThing",
        };

        // ── Entry point ────────────────────────────────────────────────────────

        static R4WorkbenchFilterCache()
        {
            Build();
        }

        public static void Build()
        {
            BenchCraftables.Clear();
            BenchWorkTypes.Clear();
            ExplicitlyExcludedItems.Clear();
            WorkbenchRouter.Reset();

            BuildEligibilityExclusions();
            StripSmelterRecipes();
            BuildBenchWorkTypes();
            ExpandVEFInheritedBenches();
            BuildFromRecipeMakers();
            BuildFromRecipeSideUsers();
            AssignFallbacks();
            UnionVEFAliasedBenchCraftables();
            InjectModdedBenchBills();
            PatchRecipeFilters();
            WorkbenchRouter.BuildFallbackCache();

            R4Log.Debug($"Cache built: {BenchCraftables.Count} benches, " +
                        $"{BenchCraftables.Values.Sum(s => s.Count)} item-bench mappings.");
        }

        static void BuildEligibilityExclusions()
        {
            foreach (R4EligibilityExclusionDef exclusionDef in DefDatabase<R4EligibilityExclusionDef>.AllDefsListForReading)
            {
                if (exclusionDef.excludedThingDefs == null) continue;

                for (int i = 0; i < exclusionDef.excludedThingDefs.Count; i++)
                {
                    ThingDef thingDef = exclusionDef.excludedThingDefs[i];
                    if (thingDef != null)
                        ExplicitlyExcludedItems.Add(thingDef);
                }
            }

            if (ExplicitlyExcludedItems.Count > 0)
                R4Log.Debug($"Loaded {ExplicitlyExcludedItems.Count} explicit R4 eligibility exclusion(s).");
        }

        // ── Step -1: strip smelter recipes from all benches ───────────────────

        /// <summary>
        /// Removes SmeltWeapon/SmeltApparel/SmeltOrDestroyThing from every bench
        /// that has them — not just ElectricSmelter. This replaces the XML patch
        /// approach so any modded smelter is also covered.
        /// </summary>
        static void StripSmelterRecipes()
        {
            int removed = 0;
            foreach (ThingDef bench in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (bench.recipes == null || bench.recipes.Count == 0) continue;

                for (int i = bench.recipes.Count - 1; i >= 0; i--)
                {
                    RecipeDef recipe = bench.recipes[i];
                    if (recipe == null) continue;
                    if (SmelterRecipesToStrip.Contains(recipe.defName))
                    {
                        bench.recipes.RemoveAt(i);
                        removed++;
                        R4Log.Debug($"Stripped {recipe.defName} from {bench.defName}");
                    }
                }
            }

            if (removed > 0)
                R4Log.Debug($"Stripped {removed} smelter recipe(s) across all benches.");
        }

        // ── Step 0: bench → WorkTypeDef map ───────────────────────────────────

        static void BuildBenchWorkTypes()
        {
            var vanillaType = typeof(WorkGiver_DoBill);

            // Pass 1: vanilla WorkGiver_DoBill only (priority)
            foreach (WorkGiverDef wg in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (wg.giverClass != vanillaType) continue;
                if (wg.fixedBillGiverDefs == null || wg.fixedBillGiverDefs.Count == 0) continue;
                if (wg.workType == null) continue;
                foreach (ThingDef bench in wg.fixedBillGiverDefs)
                    if (!BenchWorkTypes.ContainsKey(bench))
                        BenchWorkTypes[bench] = wg.workType;
            }

            // Pass 2: modded WorkGivers — fill gaps only
            foreach (WorkGiverDef wg in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (wg.giverClass == vanillaType) continue;
                if (wg.fixedBillGiverDefs == null || wg.fixedBillGiverDefs.Count == 0) continue;
                if (wg.workType == null) continue;
                foreach (ThingDef bench in wg.fixedBillGiverDefs)
                    if (!BenchWorkTypes.ContainsKey(bench))
                        BenchWorkTypes[bench] = wg.workType;
            }

            R4Log.Debug($"BenchWorkTypes: {BenchWorkTypes.Count} benches mapped.");
        }

        static void ExpandVEFInheritedBenches()
        {
            Type extType = GenTypes.GetTypeInAnyAssembly("VEF.Buildings.RecipeInheritanceExtension");
            if (extType == null)
                return;

            var field = AccessTools.Field(extType, "inheritRecipesFrom");
            if (field == null)
            {
                R4Log.Warn("VEF.Buildings.RecipeInheritanceExtension found but 'inheritRecipesFrom' field is missing. VEF alias expansion skipped.");
                return;
            }

            int registered = 0;
            foreach (ThingDef bench in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (bench.modExtensions == null || bench.modExtensions.Count == 0)
                    continue;

                for (int i = 0; i < bench.modExtensions.Count; i++)
                {
                    DefModExtension ext = bench.modExtensions[i];
                    if (ext == null || !extType.IsInstanceOfType(ext))
                        continue;

                    if (!(field.GetValue(ext) is IEnumerable<ThingDef> sourceBenches))
                        continue;

                    foreach (ThingDef sourceBench in sourceBenches)
                    {
                        if (!WorkbenchRouter.RegisterAlias(sourceBench, bench))
                            continue;

                        registered++;
                        R4Log.Debug($"VEF alias: {bench.defName} inherits from {sourceBench.defName}");
                    }
                }
            }

            if (registered > 0)
                R4Log.Debug($"Registered {registered} VEF bench inheritance alias(es).");
        }

        // ── Step 1: invert item.recipeMaker.recipeUsers ────────────────────────

        static void BuildFromRecipeMakers()
        {
            foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!IsR4Eligible(item)) continue;
                if (item.recipeMaker?.recipeUsers == null) continue;

                foreach (ThingDef bench in item.recipeMaker.recipeUsers)
                {
                    if (!BenchCraftables.TryGetValue(bench, out HashSet<ThingDef> set))
                        BenchCraftables[bench] = set = new HashSet<ThingDef>();
                    set.Add(item);
                }
            }
        }

        // ── Step 1b: invert recipe-side bench ownership ───────────────────────

        /// <summary>
        /// Secondary pass: scan all RecipeDefs and use the same bench ownership
        /// semantics vanilla exposes through RecipeDef.AllRecipeUsers. This catches:
        ///   1. recipes that list benches directly in recipe.recipeUsers
        ///   2. benches that own the recipe via ThingDef.recipes
        /// We look at the produced item of each recipe to find eligible gear.
        /// </summary>
        static void BuildFromRecipeSideUsers()
        {
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                // Only care about recipes that produce a single eligible thing
                ThingDef product = recipe.ProducedThingDef;
                if (product == null || !IsR4Eligible(product)) continue;

                foreach (ThingDef bench in AllRecipeUsers(recipe))
                {
                    if (!BenchCraftables.TryGetValue(bench, out HashSet<ThingDef> set))
                        BenchCraftables[bench] = set = new HashSet<ThingDef>();
                    set.Add(product);
                }
            }
        }

        // ── Step 2: fallback for items with no recipeMaker ────────────────────

        static void AssignFallbacks()
        {
            var covered     = new HashSet<ThingDef>(BenchCraftables.Values.SelectMany(s => s));
            var fallbackMap = BuildFallbackMap();
            int count       = 0;

            foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!IsR4Eligible(item)) continue;
                if (covered.Contains(item)) continue;

                ThingDef bench = GetFallbackBench(item, fallbackMap);
                if (bench == null) continue;

                if (!BenchCraftables.TryGetValue(bench, out HashSet<ThingDef> set))
                    BenchCraftables[bench] = set = new HashSet<ThingDef>();
                set.Add(item);
                count++;

                R4Log.Debug($"Fallback → {item.defName} (tech={item.techLevel}) → {bench.defName}");
            }

            R4Log.Debug($"{count} items assigned via techLevel fallback.");
        }

        static void UnionVEFAliasedBenchCraftables()
        {
            if (!WorkbenchRouter.HasAliases)
                return;

            int propagated = 0;

            foreach (KeyValuePair<ThingDef, List<ThingDef>> kvp in WorkbenchRouter.GetAllAliases())
            {
                ThingDef sourceBench = kvp.Key;
                if (!BenchCraftables.TryGetValue(sourceBench, out HashSet<ThingDef> sourceItems))
                    continue;

                List<ThingDef> aliasBenches = kvp.Value;
                for (int i = 0; i < aliasBenches.Count; i++)
                {
                    ThingDef aliasBench = aliasBenches[i];
                    if (!BenchCraftables.TryGetValue(aliasBench, out HashSet<ThingDef> aliasItems))
                        BenchCraftables[aliasBench] = aliasItems = new HashSet<ThingDef>();

                    int before = aliasItems.Count;
                    aliasItems.UnionWith(sourceItems);
                    propagated += aliasItems.Count - before;
                }
            }

            if (propagated > 0)
                R4Log.Debug($"VEF alias union: {propagated} item-bench mapping(s) propagated.");
        }

        static Dictionary<TechLevel, ThingDef> BuildFallbackMap()
        {
            ThingDef craftingSpot   = DefDatabase<ThingDef>.GetNamedSilentFail("CraftingSpot");
            ThingDef fueledSmithy   = DefDatabase<ThingDef>.GetNamedSilentFail("FueledSmithy");
            ThingDef electricSmithy = DefDatabase<ThingDef>.GetNamedSilentFail("ElectricSmithy");
            ThingDef machining      = DefDatabase<ThingDef>.GetNamedSilentFail("TableMachining");
            ThingDef fabrication    = DefDatabase<ThingDef>.GetNamedSilentFail("FabricationBench");
            ThingDef smithy         = electricSmithy ?? fueledSmithy;

            return new Dictionary<TechLevel, ThingDef>
            {
                { TechLevel.Animal,     craftingSpot },
                { TechLevel.Neolithic,  craftingSpot },
                { TechLevel.Medieval,   smithy       },
                { TechLevel.Industrial, machining    },
                { TechLevel.Spacer,     fabrication  },
                { TechLevel.Ultra,      fabrication  },
                { TechLevel.Archotech,  fabrication  },
                { TechLevel.Undefined,  machining    },
            };
        }

        static ThingDef GetFallbackBench(ThingDef item, Dictionary<TechLevel, ThingDef> map)
        {
            if (map.TryGetValue(item.techLevel, out ThingDef bench) && bench != null)
                return bench;
            return DefDatabase<ThingDef>.GetNamedSilentFail("TableMachining");
        }

        // ── Step 2b: inject R4 bills onto modded benches ──────────────────────

        /// <summary>
        /// For any bench in BenchCraftables, ensure the full R4 bill surface is
        /// present: repair/recycle recipes, clean when apparel is relevant, and the
        /// custom repair/clean bill WorkGivers required by the non-vanilla paths.
        ///
        /// This means mods adding new weapon/apparel workbenches automatically
        /// get R4 bill support without requiring explicit compatibility patches.
        /// </summary>
        static void InjectModdedBenchBills()
        {
            var repairRecipeBenches = new HashSet<ThingDef>();
            var recycleRecipeBenches = new HashSet<ThingDef>();
            var cleanRecipeBenches = new HashSet<ThingDef>();
            var repairBillWorkGiverBenches = new HashSet<ThingDef>();
            var cleanBillWorkGiverBenches = new HashSet<ThingDef>();
            var skippedBenches = new List<string>();

            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                HashSet<ThingDef> coveredBenches = null;
                if (recipe.workerClass == typeof(RecipeWorker_R4Repair))
                    coveredBenches = repairRecipeBenches;
                else if (recipe.workerClass == typeof(RecipeWorker_R4Recycle))
                    coveredBenches = recycleRecipeBenches;
                else if (recipe.workerClass == typeof(RecipeWorker_R4Clean))
                    coveredBenches = cleanRecipeBenches;

                if (coveredBenches == null)
                    continue;

                foreach (ThingDef bench in AllRecipeUsers(recipe))
                    coveredBenches.Add(bench);
            }

            foreach (WorkGiverDef workGiver in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (workGiver.fixedBillGiverDefs == null || workGiver.fixedBillGiverDefs.Count == 0)
                    continue;

                HashSet<ThingDef> coveredBenches = null;
                if (workGiver.giverClass == typeof(WorkGiver_R4RepairBill))
                    coveredBenches = repairBillWorkGiverBenches;
                else if (workGiver.giverClass == typeof(WorkGiver_R4CleanBill))
                    coveredBenches = cleanBillWorkGiverBenches;

                if (coveredBenches == null)
                    continue;

                for (int i = 0; i < workGiver.fixedBillGiverDefs.Count; i++)
                    coveredBenches.Add(workGiver.fixedBillGiverDefs[i]);
            }

            // Find template RecipeDefs to clone
            RecipeDef repairTemplate  = DefDatabase<RecipeDef>.GetNamedSilentFail("RRRR_Repair_Machining");
            RecipeDef recycleTemplate = DefDatabase<RecipeDef>.GetNamedSilentFail("RRRR_Recycle_Machining");
            RecipeDef cleanTemplate   = DefDatabase<RecipeDef>.GetNamedSilentFail("RRRR_Clean_CraftingSpot");

            if (repairTemplate == null || recycleTemplate == null || cleanTemplate == null)
            {
                R4Log.Warn("Could not find R4 template recipes for modded bench injection.");
                return;
            }

            int touchedBenches = 0;

            foreach (var kvp in BenchCraftables)
            {
                ThingDef bench = kvp.Key;

                // Only inject onto actual workbenches with a bills tab
                if (bench.thingClass == null ||
                    !typeof(Building_WorkTable).IsAssignableFrom(bench.thingClass) ||
                    bench.inspectorTabs == null ||
                    !bench.inspectorTabs.Contains(typeof(ITab_Bills)))
                {
                    skippedBenches.Add(bench.defName);
                    continue;
                }

                if (bench.recipes == null)
                    bench.recipes = new List<RecipeDef>();

                bool hasClean = kvp.Value.Any(IsCleanEligible);
                string safeName = bench.defName.Replace(" ", "_");
                bool changed = false;

                RecipeDef repairRecipe = null;
                RecipeDef recycleRecipe = null;
                RecipeDef cleanRecipe = null;
                WorkGiverDef repairWorkGiver = null;
                WorkGiverDef cleanWorkGiver = null;

                if (!repairRecipeBenches.Contains(bench))
                {
                    repairRecipe = InjectRecipeDef(repairTemplate, $"RRRR_Repair_Mod_{safeName}", bench, bench.recipes);
                    repairRecipeBenches.Add(bench);
                    changed = true;
                }

                if (!recycleRecipeBenches.Contains(bench))
                {
                    recycleRecipe = InjectRecipeDef(recycleTemplate, $"RRRR_Recycle_Mod_{safeName}", bench, bench.recipes);
                    recycleRecipeBenches.Add(bench);
                    changed = true;
                }

                if (hasClean && !cleanRecipeBenches.Contains(bench))
                {
                    cleanRecipe = InjectRecipeDef(cleanTemplate, $"RRRR_Clean_Mod_{safeName}", bench, bench.recipes);
                    cleanRecipeBenches.Add(bench);
                    changed = true;
                }

                // Recycle intentionally stays on the bench's existing DoBill-compatible
                // WorkGiver. Only repair and clean need custom bill WorkGivers because
                // they bypass vanilla's normal bill execution path.
                // Inject custom WorkGiverDefs for the bill pipeline where needed.
                if (!repairBillWorkGiverBenches.Contains(bench))
                {
                    repairWorkGiver = InjectRepairBillWorkGiver(bench, safeName);
                    repairBillWorkGiverBenches.Add(bench);
                    changed = true;
                }

                if (hasClean && !cleanBillWorkGiverBenches.Contains(bench))
                {
                    cleanWorkGiver = InjectCleanBillWorkGiver(bench, safeName);
                    cleanBillWorkGiverBenches.Add(bench);
                    changed = true;
                }

                ValidateInjectedRecipeDef(bench, repairRecipe);
                ValidateInjectedRecipeDef(bench, recycleRecipe);
                ValidateInjectedRecipeDef(bench, cleanRecipe);
                ValidateInjectedWorkGiverDef(bench, repairWorkGiver);
                ValidateInjectedWorkGiverDef(bench, cleanWorkGiver);

                if (!changed)
                    continue;

                R4Log.Debug($"Ensured R4 bill coverage on modded bench: {bench.defName}");
                touchedBenches++;
            }

            if (touchedBenches > 0)
                R4Log.Debug($"Ensured R4 bill coverage on {touchedBenches} modded bench(es).");

            if (skippedBenches.Count > 0)
            {
                string preview = string.Join(", ", skippedBenches.Take(10));
                if (skippedBenches.Count > 10)
                    preview += ", ...";

                R4Log.Warn(
                    $"Skipped dynamic R4 bill injection on {skippedBenches.Count} bench(es) that do not satisfy Building_WorkTable + ITab_Bills prerequisites: {preview}");
            }
        }

        static RecipeDef InjectRecipeDef(RecipeDef template, string defName, ThingDef bench, List<RecipeDef> benchRecipes)
        {
            // Deep-copy ingredients to avoid mutating the template's IngredientCount objects.
            // PatchRecipeFilters sets recipe.ingredients[0].filter on each clone independently;
            // sharing the template list would corrupt all clones and the template itself.
            List<IngredientCount> clonedIngredients = null;
            if (template.ingredients != null)
            {
                clonedIngredients = new List<IngredientCount>(template.ingredients.Count);
                foreach (IngredientCount ing in template.ingredients)
                {
                    var copy = new IngredientCount();
                    copy.SetBaseCount(ing.GetBaseCount());
                    copy.filter = new ThingFilter(); // rebuilt by PatchRecipeFilters
                    clonedIngredients.Add(copy);
                }
            }

            var recipe = new RecipeDef
            {
                defName                 = defName,
                label                   = template.label,
                description             = template.description,
                jobString               = template.jobString,
                workAmount              = template.workAmount,
                workSpeedStat           = template.workSpeedStat,
                workSkill               = template.workSkill,
                effectWorking           = template.effectWorking,
                soundWorking            = template.soundWorking,
                workerClass             = template.workerClass,
                requiredGiverWorkType   = template.requiredGiverWorkType,
                ingredients             = clonedIngredients ?? new List<IngredientCount>(),
                fixedIngredientFilter   = new ThingFilter(), // rebuilt by PatchRecipeFilters
                defaultIngredientFilter = new ThingFilter(), // must be non-null; synced by PatchRecipeFilters
                recipeUsers             = new List<ThingDef> { bench },
            };

            recipe.ResolveDefNameHash();
            DefDatabase<RecipeDef>.Add(recipe);
            recipe.ResolveReferences();

            // Also add directly to the bench's recipes list
            if (benchRecipes != null && !benchRecipes.Contains(recipe))
                benchRecipes.Add(recipe);

            return recipe;
        }

        static WorkGiverDef InjectRepairBillWorkGiver(ThingDef bench, string safeName)
        {
            // Determine work type from our cache, fall back to Crafting
            if (!BenchWorkTypes.TryGetValue(bench, out WorkTypeDef workType) || workType == null)
                workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");

            var wg = new WorkGiverDef
            {
                defName            = $"RRRR_RepairBill_Mod_{safeName}",
                label              = $"repair items at {bench.label}",
                giverClass         = typeof(WorkGiver_R4RepairBill),
                workType           = workType,
                priorityInType     = 52,
                fixedBillGiverDefs = new List<ThingDef> { bench },
                verb               = "repair at",
                gerund             = $"repairing items at {bench.label}",
                prioritizeSustains = true,
            };

            // Only add Manipulation if the def exists — GetNamedSilentFail can return null
            // in unusual modded environments, and a null entry would cause NREs at runtime.
            var manipulation = DefDatabase<PawnCapacityDef>.GetNamedSilentFail("Manipulation");
            if (manipulation != null)
                wg.requiredCapacities = new List<PawnCapacityDef> { manipulation };

            wg.ResolveDefNameHash();
            DefDatabase<WorkGiverDef>.Add(wg);
            wg.ResolveReferences();
            RegisterDynamicWorkGiver(workType, wg);
            return wg;
        }

        static WorkGiverDef InjectCleanBillWorkGiver(ThingDef bench, string safeName)
        {
            if (!BenchWorkTypes.TryGetValue(bench, out WorkTypeDef workType) || workType == null)
                workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");

            var wg = new WorkGiverDef
            {
                defName            = $"RRRR_CleanBill_Mod_{safeName}",
                label              = $"clean apparel at {bench.label}",
                giverClass         = typeof(WorkGiver_R4CleanBill),
                workType           = workType,
                priorityInType     = 47,
                fixedBillGiverDefs = new List<ThingDef> { bench },
                verb               = "clean at",
                gerund             = $"cleaning apparel at {bench.label}",
                prioritizeSustains = true,
            };

            var manipulation = DefDatabase<PawnCapacityDef>.GetNamedSilentFail("Manipulation");
            if (manipulation != null)
                wg.requiredCapacities = new List<PawnCapacityDef> { manipulation };

            wg.ResolveDefNameHash();
            DefDatabase<WorkGiverDef>.Add(wg);
            wg.ResolveReferences();
            RegisterDynamicWorkGiver(workType, wg);
            return wg;
        }

        static void RegisterDynamicWorkGiver(WorkTypeDef workType, WorkGiverDef workGiver)
        {
            if (workType == null || workGiver == null)
                return;

            if (workType.workGiversByPriority == null)
                workType.workGiversByPriority = new List<WorkGiverDef>();

            // WorkTypeDef.ResolveReferences() only runs during initial def loading,
            // so dynamically added WorkGiverDefs must be inserted into the runtime
            // priority list manually or pawns will never consider them.
            if (workType.workGiversByPriority.Contains(workGiver))
                return;

            int insertAt = workType.workGiversByPriority.Count;
            for (int i = 0; i < workType.workGiversByPriority.Count; i++)
            {
                WorkGiverDef existing = workType.workGiversByPriority[i];
                if (existing == null || existing.priorityInType < workGiver.priorityInType)
                {
                    insertAt = i;
                    break;
                }
            }

            workType.workGiversByPriority.Insert(insertAt, workGiver);
            R4Log.Debug($"Registered dynamic WorkGiver {workGiver.defName} into {workType.defName} at priority {workGiver.priorityInType}.");
        }

        static void ValidateInjectedRecipeDef(ThingDef bench, RecipeDef recipe)
        {
            if (recipe == null)
                return;

            if (DefDatabase<RecipeDef>.GetNamedSilentFail(recipe.defName) != recipe)
            {
                R4Log.Warn($"Injected recipe validation failed for {recipe.defName}: def database lookup did not return the injected instance.");
            }

            if (recipe.defaultIngredientFilter == null || recipe.fixedIngredientFilter == null)
            {
                R4Log.Warn($"Injected recipe validation failed for {recipe.defName}: ingredient filters were not initialized.");
            }

            if (bench.recipes == null || !bench.recipes.Contains(recipe))
            {
                R4Log.Warn($"Injected recipe validation failed for {recipe.defName}: bench {bench.defName} does not contain the injected recipe.");
            }

            bool foundBenchUser = false;
            foreach (ThingDef recipeUser in AllRecipeUsers(recipe))
            {
                if (recipeUser == bench)
                {
                    foundBenchUser = true;
                    break;
                }
            }

            if (!foundBenchUser)
            {
                R4Log.Warn($"Injected recipe validation failed for {recipe.defName}: bench {bench.defName} is not visible in AllRecipeUsers.");
            }
        }

        static void ValidateInjectedWorkGiverDef(ThingDef bench, WorkGiverDef workGiver)
        {
            if (workGiver == null)
                return;

            if (DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiver.defName) != workGiver)
            {
                R4Log.Warn($"Injected WorkGiver validation failed for {workGiver.defName}: def database lookup did not return the injected instance.");
            }

            if (workGiver.fixedBillGiverDefs == null || !workGiver.fixedBillGiverDefs.Contains(bench))
            {
                R4Log.Warn($"Injected WorkGiver validation failed for {workGiver.defName}: bench {bench.defName} is not in fixedBillGiverDefs.");
            }

            if (workGiver.workType == null)
            {
                R4Log.Warn($"Injected WorkGiver validation failed for {workGiver.defName}: workType is null.");
            }

            try
            {
                WorkGiver worker = workGiver.Worker;
                if (worker == null)
                    R4Log.Warn($"Injected WorkGiver validation failed for {workGiver.defName}: Worker could not be instantiated.");
            }
            catch (Exception ex)
            {
                R4Log.Warn($"Injected WorkGiver validation failed for {workGiver.defName}: {ex}");
            }
        }

        // ── Step 3: stamp filter onto every RRRR RecipeDef ───────────────────

        static void PatchRecipeFilters()
        {
            var repairType  = typeof(RecipeWorker_R4Repair);
            var recycleType = typeof(RecipeWorker_R4Recycle);
            var cleanType   = typeof(RecipeWorker_R4Clean);

            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe.workerClass != repairType  &&
                    recipe.workerClass != recycleType &&
                    recipe.workerClass != cleanType) continue;

                var allowed = new HashSet<ThingDef>();
                foreach (ThingDef bench in AllRecipeUsers(recipe))
                {
                    if (BenchCraftables.TryGetValue(bench, out HashSet<ThingDef> craftables))
                        allowed.UnionWith(craftables);
                }

                if (recipe.workerClass == repairType)
                    allowed.RemoveWhere(d => !IsRepairEligible(d));
                else if (recipe.workerClass == recycleType)
                    allowed.RemoveWhere(d => !IsRecycleEligible(d));
                else
                    allowed.RemoveWhere(d => !IsCleanEligible(d));

                if (allowed.Count == 0)
                {
                    R4Log.Warn($"{recipe.defName}: no eligible items after filter build — bill will be empty.");
                    continue;
                }

                ThingFilter finalFilter = BuildFilter(allowed);
                recipe.fixedIngredientFilter   = finalFilter;
                // defaultIngredientFilter is what Bill..ctor copies into the new bill's
                // ingredientFilter via CopyAllowancesFrom. It must match fixedIngredientFilter
                // exactly — for XML-defined recipes ResolveReferences built it from the empty
                // placeholder before we stamped the real filter, so we always overwrite it here.
                recipe.defaultIngredientFilter = BuildFilter(allowed);
                if (recipe.ingredients != null && recipe.ingredients.Count > 0)
                    recipe.ingredients[0].filter = BuildFilter(allowed);

                R4Log.Debug($"{recipe.defName}: {allowed.Count} items in filter.");
            }
        }

        static ThingFilter BuildFilter(HashSet<ThingDef> defs)
        {
            ThingFilter filter = new ThingFilter();
            foreach (ThingDef td in defs)
                filter.SetAllow(td, true);

            // ResolveReferences sets up:
            //  1. disallowedSpecialFilters (AllowDeadmansApparel etc.) from allowedByDefault=false
            //  2. allowedHitPointsConfigurable / allowedQualitiesConfigurable → enables HP/quality sliders
            //  3. DisplayRootCategory → drives the filter UI tree
            // Declaration lists (thingDefs, categories) are null so step 1 is the only mutation.
            filter.ResolveReferences();

            // Re-allow all special filters that ResolveReferences just disallowed.
            // R4 recipes take gear items as inputs (not consumable materials), so:
            //  - Repair: tainted damaged items should be repairable
            //  - Recycle: tainted items are recyclable (taint penalty applied to yield)
            //  - Clean: tainted apparel is the required input
            // The player can still toggle these off per-bill in Dialog_BillConfig.
            foreach (SpecialThingFilterDef sf in DefDatabase<SpecialThingFilterDef>.AllDefsListForReading)
                if (!sf.allowedByDefault)
                    filter.SetAllow(sf, true);

            return filter;
        }

        static IEnumerable<ThingDef> AllRecipeUsers(RecipeDef recipe)
        {
            var seen = new HashSet<ThingDef>();

            foreach (ThingDef bench in recipe.AllRecipeUsers)
            {
                if (bench != null && seen.Add(bench))
                    yield return bench;
            }
        }

        // ── Public helpers ─────────────────────────────────────────────────────

        public static bool AnyBenchMatchesWorkType(IEnumerable<ThingDef> benchDefs, WorkTypeDef workType)
        {
            foreach (ThingDef bench in benchDefs)
                if (BenchWorkTypes.TryGetValue(bench, out WorkTypeDef wt) && wt == workType)
                    return true;
            return false;
        }

        // ── Eligibility predicate ──────────────────────────────────────────────

        static bool IsGear(ThingDef def)
        {
            return def != null &&
                   def.useHitPoints &&
                   (def.IsWeapon || def.IsApparel) &&
                   !ExplicitlyExcludedItems.Contains(def);
        }

        /// <summary>
        /// Broad R4 routing predicate used for shared bench discovery.
        /// Per-operation entry points should use IsRepairEligible,
        /// IsRecycleEligible, or IsCleanEligible instead.
        /// </summary>
        public static bool IsR4Eligible(ThingDef def) => IsGear(def);

        public static bool IsRepairEligible(ThingDef def) => IsGear(def);

        public static bool IsRecycleEligible(ThingDef def) => IsGear(def);

        public static bool IsCleanEligible(ThingDef def) =>
            def != null && def.useHitPoints && def.IsApparel &&
            !ExplicitlyExcludedItems.Contains(def);
    }
}
