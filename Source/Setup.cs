using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Harmony patch entry point. Fires after all Defs are loaded.
    /// Also triggers the ThingDef cache build.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RRRR_Init
    {
        static RRRR_Init()
        {
            Log.Message("[R4] === R4 Startup Begin ===");

            // Apply Harmony patches
            Log.Message("[R4] Applying Harmony patches...");
            var harmony = new Harmony("com.cheatereater.rrrr");
            harmony.PatchAll();
            Log.Message("[R4] Harmony patches applied successfully.");

            // Verify defs loaded
            VerifyDef<DesignationDef>("R4_Recycle");
            VerifyDef<DesignationDef>("R4_Repair");
            VerifyDef<JobDef>("RRRR_Recycle");
            VerifyDef<JobDef>("RRRR_Repair");

            // Trigger the ThingDef cache build explicitly
            Log.Message("[R4] Building ThingDef cache...");
            RuntimeHelpers.RunClassConstructor(typeof(R4ThingDefCache).TypeHandle);

            // Count how many ThingDefs got our comp injected
            int compCount = 0;
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.comps != null)
                {
                    for (int i = 0; i < def.comps.Count; i++)
                    {
                        if (def.comps[i] is CompProperties_Recyclable)
                        {
                            compCount++;
                            break;
                        }
                    }
                }
            }
            Log.Message($"[R4] CompProperties_Recyclable found on {compCount} ThingDefs.");

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
