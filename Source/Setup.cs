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
            Log.Message("[R4] === R4 Startup Begin ===");

            Log.Message("[R4] Applying Harmony patches...");
            var harmony = new Harmony("com.cheatereater.rrrr");
            harmony.PatchAll();
            Log.Message("[R4] Harmony patches applied successfully.");

            // Verify designations and jobs
            VerifyDef<DesignationDef>("R4_Recycle");
            VerifyDef<DesignationDef>("R4_Repair");
            VerifyDef<DesignationDef>("R4_Clean");
            VerifyDef<JobDef>("RRRR_Recycle");
            VerifyDef<JobDef>("RRRR_Repair");
            VerifyDef<JobDef>("RRRR_Clean");

            // Verify bill recipes
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

            Log.Message("[R4] Building workbench filter cache...");
            RuntimeHelpers.RunClassConstructor(typeof(R4WorkbenchFilterCache).TypeHandle);

            Log.Message("[R4] === R4 Startup Complete ===");
        }

        private static void VerifyDef<T>(string defName) where T : Def
        {
            var def = DefDatabase<T>.GetNamedSilentFail(defName);
            if (def != null)
                Log.Message($"[R4] {typeof(T).Name} '{defName}' loaded OK.");
            else
                Log.Error($"[R4] {typeof(T).Name} '{defName}' NOT FOUND!");
        }
    }
}
