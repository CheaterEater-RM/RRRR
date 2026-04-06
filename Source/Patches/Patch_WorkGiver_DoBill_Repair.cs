using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Prevent vanilla WorkGiver_DoBill from issuing R4 repair jobs in parallel
    /// with WorkGiver_R4RepairBill. This keeps vanilla bill search intact for
    /// every other recipe while removing the duplicate repair path that can
    /// re-queue full-health items into endless repair cycles.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_WorkGiver_DoBill_Repair
    {
        static void Postfix(ref Job __result)
        {
            if (__result?.bill?.recipe?.workerClass != typeof(RecipeWorker_R4Repair))
                return;

            __result = null;
        }
    }
}