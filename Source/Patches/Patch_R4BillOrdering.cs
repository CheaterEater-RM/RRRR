using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Lets the active DoBill-style workgiver choose the first runnable
    /// vanilla/recycle bill, then substitutes a custom R4 repair/clean job
    /// only when one sits above the chosen bill in the same stack.
    ///
    /// Harmony patches the base WorkGiver_DoBill.JobOnThing plus any loaded
    /// overrides on WorkGiver_DoBill subclasses so modded benches using a
    /// custom DoBill implementation still participate in the same bill-order
    /// contract.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_R4BillOrdering
    {
        private static readonly System.Type[] JobOnThingArgs =
        {
            typeof(Pawn),
            typeof(Thing),
            typeof(bool),
        };

        static IEnumerable<MethodBase> TargetMethods()
        {
            var seen = new HashSet<MethodBase>();

            foreach (System.Type type in GenTypes.AllTypes)
            {
                if (type == null || type.IsAbstract || !typeof(WorkGiver_DoBill).IsAssignableFrom(type))
                    continue;

                MethodInfo method = AccessTools.Method(type, nameof(WorkGiver_DoBill.JobOnThing), JobOnThingArgs);
                if (!IsPatchedJobOnThingMethod(method))
                    continue;

                if (seen.Add(method))
                    yield return method;
            }
        }

        public static bool SupportsWorkGiver(System.Type giverClass)
        {
            if (giverClass == null || !typeof(WorkGiver_DoBill).IsAssignableFrom(giverClass))
                return false;

            MethodInfo method = AccessTools.Method(giverClass, nameof(WorkGiver_DoBill.JobOnThing), JobOnThingArgs);
            return IsPatchedJobOnThingMethod(method);
        }

        private static bool IsPatchedJobOnThingMethod(MethodInfo method)
        {
            return method?.DeclaringType != null &&
                   typeof(WorkGiver_DoBill).IsAssignableFrom(method.DeclaringType);
        }

        private static bool IsBaseVanillaJobOnThing(MethodBase method)
        {
            return method?.DeclaringType == typeof(WorkGiver_DoBill);
        }

        [HarmonyPriority(Priority.Last)]
        static void Postfix(
            WorkGiver_DoBill __instance,
            ref Job __result,
            MethodBase __originalMethod,
            bool __runOriginal,
            [HarmonyArgument(0)] Pawn pawn,
            [HarmonyArgument(1)] Thing thing,
            [HarmonyArgument(2)] bool forced)
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
                            $"Skipped R4 bill ordering: DoBill returned a bill outside this stack for pawn={pawn.LabelShort} bench={thing?.def?.defName ?? "null"} bill={__result.bill.recipe?.defName ?? "null"}.");
                    }

                    return;
                }
            }
            else if (!__runOriginal)
            {
                if (R4Log.DebugEnabled)
                {
                    R4Log.Debug(
                        $"Skipped R4 bill ordering null fallback because the original DoBill method did not run: pawn={pawn.LabelShort} bench={thing?.def?.defName ?? "null"} workGiver={__instance?.GetType().FullName ?? "null"}.");
                }

                return;
            }
            else if (!IsBaseVanillaJobOnThing(__originalMethod))
            {
                if (R4Log.DebugEnabled)
                {
                    R4Log.Debug(
                        $"Skipped R4 bill ordering null fallback for custom DoBill override: pawn={pawn.LabelShort} bench={thing?.def?.defName ?? "null"} workGiver={__instance?.GetType().FullName ?? "null"}.");
                }

                return;
            }
            else if (!R4BillJobFactory.PassesVanillaBenchPrechecks(__instance, pawn, thing, forced))
            {
                return;
            }
            else
            {
                billGiver.BillStack.RemoveIncompletableBills();
            }

            Job r4Job = R4BillJobFactory.TryDispatchAboveIndex(
                pawn, thing, billGiver, vanillaIndex, forced);
            if (r4Job != null)
                __result = r4Job;
        }
    }
}
