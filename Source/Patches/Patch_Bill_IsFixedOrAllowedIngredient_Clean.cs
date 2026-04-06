using HarmonyLib;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Restrict R4 clean bills to corpse-tainted apparel at the same vanilla
    /// ingredient-validation hook used by WorkGiver_DoBill. This prevents an
    /// item that has just been cleaned from immediately qualifying again.
    /// </summary>
    [HarmonyPatch(typeof(Bill), nameof(Bill.IsFixedOrAllowedIngredient), new[] { typeof(Thing) })]
    public static class Patch_Bill_IsFixedOrAllowedIngredient_Clean
    {
        static void Postfix(Bill __instance, Thing thing, ref bool __result)
        {
            if (!__result)
                return;
            if (__instance?.recipe?.workerClass != typeof(RecipeWorker_R4Clean))
                return;

            __result = thing is Apparel apparel && apparel.WornByCorpse;
        }
    }
}