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

        // Repair settings
        public float repairTechDifficultyMult = 1.0f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref recycleGlobalMult, "recycleGlobalMult", 1.0f);
            Scribe_Values.Look(ref skipIntricateComponents, "skipIntricateComponents", true);
            Scribe_Values.Look(ref repairTechDifficultyMult, "repairTechDifficultyMult", 1.0f);
        }

        public void ResetToDefaults()
        {
            recycleGlobalMult = 1.0f;
            skipIntricateComponents = true;
            repairTechDifficultyMult = 1.0f;
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

            // --- Recycle ---
            listing.Label("R4_Settings_RecycleHeader".Translate());
            listing.GapLine();

            listing.Label("R4_Settings_RecycleMult".Translate() + $": {Settings.recycleGlobalMult:P0}");
            Settings.recycleGlobalMult = listing.Slider(Settings.recycleGlobalMult, 0.1f, 2.0f);

            listing.CheckboxLabeled(
                "R4_Settings_SkipIntricate".Translate(),
                ref Settings.skipIntricateComponents,
                "R4_Settings_SkipIntricate_Tip".Translate());

            listing.Gap();

            // --- Repair ---
            listing.Label("R4_Settings_RepairHeader".Translate());
            listing.GapLine();

            listing.Label("R4_Settings_RepairTechDifficulty".Translate() + $": {Settings.repairTechDifficultyMult:P0}");
            Settings.repairTechDifficultyMult = listing.Slider(Settings.repairTechDifficultyMult, 0.5f, 2.0f);

            listing.GapLine();
            if (listing.ButtonText("R4_Settings_Reset".Translate()))
            {
                Settings.ResetToDefaults();
            }

            listing.End();
        }
    }
}
