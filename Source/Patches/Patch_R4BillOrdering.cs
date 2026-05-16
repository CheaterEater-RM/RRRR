using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Lets vanilla choose the first runnable vanilla/recycle bill, then
    /// substitutes a custom R4 repair/clean job only when one sits above
    /// vanilla's chosen bill in the same stack.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_R4BillOrdering
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(
            WorkGiver_DoBill __instance,
            ref Job __result,
            Pawn pawn,
            Thing thing,
            bool forced)
        {
            if (!(thing is IBillGiver billGiver) ||
                !R4BillJobFactory.HasAnyR4RepairOrCleanBill(billGiver))
            {
                return;
            }

            if (__result != null && __result.bill == null)
                return;

            int vanillaIndex = int.MaxValue;
            if (__result?.bill != null)
            {
                vanillaIndex = billGiver.BillStack.IndexOf(__result.bill);
                if (vanillaIndex < 0)
                {
                    if (R4Log.DebugEnabled)
                    {
                        R4Log.Debug(
                            $"Skipped R4 bill ordering: vanilla returned a bill outside this stack for pawn={pawn.LabelShort} bench={thing?.def?.defName ?? "null"} bill={__result.bill.recipe?.defName ?? "null"}.");
                    }

                    return;
                }
            }
            else if (!R4BillJobFactory.PassesVanillaBenchPrechecks(__instance, pawn, thing, forced))
            {
                return;
            }

            Job r4Job = R4BillJobFactory.TryDispatchAboveIndex(
                pawn, thing, billGiver, vanillaIndex, forced);
            if (r4Job != null)
                __result = r4Job;
        }
    }
}
