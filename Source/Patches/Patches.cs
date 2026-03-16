using HarmonyLib;
using Verse;

namespace RRRR.Patches
{
    /// <summary>
    /// Fallback comp injection: ensures all apparel and weapons get CompRecyclable
    /// even if XML patches miss dynamically generated ThingDefs from other mods.
    /// </summary>
    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.ResolveReferences))]
    public static class ThingDef_ResolveReferences_Patch
    {
        static void Postfix(ThingDef __instance)
        {
            if (__instance.comps == null)
                return;

            if (!__instance.IsApparel && !__instance.IsWeapon)
                return;

            // Check if already has CompRecyclable
            foreach (var comp in __instance.comps)
            {
                if (comp is CompProperties_Recyclable)
                    return;
            }

            __instance.comps.Add(new CompProperties_Recyclable());
        }
    }
}
