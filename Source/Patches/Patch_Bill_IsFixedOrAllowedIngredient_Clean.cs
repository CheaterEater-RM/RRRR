using HarmonyLib;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Apply R4 bill-only ingredient restrictions at the same vanilla
    /// ingredient-validation hook used by WorkGiver_DoBill.
    ///
    /// Current rules:
    /// - R4 clean bills only accept corpse-tainted apparel.
    /// - R4 repair and clean bills reject recycle-designated items.
    /// - R4 recycle bills reject repair- or clean-designated items.
    /// </summary>
    [HarmonyPatch(typeof(Bill), nameof(Bill.IsFixedOrAllowedIngredient), new[] { typeof(Thing) })]
    public static class Patch_Bill_IsFixedOrAllowedIngredient_Clean
    {
        static void Postfix(Bill __instance, Thing thing, ref bool __result)
        {
            if (!__result)
                return;
            if (__instance?.recipe?.workerClass == null || thing?.Map == null)
                return;

            System.Type workerClass = __instance.recipe.workerClass;
            DesignationManager designationManager = thing.Map.designationManager;

            if (workerClass == typeof(RecipeWorker_R4Clean))
            {
                __result = thing is Apparel apparel && apparel.WornByCorpse;
                if (!__result)
                    return;
            }

            if (workerClass == typeof(RecipeWorker_R4Repair) || workerClass == typeof(RecipeWorker_R4Clean))
            {
                if (designationManager.DesignationOn(thing, R4DefOf.R4_Recycle) != null)
                {
                    __result = false;
                    return;
                }
            }

            if (workerClass == typeof(RecipeWorker_R4Recycle))
            {
                if (designationManager.DesignationOn(thing, R4DefOf.R4_Repair) != null ||
                    designationManager.DesignationOn(thing, R4DefOf.R4_Clean) != null)
                {
                    __result = false;
                }
            }
        }
    }
}