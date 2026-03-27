using System.Collections.Generic;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Maps items to valid workbenches. Primary strategy: use the item's own
    /// recipeMaker.recipeUsers — these are the benches where the item was made,
    /// so they're the right place to recycle/repair it too.
    /// 
    /// Fallback for items without recipeMaker (quest rewards, trader items, etc.):
    /// smeltable → smelt benches, non-smeltable → apparel/crafting benches.
    /// </summary>
    public static class WorkbenchRouter
    {
        /// <summary>
        /// Returns the list of valid bench ThingDefs for the given item.
        /// Prefers the item's own recipeMaker.recipeUsers when available.
        /// </summary>
        public static List<ThingDef> GetValidBenches(Thing item)
        {
            // Primary: use the item's own crafting bench list
            var recipeUsers = item.def.recipeMaker?.recipeUsers;
            if (recipeUsers != null && recipeUsers.Count > 0)
                return recipeUsers;

            // Fallback: route by smeltable/non-smeltable using cached bench lists
            if (item.Smeltable)
                return R4ThingDefCache.SmeltBenches;

            if (item.def.IsApparel)
                return R4ThingDefCache.ApparelBenches;

            // Last resort: try smelt benches, then apparel benches
            if (R4ThingDefCache.SmeltBenches.Count > 0)
                return R4ThingDefCache.SmeltBenches;

            return R4ThingDefCache.ApparelBenches;
        }
    }
}
