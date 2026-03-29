using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Postfix on Building_WorkTable.SpawnSetup to strip any bills whose recipe
    /// is no longer available on that bench.
    ///
    /// This handles save-compatibility: when VanillaSmelting.xml removes
    /// SmeltWeapon / SmeltApparel / SmeltOrDestroyThing from the electric smelter,
    /// the ThingDef's AllRecipes list no longer contains those recipes. But saves
    /// that were created before this mod was added have a serialised BillStack that
    /// still holds those bills — they survive load because the RecipeDef itself
    /// still exists in the game. SpawnSetup runs on every load and fresh placement,
    /// so this postfix catches both cases.
    ///
    /// We only remove a bill if:
    ///   1. Its recipe is not null (null recipes are already stripped by BillStack.ExposeData).
    ///   2. The recipe is not in this bench's AllRecipes list.
    ///
    /// This is equivalent to what vanilla's ITab_Bills already does for new bill
    /// creation — it only shows recipes in AllRecipes — so removing existing bills
    /// that no longer belong is the correct mirror operation.
    /// </summary>
    [HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.SpawnSetup))]
    public static class Patch_BuildingWorkTable_SpawnSetup
    {
        static void Postfix(Building_WorkTable __instance)
        {
            // AllRecipes is the authoritative list of what a bench can do
            List<RecipeDef> allowed = __instance.def.AllRecipes;
            if (allowed == null)
                return;

            BillStack stack = __instance.billStack;
            if (stack == null || stack.Count == 0)
                return;

            for (int i = stack.Count - 1; i >= 0; i--)
            {
                Bill bill = stack[i];
                if (bill?.recipe == null)
                    continue;
                if (!allowed.Contains(bill.recipe))
                {
                    Log.Message($"[R4] Removing stale bill '{bill.recipe.defName}' " +
                                $"from {__instance.def.defName} (recipe no longer available on this bench).");
                    stack.Delete(bill);
                }
            }
        }
    }
}
