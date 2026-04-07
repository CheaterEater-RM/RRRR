using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Prevent vanilla WorkGiver_DoBill from issuing R4 repair or clean jobs in
    /// parallel with the custom R4 bill WorkGivers. Recycle continues to use
    /// the vanilla DoBill pipeline.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_WorkGiver_DoBill_Repair
    {
        static void Postfix(ref Job __result)
        {
            System.Type workerClass = __result?.bill?.recipe?.workerClass;
            if (workerClass != typeof(RecipeWorker_R4Repair) &&
                workerClass != typeof(RecipeWorker_R4Clean))
                return;

            __result = null;
        }
    }
}