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
    /// Designation-based WorkGivers (WorkGiver_R4Repair/Recycle/Clean) use
    /// WorkbenchRouter.GetValidBenches which reads recipeMaker.recipeUsers directly
    /// and is unaffected by these filters.
    /// </summary>
    public static class R4WorkbenchFilterCache
    {
        // ── Public data (WorkbenchRouter fallback reads this) ──────────────────

        /// <summary>bench ThingDef → set of R4-eligible items it can craft.</summary>
        public static readonly Dictionary<ThingDef, HashSet<ThingDef>> BenchCraftables
            = new Dictionary<ThingDef, HashSet<ThingDef>>();

        // ── Entry point ────────────────────────────────────────────────────────

        static R4WorkbenchFilterCache()
        {
            Build();
        }

        public static void Build()
        {
            BenchCraftables.Clear();

            BuildFromRecipeMakers();
            AssignFallbacks();
            PatchRecipeFilters();
            LogSummary();
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
            var covered = new HashSet<ThingDef>(BenchCraftables.Values.SelectMany(s => s));
            var fallbackMap = BuildFallbackMap();

            int count = 0;
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

                Log.Message($"[R4] Fallback → {item.defName} (tech={item.techLevel}) → {bench.defName}");
            }

            Log.Message($"[R4] {count} items assigned via techLevel fallback.");
        }

        static Dictionary<TechLevel, ThingDef> BuildFallbackMap()
        {
            ThingDef craftingSpot   = DefDatabase<ThingDef>.GetNamedSilentFail("CraftingSpot");
            ThingDef fueledSmithy   = DefDatabase<ThingDef>.GetNamedSilentFail("FueledSmithy");
            ThingDef electricSmithy = DefDatabase<ThingDef>.GetNamedSilentFail("ElectricSmithy");
            ThingDef machining      = DefDatabase<ThingDef>.GetNamedSilentFail("TableMachining");
            ThingDef fabrication    = DefDatabase<ThingDef>.GetNamedSilentFail("FabricationBench");

            // Prefer electric smithy; fall back to fueled
            ThingDef smithy = electricSmithy ?? fueledSmithy;

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
            // Hard fallback: machining table handles unknowns
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

                // Union craftable sets for every bench this recipe is on
                var allowed = new HashSet<ThingDef>();
                foreach (ThingDef bench in recipe.recipeUsers)
                {
                    if (BenchCraftables.TryGetValue(bench, out HashSet<ThingDef> craftables))
                        allowed.UnionWith(craftables);
                }

                // Clean bills: only apparel (weapons can't be tainted)
                if (recipe.workerClass == cleanType)
                    allowed.RemoveWhere(d => !d.IsApparel);

                if (allowed.Count == 0)
                {
                    Log.Warning($"[R4] {recipe.defName}: no eligible items after filter build — bill will be empty.");
                    continue;
                }

                // Build ThingFilter from the allowed set
                recipe.fixedIngredientFilter = BuildFilter(allowed);

                if (recipe.ingredients != null && recipe.ingredients.Count > 0)
                    recipe.ingredients[0].filter = BuildFilter(allowed);

                Log.Message($"[R4] {recipe.defName}: {allowed.Count} items in filter.");
            }
        }

        static ThingFilter BuildFilter(HashSet<ThingDef> defs)
        {
            ThingFilter filter = new ThingFilter();
            foreach (ThingDef td in defs)
                filter.SetAllow(td, true);
            return filter;
        }

        // ── Eligibility predicate ──────────────────────────────────────────────

        /// <summary>
        /// An item is R4-eligible if:
        /// - it uses hit points (can be damaged/repaired)
        /// - it is a weapon or apparel
        /// - it is smeltable (player-owned item, not a turret internal component)
        /// </summary>
        public static bool IsR4Eligible(ThingDef def) =>
            def.useHitPoints &&
            (def.IsWeapon || def.IsApparel) &&
            def.smeltable;

        // ── Logging ───────────────────────────────────────────────────────────

        static void LogSummary()
        {
            foreach (KeyValuePair<ThingDef, HashSet<ThingDef>> kvp in BenchCraftables)
                Log.Message($"[R4] BenchCraftables[{kvp.Key.defName}]: {kvp.Value.Count} items");
        }
    }
}
