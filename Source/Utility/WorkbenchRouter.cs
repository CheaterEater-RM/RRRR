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
        /// <summary>
        /// Inverted cache: item ThingDef → list of bench ThingDefs that can service it.
        /// Populated once at startup by BuildFallbackCache().
        /// </summary>
        private static readonly Dictionary<ThingDef, List<ThingDef>> FallbackBenchCache
            = new Dictionary<ThingDef, List<ThingDef>>();

        private static List<ThingDef> _lastResort = new List<ThingDef>();

        /// <summary>
        /// Called once from R4WorkbenchFilterCache.Build() after BenchCraftables
        /// is fully populated. Inverts the bench→items mapping into items→benches
        /// for O(1) fallback lookups.
        /// </summary>
        public static void BuildFallbackCache()
        {
            FallbackBenchCache.Clear();

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

            ThingDef machining = DefDatabase<ThingDef>.GetNamedSilentFail("TableMachining");
            _lastResort = machining != null ? new List<ThingDef> { machining } : new List<ThingDef>();

            R4Log.Debug($"WorkbenchRouter fallback cache: {FallbackBenchCache.Count} items mapped.");
        }

        /// <summary>
        /// Returns the list of valid bench ThingDefs for the given item.
        /// </summary>
        public static List<ThingDef> GetValidBenches(Thing item)
        {
            // Primary: item declares its own crafting benches
            List<ThingDef> recipeUsers = item.def.recipeMaker?.recipeUsers;
            if (recipeUsers != null && recipeUsers.Count > 0)
                return recipeUsers;

            // Fallback: O(1) lookup from the pre-built inverse cache
            if (FallbackBenchCache.TryGetValue(item.def, out List<ThingDef> cached) && cached.Count > 0)
                return cached;

            // Last resort: machining table handles unknowns
            return _lastResort;
        }
    }
}
