using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Mod settings — persisted in the config folder.
    /// </summary>
    public class RRRR_Settings : ModSettings
    {
        // Recycle settings
        public float recycleGlobalMult = 1.0f;
        public bool skipIntricateComponents = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref recycleGlobalMult, "recycleGlobalMult", 1.0f);
            Scribe_Values.Look(ref skipIntricateComponents, "skipIntricateComponents", true);
        }

        public void ResetToDefaults()
        {
            recycleGlobalMult = 1.0f;
            skipIntricateComponents = true;
        }
    }

    /// <summary>
    /// Mod entry point for settings UI. Loads very early — do NOT reference Defs here.
    /// </summary>
    public class RRRR_Mod : Mod
    {
        public static RRRR_Settings Settings { get; private set; }

        public RRRR_Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RRRR_Settings>();
            Log.Message("[R4] Mod constructor fired. Settings loaded.");
        }

        public override string SettingsCategory() => "R4: Reduce, Reuse, Recycle";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label("R4_Settings_RecycleHeader".Translate());
            listing.GapLine();

            // Global recycle multiplier
            listing.Label("R4_Settings_RecycleMult".Translate() + $": {Settings.recycleGlobalMult:P0}");
            Settings.recycleGlobalMult = listing.Slider(Settings.recycleGlobalMult, 0.1f, 2.0f);

            // Skip intricate components
            listing.CheckboxLabeled(
                "R4_Settings_SkipIntricate".Translate(),
                ref Settings.skipIntricateComponents,
                "R4_Settings_SkipIntricate_Tip".Translate());

            listing.GapLine();
            if (listing.ButtonText("R4_Settings_Reset".Translate()))
            {
                Settings.ResetToDefaults();
            }

            listing.End();
        }
    }
}
