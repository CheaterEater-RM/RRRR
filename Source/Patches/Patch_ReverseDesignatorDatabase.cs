using HarmonyLib;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Postfix on ReverseDesignatorDatabase.InitDesignators to inject our
    /// designator into the gizmo list (the buttons that appear when you
    /// select an item on the map).
    /// 
    /// This is separate from the Orders menu injection (which is XML-based).
    /// Both are needed: Orders menu = drag-to-designate, gizmo = click-on-selected-item.
    /// </summary>
    [HarmonyPatch(typeof(ReverseDesignatorDatabase), "InitDesignators")]
    public static class Patch_ReverseDesignatorDatabase_InitDesignators
    {
        static void Postfix(ReverseDesignatorDatabase __instance)
        {
            Log.Message("[R4] ReverseDesignatorDatabase.InitDesignators postfix firing...");

            // Access the private desList field
            var desList = Traverse.Create(__instance).Field("desList").GetValue<System.Collections.Generic.List<Designator>>();
            if (desList == null)
            {
                Log.Error("[R4] Could not access desList in ReverseDesignatorDatabase!");
                return;
            }

            var recycleDesignator = new Designator_RecycleThing();
            desList.Add(recycleDesignator);
            Log.Message($"[R4] Injected Designator_RecycleThing into ReverseDesignatorDatabase. Total designators: {desList.Count}");
        }
    }
}
