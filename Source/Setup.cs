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

            // Verify our DesignationDef loaded
            var recycleDes = DefDatabase<DesignationDef>.GetNamedSilentFail("R4_Recycle");
            if (recycleDes != null)
            {
                Log.Message("[R4] DesignationDef 'R4_Recycle' loaded OK.");
            }
            else
            {
                Log.Error("[R4] DesignationDef 'R4_Recycle' NOT FOUND! Check 1.6/Defs/Designations.xml");
            }

            // Count how many ThingDefs got our comp injected
            int compCount = 0;
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.comps != null)
                {
                    foreach (var comp in def.comps)
                    {
                        if (comp is CompProperties_Recyclable)
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
    }
}
