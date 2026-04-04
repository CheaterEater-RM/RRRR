using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Startup cache that builds dynamic ThingFilters for each RRRR bill recipe
    /// by inverting the recipeMaker.recipeUsers relationship, and auto-detects
    /// modded workbenches to inject R4 bills and strip smelter recipes.
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
    ///   After the cache is built, any bench in BenchCraftables that isn't already
    ///   covered by an existing R4 RecipeDef gets dynamic R4 RecipeDefs and
    ///   WorkGiverDefs injected into the game at runtime. This means modded
    ///   workbenches automatically receive repair/recycle/clean bills.
    ///
    /// The approach relies on two sources of bench-item relationships:
    ///   1. item.recipeMaker.recipeUsers  — items listing their benches (primary)
    ///   2. recipe.recipeUsers            — recipes listing their benches (secondary)
    ///      Used to catch modded benches added via recipe-side patching.
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

            BuildEligibilityExclusions();
            StripSmelterRecipes();
            BuildBenchWorkTypes();
            BuildFromRecipeMakers();
            BuildFromRecipeSideUsers();
            AssignFallbacks();
            InjectModdedBenchBills();
            PatchRecipeFilters();

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

        // ── Step 1b: invert recipe.recipeUsers ────────────────────────────────

        /// <summary>
        /// Secondary pass: scan all RecipeDefs whose recipeUsers list benches.
        /// This catches mods that add a new bench by patching an existing recipe's
        /// recipeUsers list (rather than setting recipeMaker.recipeUsers on items).
        /// We look at the produced items of each recipe to find eligible gear.
        /// </summary>
        static void BuildFromRecipeSideUsers()
        {
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe.recipeUsers == null || recipe.recipeUsers.Count == 0) continue;
                // Only care about recipes that produce a single eligible thing
                ThingDef product = recipe.ProducedThingDef;
                if (product == null || !IsR4Eligible(product)) continue;

                foreach (ThingDef bench in recipe.recipeUsers)
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
        /// For any bench in BenchCraftables that isn't already covered by an
        /// existing R4 RecipeDef, dynamically create and inject repair + recycle
        /// (and clean, if the bench has apparel) RecipeDefs and a WorkGiverDef
        /// for the bill pipeline.
        ///
        /// This means mods adding new weapon/apparel workbenches automatically
        /// get R4 bills without requiring explicit compatibility patches.
        /// </summary>
        static void InjectModdedBenchBills()
        {
            // Build set of benches already covered by existing R4 RecipeDefs
            var coveredBenches = new HashSet<ThingDef>();
            var r4Types = new HashSet<System.Type>
            {
                typeof(RecipeWorker_R4Repair),
                typeof(RecipeWorker_R4Recycle),
                typeof(RecipeWorker_R4Clean),
            };

            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (!r4Types.Contains(recipe.workerClass)) continue;
                if (recipe.recipeUsers == null) continue;
                foreach (ThingDef bench in recipe.recipeUsers)
                    coveredBenches.Add(bench);
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

            int injected = 0;

            foreach (var kvp in BenchCraftables)
            {
                ThingDef bench = kvp.Key;
                if (coveredBenches.Contains(bench)) continue;

                // Only inject onto actual workbenches with a bills tab
                if (bench.thingClass == null) continue;
                if (!typeof(Building_WorkTable).IsAssignableFrom(bench.thingClass)) continue;
                if (bench.inspectorTabs == null || !bench.inspectorTabs.Contains(typeof(ITab_Bills))) continue;

                bool hasClean   = kvp.Value.Any(IsCleanEligible);
                string safeName = bench.defName.Replace(" ", "_");

                // Inject repair bill
                InjectRecipeDef(repairTemplate,  $"RRRR_Repair_Mod_{safeName}",  bench, bench.recipes);
                // Inject recycle bill
                InjectRecipeDef(recycleTemplate, $"RRRR_Recycle_Mod_{safeName}", bench, bench.recipes);
                // Inject clean bill if bench has apparel items
                if (hasClean)
                    InjectRecipeDef(cleanTemplate, $"RRRR_Clean_Mod_{safeName}", bench, bench.recipes);

                // Inject a WorkGiverDef for the repair bill pipeline
                InjectRepairBillWorkGiver(bench, safeName);

                R4Log.Debug($"Injected R4 bills onto modded bench: {bench.defName}");
                injected++;
            }

            if (injected > 0)
                R4Log.Debug($"Injected R4 bills onto {injected} modded bench(es).");
        }

        static void InjectRecipeDef(RecipeDef template, string defName, ThingDef bench, List<RecipeDef> benchRecipes)
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
                ingredients             = clonedIngredients,
                fixedIngredientFilter   = new ThingFilter(), // rebuilt by PatchRecipeFilters
                defaultIngredientFilter = new ThingFilter(), // must be non-null; synced by PatchRecipeFilters
                recipeUsers             = new List<ThingDef> { bench },
            };

            recipe.ResolveDefNameHash();
            DefDatabase<RecipeDef>.Add(recipe);

            // Also add directly to the bench's recipes list
            if (benchRecipes != null && !benchRecipes.Contains(recipe))
                benchRecipes.Add(recipe);
        }

        static void InjectRepairBillWorkGiver(ThingDef bench, string safeName)
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

                if (recipe.recipeUsers == null || recipe.recipeUsers.Count == 0) continue;

                var allowed = new HashSet<ThingDef>();
                foreach (ThingDef bench in recipe.recipeUsers)
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
            return filter;
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
            def != null && def.useHitPoints && def.IsApparel;
    }
}
