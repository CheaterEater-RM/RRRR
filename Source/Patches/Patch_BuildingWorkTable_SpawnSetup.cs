using HarmonyLib;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Postfix on Building_WorkTable.SpawnSetup to clean up leftover smelter bills
    /// on saved benches.
    ///
    /// This handles save-compatibility: <see cref="R4WorkbenchFilterCache.StripSmelterRecipes"/>
    /// removes SmeltWeapon / SmeltApparel / SmeltOrDestroyThing from benches at
    /// startup, but saves created before that have a serialised BillStack that still
    /// holds those bills — they survive load because the RecipeDef itself still
    /// exists in the game. SpawnSetup runs on every load and fresh placement, so
    /// this postfix catches both cases.
    ///
    /// The removal is scoped to exactly the recipes StripSmelterRecipes strips
    /// (<see cref="R4WorkbenchFilterCache.IsStrippedSmelterRecipe"/>) — never any
    /// other bill. We deliberately do NOT validate against def.AllRecipes: that
    /// list is a lazily-cached collection other mods (e.g. VEF recipe sharing)
    /// rebuild incompletely, which would cause us to delete legitimate bills.
    ///
    /// Both the strip and this cleanup are gated by the stripSmelterBills setting,
    /// so players who keep vanilla smelting never have their smelt bills removed.
    /// </summary>
    [HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.SpawnSetup))]
    public static class Patch_BuildingWorkTable_SpawnSetup
    {
        static void Postfix(Building_WorkTable __instance)
        {
            BillStack stack = __instance.billStack;
            if (stack == null || stack.Count == 0)
                return;

            bool stripSmelter = RRRR_Mod.StripSmelterBillsThisSession;

            for (int i = stack.Count - 1; i >= 0; i--)
            {
                Bill bill = stack[i];
                if (bill?.recipe == null)
                    continue;
                if (stripSmelter && R4WorkbenchFilterCache.IsStrippedSmelterRecipe(bill.recipe))
                {
                    R4Log.Debug($"Removing stale smelter bill '{bill.recipe.defName}' " +
                                $"from {__instance.def.defName} (smelting replaced by R4 recycling).");
                    stack.Delete(bill);
                    continue;
                }

                if (bill is Bill_Production production &&
                    R4BillJobFactory.IsR4RepairOrClean(production.recipe) &&
                    production.repeatMode == BillRepeatModeDefOf.TargetCount &&
                    (production.recipe.WorkerCounter == null ||
                     !production.recipe.WorkerCounter.CanCountProducts(production)))
                {
                    production.repeatMode = BillRepeatModeDefOf.RepeatCount;
                    if (production.repeatCount < 1)
                        production.repeatCount = 1;
                    production.paused = false;

                    R4Log.Debug($"Reset invalid target-count R4 bill '{production.recipe.defName}' " +
                                $"on {__instance.def.defName} to repeat count mode.");
                }
            }
        }
    }
}
