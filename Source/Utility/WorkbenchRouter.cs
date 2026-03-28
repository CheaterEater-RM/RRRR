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
    /// Fallback: for items without a recipeMaker, use the techLevel-based bench
    /// assignments built by R4WorkbenchFilterCache.BenchCraftables. This covers
    /// loot drops, quest rewards, and trader items.
    /// </summary>
    public static class WorkbenchRouter
    {
        /// <summary>
        /// Returns the list of valid bench ThingDefs for the given item.
        /// </summary>
        public static List<ThingDef> GetValidBenches(Thing item)
        {
            // Primary: item declares its own crafting benches
            List<ThingDef> recipeUsers = item.def.recipeMaker?.recipeUsers;
            if (recipeUsers != null && recipeUsers.Count > 0)
                return recipeUsers;

            // Fallback: find all benches that have this item in their craftable set
            // (populated by R4WorkbenchFilterCache for items without recipeMaker)
            var result = new List<ThingDef>();
            foreach (KeyValuePair<ThingDef, HashSet<ThingDef>> kvp in R4WorkbenchFilterCache.BenchCraftables)
            {
                if (kvp.Value.Contains(item.def))
                    result.Add(kvp.Key);
            }

            if (result.Count > 0)
                return result;

            // Last resort: machining table handles unknowns
            ThingDef machining = DefDatabase<ThingDef>.GetNamedSilentFail("TableMachining");
            if (machining != null)
                return new List<ThingDef> { machining };

            return result; // empty
        }
    }
}
