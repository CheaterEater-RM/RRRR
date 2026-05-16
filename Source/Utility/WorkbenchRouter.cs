using System.Collections.Generic;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Maps items to valid workbenches for designation-based jobs
    /// (WorkGiver_R4Repair, WorkGiver_R4Recycle, WorkGiver_R4Clean).
    ///
    /// Primary: read the item's own recipeMaker.recipeUsers — the benches that
    /// can craft it are the right benches to repair/recycle it.
    ///
    /// Fallback: for items without a recipeMaker, use the cached inverse of
    /// R4WorkbenchFilterCache.BenchCraftables. This covers loot drops, quest
    /// rewards, and trader items. The cache is built once at startup by
    /// BuildFallbackCache(), eliminating the per-call dictionary scan + allocation
    /// that was the main performance concern on the designation path.
    /// </summary>
    public static class WorkbenchRouter
    {
        private static readonly Dictionary<ThingDef, List<ThingDef>> BenchAliases
            = new Dictionary<ThingDef, List<ThingDef>>();

        private static readonly Dictionary<ThingDef, List<ThingDef>> ExpandedRecipeUserCache
            = new Dictionary<ThingDef, List<ThingDef>>();

        /// <summary>
        /// Inverted cache: item ThingDef → list of bench ThingDefs that can service it.
        /// Populated once at startup by BuildFallbackCache().
        /// </summary>
        private static readonly Dictionary<ThingDef, List<ThingDef>> FallbackBenchCache
            = new Dictionary<ThingDef, List<ThingDef>>();

        private static List<ThingDef> _lastResort = new List<ThingDef>();

        public static bool HasAliases => BenchAliases.Count > 0;

        public static void Reset()
        {
            BenchAliases.Clear();
            ExpandedRecipeUserCache.Clear();
            FallbackBenchCache.Clear();
            _lastResort = new List<ThingDef>();
        }

        public static bool RegisterAlias(ThingDef sourceBench, ThingDef aliasBench)
        {
            if (sourceBench == null || aliasBench == null || sourceBench == aliasBench)
                return false;

            if (!BenchAliases.TryGetValue(sourceBench, out List<ThingDef> aliases))
            {
                aliases = new List<ThingDef>();
                BenchAliases[sourceBench] = aliases;
            }

            if (aliases.Contains(aliasBench))
                return false;

            aliases.Add(aliasBench);
            return true;
        }

        public static IEnumerable<KeyValuePair<ThingDef, List<ThingDef>>> GetAllAliases()
        {
            return BenchAliases;
        }

        /// <summary>
        /// Called once from R4WorkbenchFilterCache.Build() after BenchCraftables
        /// is fully populated. Inverts the bench→items mapping into items→benches
        /// for O(1) fallback lookups.
        /// </summary>
        public static void BuildFallbackCache()
        {
            FallbackBenchCache.Clear();
            ExpandedRecipeUserCache.Clear();

            foreach (KeyValuePair<ThingDef, HashSet<ThingDef>> kvp in R4WorkbenchFilterCache.BenchCraftables)
            {
                ThingDef bench = kvp.Key;
                foreach (ThingDef item in kvp.Value)
                {
                    if (!FallbackBenchCache.TryGetValue(item, out List<ThingDef> benches))
                    {
                        benches = new List<ThingDef>();
                        FallbackBenchCache[item] = benches;
                    }

                    if (!benches.Contains(bench))
                        benches.Add(bench);
                }
            }

            if (HasAliases)
            {
                foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    List<ThingDef> recipeUsers = item.recipeMaker?.recipeUsers;
                    if (recipeUsers == null || recipeUsers.Count == 0)
                        continue;

                    bool needsExpansion = false;
                    for (int i = 0; i < recipeUsers.Count; i++)
                    {
                        if (BenchAliases.ContainsKey(recipeUsers[i]))
                        {
                            needsExpansion = true;
                            break;
                        }
                    }

                    if (!needsExpansion)
                        continue;

                    var expanded = new List<ThingDef>();
                    var seen = new HashSet<ThingDef>();
                    for (int i = 0; i < recipeUsers.Count; i++)
                        AddBenchAndAliases(recipeUsers[i], expanded, seen);

                    ExpandedRecipeUserCache[item] = expanded;
                }
            }

            ThingDef machining = DefDatabase<ThingDef>.GetNamedSilentFail("TableMachining");
            _lastResort = machining != null ? new List<ThingDef> { machining } : new List<ThingDef>();

            R4Log.Debug(
                $"WorkbenchRouter cache: {FallbackBenchCache.Count} fallback items, {ExpandedRecipeUserCache.Count} expanded recipe-user entries.");
        }

        /// <summary>
        /// Returns the list of valid bench ThingDefs for the given item.
        /// </summary>
        public static List<ThingDef> GetValidBenches(Thing item)
        {
            // Primary: item declares its own crafting benches
            List<ThingDef> recipeUsers = item.def.recipeMaker?.recipeUsers;
            if (recipeUsers != null && recipeUsers.Count > 0)
            {
                if (ExpandedRecipeUserCache.TryGetValue(item.def, out List<ThingDef> expanded) && expanded.Count > 0)
                    return expanded;

                return recipeUsers;
            }

            // Fallback: O(1) lookup from the pre-built inverse cache
            if (FallbackBenchCache.TryGetValue(item.def, out List<ThingDef> cached) && cached.Count > 0)
                return cached;

            // Last resort: machining table handles unknowns
            return _lastResort;
        }

        private static void AddBenchAndAliases(ThingDef bench, List<ThingDef> expanded, HashSet<ThingDef> seen)
        {
            if (bench == null || !seen.Add(bench))
                return;

            expanded.Add(bench);

            if (!BenchAliases.TryGetValue(bench, out List<ThingDef> aliases))
                return;

            for (int i = 0; i < aliases.Count; i++)
                AddBenchAndAliases(aliases[i], expanded, seen);
        }
    }
}
