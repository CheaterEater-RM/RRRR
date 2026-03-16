using HarmonyLib;
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
            Log.Message("[R⁴] Harmony patches applied.");
        }
    }
}
