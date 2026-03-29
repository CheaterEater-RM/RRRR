using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Startup cache that builds dynamic ThingFilters for each RRRR bill recipe
    /// by inverting the recipeMaker.recipeUsers relationship.
    ///
    /// For every item that declares which bench(es) can craft it, we add that item
    /// to those benches' eligible sets. Items with no recipeMaker are routed by
    /// techLevel (the fallback map). The resulting filter is then stamped onto each
    /// RRRR RecipeDef's fixedIngredientFilter and ingredients[0].filter so that
    /// each bench's bills only show items that bench could have crafted.
    ///
    /// Also builds BenchWorkTypes: bench ThingDef → WorkTypeDef, used by
    /// designation WorkGivers to filter candidates by the current pawn's work type.
    ///
    /// BenchWorkTypes priority: vanilla WorkGiver_DoBill entries take precedence
    /// over any modded WorkGivers for the same bench, ensuring deterministic
    /// assignment regardless of mod load order.
    /// </summary>
    public static class R4WorkbenchFilterCache
    {
        // ── Public data ────────────────────────────────────────────────────────

        /// <summary>bench ThingDef → set of R4-eligible items it can craft.</summary>
        public static readonly Dictionary<ThingDef, HashSet<ThingDef>> BenchCraftables
            = new Dictionary<ThingDef, HashSet<ThingDef>>();

        /// <summary>
        /// bench ThingDef → the WorkTypeDef that canonically services it.
        /// Vanilla WorkGiver_DoBill entries take priority over modded ones.
        /// </summary>
        public static readonly Dictionary<ThingDef, WorkTypeDef> BenchWorkTypes
            = new Dictionary<ThingDef, WorkTypeDef>();

        // ── Entry point ────────────────────────────────────────────────────────

        static R4WorkbenchFilterCache()
        {
            Build();
        }

        public static void Build()
        {
            BenchCraftables.Clear();
            BenchWorkTypes.Clear();

            BuildBenchWorkTypes();
            BuildFromRecipeMakers();
            AssignFallbacks();
            PatchRecipeFilters();

            R4Log.Debug($"Cache built: {BenchCraftables.Count} benches, " +
                        $"{BenchCraftables.Values.Sum(s => s.Count)} item-bench mappings.");
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

        // ── Step 1: invert recipeMaker.recipeUsers ─────────────────────────────

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

                if (recipe.workerClass == cleanType)
                    allowed.RemoveWhere(d => !d.IsApparel);

                if (allowed.Count == 0)
                {
                    R4Log.Warn($"{recipe.defName}: no eligible items after filter build — bill will be empty.");
                    continue;
                }

                recipe.fixedIngredientFilter = BuildFilter(allowed);
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

        /// <summary>
        /// An item is R4-eligible if it uses hit points, is a weapon or apparel,
        /// and is smeltable (excludes improvised-weapon items like beer and wood logs).
        /// </summary>
        public static bool IsR4Eligible(ThingDef def) =>
            def.useHitPoints &&
            (def.IsWeapon || def.IsApparel) &&
            def.smeltable;
    }
}
