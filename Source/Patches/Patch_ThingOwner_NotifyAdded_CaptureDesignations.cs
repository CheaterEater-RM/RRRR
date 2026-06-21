using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Capture half of the inventory-designation-preservation feature.
    ///
    /// Vanilla <c>ThingOwner.NotifyAdded</c> strips an item's map designations when
    /// it enters an enclosing container — which includes a pawn's inventory. This
    /// prefix runs immediately before that strip and, for items entering a
    /// <c>Pawn_InventoryTracker</c>, snapshots any R4 designations into
    /// <see cref="R4HauledDesignationTracker"/> so the restore patch can re-apply
    /// them when the item is dropped back on a map.
    ///
    /// We patch the exact method that performs the strip, so capture is co-located
    /// with the damage and cannot miss a strip-causing pickup. The item may already
    /// be despawned by this point (PUaH SplitOff de-spawns full stacks before the
    /// add), but designations are keyed by Thing reference and survive despawn, so
    /// we look them up across all maps just as the vanilla strip loop does.
    ///
    /// Read-only: never alters NotifyAdded's behavior; the vanilla strip still runs.
    /// Always active — keeps R4 marks alive across any inventory-hauling mod
    /// (Pick Up And Haul, Hauler's Dream, …) without naming or detecting one.
    /// </summary>
    [HarmonyPatch(typeof(ThingOwner), "NotifyAdded", new Type[] { typeof(Thing) })]
    public static class Patch_ThingOwner_NotifyAdded_CaptureDesignations
    {
        [HarmonyPrefix]
        public static void Prefix(ThingOwner __instance, Thing item)
        {
            if (item == null || !(__instance.Owner is Pawn_InventoryTracker))
                return;

            var tracker = R4HauledDesignationTracker.Instance;
            if (tracker == null)
                return;

            List<DesignationDef> found = null;
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                DesignationManager dm = maps[m].designationManager;
                CollectR4(dm, item, R4DefOf.R4_Repair, ref found);
                CollectR4(dm, item, R4DefOf.R4_Recycle, ref found);
                CollectR4(dm, item, R4DefOf.R4_Clean, ref found);
            }

            if (found != null)
                tracker.Capture(item.thingIDNumber, found);
        }

        private static void CollectR4(DesignationManager dm, Thing item, DesignationDef def, ref List<DesignationDef> found)
        {
            if (def == null)
                return;
            if (dm.DesignationOn(item, def) != null)
            {
                if (found == null)
                    found = new List<DesignationDef>(3);
                found.Add(def);
            }
        }
    }
}
