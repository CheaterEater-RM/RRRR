using System.Collections.Generic;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Maps item types to valid workbenches by scanning recipes at startup.
    /// Smeltable items → SmeltBenches (benches with SmeltWeapon/SmeltApparel recipes).
    /// Non-smeltable apparel items → ApparelBenches (benches with apparel crafting recipes).
    /// Fallback: when no specific bench category applies, returns SmeltBenches if non-empty,
    /// otherwise returns ApparelBenches.
    /// </summary>
    public static class WorkbenchRouter
    {
        /// <summary>
        /// Returns the cached list of valid bench defs for the given item.
        /// </summary>
        public static List<ThingDef> GetValidBenches(Thing item)
        {
            if (item.Smeltable)
                return R4ThingDefCache.SmeltBenches;

            if (item.def.IsApparel)
                return R4ThingDefCache.ApparelBenches;

            // Fallback: try smelt benches first, then apparel benches
            if (R4ThingDefCache.SmeltBenches.Count > 0)
                return R4ThingDefCache.SmeltBenches;

            return R4ThingDefCache.ApparelBenches;
        }
    }
}
