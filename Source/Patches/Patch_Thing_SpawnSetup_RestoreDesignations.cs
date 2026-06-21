using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Restore half of the inventory-designation-preservation feature.
    ///
    /// When an item that was hauled into a pawn's inventory is dropped back onto a
    /// map — by an inventory-hauling mod's unload (Pick Up And Haul, Hauler's
    /// Dream, …), by a pawn dying, or by a caravan returning/settling — it respawns
    /// and this postfix fires. If the tracker has a record for it, the captured R4
    /// designations are re-applied.
    ///
    /// Re-application is gated on a fresh eligibility check, so we never schedule a
    /// stale repair/clean/recycle job for an item whose state changed in transit.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    public static class Patch_Thing_SpawnSetup_RestoreDesignations
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, Map map, bool respawningAfterLoad)
        {
            var tracker = R4HauledDesignationTracker.Instance;
            if (tracker == null || tracker.IsEmpty)
                return;

            // On a load, the designation is already saved on the map; don't touch.
            if (respawningAfterLoad)
                return;

            if (!tracker.TryConsume(__instance.thingIDNumber, out List<DesignationDef> defs))
                return;

            if (map == null || defs == null)
                return;

            DesignationManager dm = map.designationManager;
            for (int i = 0; i < defs.Count; i++)
            {
                DesignationDef def = defs[i];
                if (def == null)
                    continue;
                if (!StillEligible(__instance, def))
                    continue;
                if (dm.DesignationOn(__instance, def) != null)
                    continue;
                dm.AddDesignation(new Designation(__instance, def));
            }
        }

        /// <summary>
        /// Re-validate that the work still makes sense, mirroring the designators'
        /// CanDesignateThing checks (Designator_RepairThing / _CleanThing / _RecycleThing).
        /// </summary>
        private static bool StillEligible(Thing t, DesignationDef def)
        {
            if (def == R4DefOf.R4_Repair)
            {
                return R4WorkbenchFilterCache.IsRepairEligible(t.def)
                    && t.def.useHitPoints
                    && t.HitPoints < t.MaxHitPoints;
            }
            if (def == R4DefOf.R4_Clean)
            {
                return R4WorkbenchFilterCache.IsCleanEligible(t.def)
                    && t is Apparel apparel
                    && apparel.WornByCorpse;
            }
            if (def == R4DefOf.R4_Recycle)
            {
                return R4WorkbenchFilterCache.IsRecycleEligible(t.def);
            }
            return false;
        }
    }
}
