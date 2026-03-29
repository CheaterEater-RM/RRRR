using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RRRR
{
    [StaticConstructorOnStartup]
    public static class RRRR_Init
    {
        static RRRR_Init()
        {
            var harmony = new Harmony("com.cheatereater.rrrr");
            harmony.PatchAll();

            // Verify required defs — errors are always shown regardless of debug setting
            VerifyDef<DesignationDef>("R4_Recycle");
            VerifyDef<DesignationDef>("R4_Repair");
            VerifyDef<DesignationDef>("R4_Clean");
            VerifyDef<JobDef>("RRRR_Recycle");
            VerifyDef<JobDef>("RRRR_Repair");
            VerifyDef<JobDef>("RRRR_Clean");
            VerifyDef<RecipeDef>("RRRR_Repair_CraftingSpot");
            VerifyDef<RecipeDef>("RRRR_Repair_Tailor");
            VerifyDef<RecipeDef>("RRRR_Repair_Smithy");
            VerifyDef<RecipeDef>("RRRR_Repair_Machining");
            VerifyDef<RecipeDef>("RRRR_Repair_Fabrication");
            VerifyDef<RecipeDef>("RRRR_Recycle_CraftingSpot");
            VerifyDef<RecipeDef>("RRRR_Recycle_Tailor");
            VerifyDef<RecipeDef>("RRRR_Recycle_Smithy");
            VerifyDef<RecipeDef>("RRRR_Recycle_Machining");
            VerifyDef<RecipeDef>("RRRR_Recycle_Fabrication");
            VerifyDef<RecipeDef>("RRRR_Clean_CraftingSpot");
            VerifyDef<RecipeDef>("RRRR_Clean_Tailor");

            RuntimeHelpers.RunClassConstructor(typeof(R4WorkbenchFilterCache).TypeHandle);
        }

        private static void VerifyDef<T>(string defName) where T : Def
        {
            var def = DefDatabase<T>.GetNamedSilentFail(defName);
            if (def == null)
                R4Log.Error($"{typeof(T).Name} '{defName}' NOT FOUND — mod may not function correctly.");
        }
    }
}
