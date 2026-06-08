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
    /// map — by Pick Up And Haul's unload, by a pawn dying, or by a caravan
    /// returning/settling — it respawns and this postfix fires. If the tracker has
    /// a record for it, the captured R4 designations are re-applied.
    ///
    /// The record is consumed unconditionally (even when the feature is off) so a
    /// later re-enable cannot resurrect a stale designation. Re-application is
    /// gated on the setting and on a fresh eligibility check, so we never schedule
    /// a stale repair/clean/recycle job for an item whose state changed in transit.
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

            // Consume unconditionally so disabled periods drain stale records.
            if (!tracker.TryConsume(__instance.thingIDNumber, out List<DesignationDef> defs))
                return;

            if (!RRRR_Mod.Settings.preserveInventoryDesignations)
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
