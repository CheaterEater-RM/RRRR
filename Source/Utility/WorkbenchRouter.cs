using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Determines the correct workbench for a given item based on its properties.
    /// </summary>
    public static class WorkbenchRouter
    {
        // Cached mapping of ThingDef → valid workbench ThingDefs
        private static Dictionary<ThingDef, List<ThingDef>> benchCache = new Dictionary<ThingDef, List<ThingDef>>();

        /// <summary>
        /// Find the best reachable workbench for the given item and pawn.
        /// </summary>
        public static Building FindBestBench(Thing item, Pawn worker)
        {
            var validBenchDefs = GetValidBenchDefs(item.def);
            if (validBenchDefs == null || validBenchDefs.Count == 0)
                return null;

            Building best = null;
            float bestDist = float.MaxValue;

            foreach (var benchDef in validBenchDefs)
            {
                var buildings = worker.Map.listerBuildings.AllBuildingsColonistOfDef(benchDef);
                foreach (var building in buildings)
                {
                    if (building is Building_WorkTable workTable
                        && !building.IsForbidden(worker)
                        && !building.IsBurning()
                        && worker.CanReserveAndReach(building, PathEndMode.InteractionCell, Danger.Some))
                    {
                        float dist = building.Position.DistanceToSquared(worker.Position);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = building;
                        }
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Get the list of valid workbench ThingDefs for a given item ThingDef.
        /// </summary>
        public static List<ThingDef> GetValidBenchDefs(ThingDef itemDef)
        {
            if (benchCache.TryGetValue(itemDef, out var cached))
                return cached;

            var result = new List<ThingDef>();

            if (itemDef.IsApparel)
            {
                if (IsMetallic(itemDef))
                {
                    // Armor/plate apparel → smithy + machining
                    TryAddBenchDef(result, "ElectricSmithy");
                    TryAddBenchDef(result, "FueledSmithy");
                    TryAddBenchDef(result, "TableMachining");
                }
                else
                {
                    // Textile/leather apparel → tailoring benches
                    TryAddBenchDef(result, "ElectricTailoringBench");
                    TryAddBenchDef(result, "HandTailoringBench");
                }
            }
            else if (itemDef.IsRangedWeapon)
            {
                TryAddBenchDef(result, "TableMachining");
            }
            else if (itemDef.IsMeleeWeapon)
            {
                if (itemDef.techLevel <= TechLevel.Medieval)
                {
                    TryAddBenchDef(result, "ElectricSmithy");
                    TryAddBenchDef(result, "FueledSmithy");
                }
                else
                {
                    TryAddBenchDef(result, "TableMachining");
                }
            }

            // Fallback: crafting spot
            if (result.Count == 0)
            {
                TryAddBenchDef(result, "CraftingSpot");
            }

            benchCache[itemDef] = result;
            return result;
        }

        private static bool IsMetallic(ThingDef itemDef)
        {
            // Check stuff categories for metallic
            if (itemDef.stuffCategories != null)
            {
                foreach (var cat in itemDef.stuffCategories)
                {
                    if (cat == StuffCategoryDefOf.Metallic)
                        return true;
                }
            }

            // Check costList for metallic materials
            if (itemDef.costList != null)
            {
                foreach (var cost in itemDef.costList)
                {
                    if (cost.thingDef != null && cost.thingDef.IsStuff
                        && cost.thingDef.stuffProps?.categories != null
                        && cost.thingDef.stuffProps.categories.Contains(StuffCategoryDefOf.Metallic))
                    {
                        return true;
                    }
                    // Also catch components/plasteel/steel by defName
                    if (cost.thingDef == ThingDefOf.Steel || cost.thingDef == ThingDefOf.Plasteel
                        || cost.thingDef == ThingDefOf.ComponentIndustrial
                        || cost.thingDef == ThingDefOf.ComponentSpacer)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void TryAddBenchDef(List<ThingDef> list, string defName)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def != null)
                list.Add(def);
        }

        /// <summary>
        /// Clear the routing cache (call if defs are modified at runtime).
        /// </summary>
        public static void ClearCache()
        {
            benchCache.Clear();
        }
    }
}
