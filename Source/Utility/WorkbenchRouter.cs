using System.Collections.Generic;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Maps items to valid workbenches for designation-based jobs
    /// (WorkGiver_R4Repair, WorkGiver_R4Recycle, WorkGiver_R4Clean) and for the
    /// gizmo "Routes to" tooltip.
    ///
    /// The merged cache is precomputed at startup from R4WorkbenchFilterCache.BenchCraftables,
    /// which by the time BuildFallbackCache runs already contains:
    ///   • native recipeMaker.recipeUsers entries (BuildFromRecipeMakers)
    ///   • recipe-side bench ownership (BuildFromRecipeSideUsers)
    ///   • catch-all predicate additions (ApplyCatchAllPredicates)
    ///   • VEF inheritance unions (UnionVEFAliasedBenchCraftables)
    ///
    /// Result list order, per item:
    ///   (a) recipeMaker.recipeUsers (VEF-expanded) — natural bench(es) first
    ///   (b) catch-all benches in tier-ascending order (CraftingSpot → Fabrication)
    ///   (c) any remaining benches (modded benches not in the ordered list)
    ///
    /// IMPORTANT: This order is for tooltip stability only. Designation routing
    /// in WorkGiver_R4DesignationBase.FindBench pools all candidate benches and
    /// picks the closest reachable one via GenClosest, so the actual bench used
    /// is closest-wins, not first-in-list.
    /// </summary>
    public static class WorkbenchRouter
    {
        private static readonly Dictionary<ThingDef, List<ThingDef>> BenchAliases
            = new Dictionary<ThingDef, List<ThingDef>>();

        /// <summary>
        /// item ThingDef → ordered list of bench ThingDefs that can service it.
        /// Populated once by BuildFallbackCache at startup.
        /// </summary>
        private static readonly Dictionary<ThingDef, List<ThingDef>> MergedBenchCache
            = new Dictionary<ThingDef, List<ThingDef>>();

        private static List<ThingDef> _lastResort = new List<ThingDef>();

        public static bool HasAliases => BenchAliases.Count > 0;

        public static void Reset()
        {
            BenchAliases.Clear();
            MergedBenchCache.Clear();
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
        /// in deterministic tier-ascending order for O(1) routing lookups.
        /// </summary>
        public static void BuildFallbackCache()
        {
            MergedBenchCache.Clear();

            // (1) Invert BenchCraftables → item → set of benches.
            var inverse = new Dictionary<ThingDef, HashSet<ThingDef>>();
            foreach (KeyValuePair<ThingDef, HashSet<ThingDef>> kvp in R4WorkbenchFilterCache.BenchCraftables)
            {
                ThingDef bench = kvp.Key;
                foreach (ThingDef item in kvp.Value)
                {
                    if (!inverse.TryGetValue(item, out HashSet<ThingDef> benches))
                        inverse[item] = benches = new HashSet<ThingDef>();
                    benches.Add(bench);
                }
            }

            List<ThingDef> orderedCatchAll = R4WorkbenchFilterCache.OrderedCatchAllBenches;

            // (2) Per item, build the merged ordered list.
            // The `seen` HashSet is reused across iterations via Clear() to avoid
            // per-item allocations; the merged List<> sizes to itemBenches.Count
            // to avoid resizing during Add.
            var seen = new HashSet<ThingDef>();
            foreach (KeyValuePair<ThingDef, HashSet<ThingDef>> kvp in inverse)
            {
                ThingDef item = kvp.Key;
                HashSet<ThingDef> itemBenches = kvp.Value;

                seen.Clear();
                var merged = new List<ThingDef>(itemBenches.Count);

                // (a) recipeMaker first (VEF-expanded) — natural benches at the
                // head of the list for tooltip stability.
                List<ThingDef> recipeUsers = item.recipeMaker?.recipeUsers;
                if (recipeUsers != null)
                {
                    for (int i = 0; i < recipeUsers.Count; i++)
                        AddBenchAndAliases(recipeUsers[i], merged, seen);
                }

                // (b) Catch-all benches in tier order, only if the item matched.
                for (int i = 0; i < orderedCatchAll.Count; i++)
                {
                    ThingDef bench = orderedCatchAll[i];
                    if (itemBenches.Contains(bench) && seen.Add(bench))
                        merged.Add(bench);
                }

                // (c) Any remaining inverse benches (modded benches, VEF alias
                // targets that aren't in the ordered list).
                foreach (ThingDef bench in itemBenches)
                {
                    if (seen.Add(bench))
                        merged.Add(bench);
                }

                MergedBenchCache[item] = merged;
            }

            ThingDef machining = DefDatabase<ThingDef>.GetNamedSilentFail("TableMachining");
            _lastResort = machining != null ? new List<ThingDef> { machining } : new List<ThingDef>();

            R4Log.Debug($"WorkbenchRouter cache: {MergedBenchCache.Count} merged item entries.");
        }

        /// <summary>
        /// Returns the bench defs that can service this item. Order is
        /// tooltip-stable (recipeMaker first, then catch-all in tier order,
        /// then other) but the bench actually used by a designation job is
        /// determined by FindBench's closest-reachable selection — order
        /// does NOT decide routing.
        /// </summary>
        public static List<ThingDef> GetValidBenches(Thing item)
        {
            if (MergedBenchCache.TryGetValue(item.def, out List<ThingDef> merged) && merged.Count > 0)
                return merged;

            // Defensive fallbacks. R4-eligible items should always be in the
            // merged cache (the FabricationBench catch-all is ≤ Archotech).
            // These paths cover degenerate cases: items asked about that aren't
            // R4-eligible, or environments where FabricationBench was modded out.
            List<ThingDef> recipeUsers = item.def.recipeMaker?.recipeUsers;
            if (recipeUsers != null && recipeUsers.Count > 0)
                return recipeUsers;

            return _lastResort;
        }

        private static void AddBenchAndAliases(
            ThingDef bench,
            List<ThingDef> merged,
            HashSet<ThingDef> seen)
        {
            if (bench == null || !seen.Add(bench))
                return;

            merged.Add(bench);

            if (!BenchAliases.TryGetValue(bench, out List<ThingDef> aliases))
                return;

            for (int i = 0; i < aliases.Count; i++)
                AddBenchAndAliases(aliases[i], merged, seen);
        }
    }
}
