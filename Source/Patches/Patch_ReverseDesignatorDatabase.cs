using HarmonyLib;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Postfix on ReverseDesignatorDatabase.InitDesignators to inject our
    /// designators into the gizmo list (buttons that appear when selecting items).
    /// </summary>
    [HarmonyPatch(typeof(ReverseDesignatorDatabase), "InitDesignators")]
    public static class Patch_ReverseDesignatorDatabase_InitDesignators
    {
        static void Postfix(ReverseDesignatorDatabase __instance)
        {
            var desList = Traverse.Create(__instance).Field("desList")
                .GetValue<System.Collections.Generic.List<Designator>>();

            if (desList == null)
            {
                Log.Error("[R4] Could not access desList in ReverseDesignatorDatabase!");
                return;
            }

            desList.Add(new Designator_RecycleThing());
            desList.Add(new Designator_RepairThing());
            desList.Add(new Designator_CleanThing());

            Log.Message($"[R4] Injected R4 designators into ReverseDesignatorDatabase. Total: {desList.Count}");
        }
    }
}
