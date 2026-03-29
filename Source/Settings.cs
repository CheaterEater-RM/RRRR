using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Mod settings — persisted in the config folder.
    ///
    /// All cost formulas derive from these values rather than hardcoding numbers,
    /// so players (or future XML patching) can adjust behaviour without recompiling.
    ///
    /// Derived values (computed properties, not stored):
    ///   RepairCyclesFull  = Ceiling(1 / repairHpPerCycle)  — how many cycles to fully repair
    ///   RepairCostDivisor = RepairCyclesFull * 2           — targets ~50% of make cost total
    ///   CleanCostDivisor  = Round(1 / cleanCostFraction)   — denominator for one clean operation
    /// </summary>
    public class RRRR_Settings : ModSettings
    {
        // ── Debug ─────────────────────────────────────────────────────────────
        /// <summary>If true, verbose [R4] debug messages are written to the log.</summary>
        public bool debugLogging = false;

        // ── Recycle ───────────────────────────────────────────────────────────
        /// <summary>Scalar applied to all recycled material yields. 1.0 = default.</summary>
        public float recycleGlobalMult = 1.0f;

        /// <summary>If true, intricate components are excluded from recycle returns.</summary>
        public bool skipIntricateComponents = false;

        // ── Repair ────────────────────────────────────────────────────────────
        /// <summary>
        /// Fraction of max HP restored per repair cycle. Default 0.20 (20%).
        /// Drives cycle count: RepairCyclesFull = Ceiling(1.0 / repairHpPerCycle).
        /// At 20%: 5 cycles to full. At 10%: 10 cycles. At 25%: 4 cycles.
        /// </summary>
        public float repairHpPerCycle = 0.20f;

        // ── Clean ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Material cost for one cleaning operation as a fraction of make cost.
        /// Default 0.20 (20%) — equivalent to one repair cycle.
        /// CleanCostDivisor = Round(1.0 / cleanCostFraction).
        /// At 20%: divisor 5. At 10%: divisor 10.
        /// </summary>
        public float cleanCostFraction = 0.20f;

        // ── Derived (computed, not stored) ────────────────────────────────────

        /// <summary>How many repair cycles are needed to fully restore an item.</summary>
        public int RepairCyclesFull =>
            Mathf.Max(1, Mathf.CeilToInt(1.0f / Mathf.Clamp(repairHpPerCycle, 0.05f, 1.0f)));

        /// <summary>
        /// Divisor used in per-cycle cost formula: Ceiling(baseCost / RepairCostDivisor).
        /// = RepairCyclesFull * 2, targeting ~50% of make cost over all cycles.
        /// </summary>
        public int RepairCostDivisor => RepairCyclesFull * 2;

        /// <summary>
        /// Divisor used in clean cost formula: Ceiling(baseCost / CleanCostDivisor).
        /// = Round(1.0 / cleanCostFraction).
        /// </summary>
        public int CleanCostDivisor =>
            Mathf.Max(1, Mathf.RoundToInt(1.0f / Mathf.Clamp(cleanCostFraction, 0.05f, 1.0f)));

        // ── Persistence ───────────────────────────────────────────────────────

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref debugLogging,            "debugLogging",            false);
            Scribe_Values.Look(ref recycleGlobalMult,       "recycleGlobalMult",       1.0f);
            Scribe_Values.Look(ref skipIntricateComponents, "skipIntricateComponents", true);
            Scribe_Values.Look(ref repairHpPerCycle,        "repairHpPerCycle",        0.20f);
            Scribe_Values.Look(ref cleanCostFraction,       "cleanCostFraction",       0.20f);
        }

        public void ResetToDefaults()
        {
            debugLogging            = false;
            recycleGlobalMult       = 1.0f;
            skipIntricateComponents = true;
            repairHpPerCycle        = 0.20f;
            cleanCostFraction       = 0.20f;
        }
    }

    /// <summary>
    /// Mod entry point. Loads very early — do NOT reference Defs here.
    /// </summary>
    public class RRRR_Mod : Mod
    {
        public static RRRR_Settings Settings { get; private set; }

        public RRRR_Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RRRR_Settings>();
        }

        public override string SettingsCategory() => "R4: Reduce, Reuse, Recycle, Repair";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            // ── Recycle ───────────────────────────────────────────────────────
            listing.Label("R4_Settings_RecycleHeader".Translate());
            listing.GapLine();

            listing.Label("R4_Settings_RecycleMult".Translate()
                + $": {Settings.recycleGlobalMult:P0}");
            Settings.recycleGlobalMult = listing.Slider(Settings.recycleGlobalMult, 0.1f, 2.0f);

            listing.CheckboxLabeled(
                "R4_Settings_SkipIntricate".Translate(),
                ref Settings.skipIntricateComponents,
                "R4_Settings_SkipIntricate_Tip".Translate());

            listing.Gap();

            // ── Repair ────────────────────────────────────────────────────────
            listing.Label("R4_Settings_RepairHeader".Translate());
            listing.GapLine();

            int cycles = Settings.RepairCyclesFull;
            listing.Label("R4_Settings_RepairHpPerCycle".Translate()
                + $": {Settings.repairHpPerCycle:P0}"
                + "  ("
                + "R4_Settings_RepairCyclesNote".Translate(cycles)
                + ")");
            Settings.repairHpPerCycle = listing.Slider(Settings.repairHpPerCycle, 0.05f, 0.50f);

            listing.Gap();

            // ── Clean ─────────────────────────────────────────────────────────
            listing.Label("R4_Settings_CleanHeader".Translate());
            listing.GapLine();

            listing.Label("R4_Settings_CleanCostFraction".Translate()
                + $": {Settings.cleanCostFraction:P0}");
            Settings.cleanCostFraction = listing.Slider(Settings.cleanCostFraction, 0.05f, 0.50f);

            listing.Gap();

            // ── Debug ─────────────────────────────────────────────────────────
            listing.Label("R4_Settings_DebugHeader".Translate());
            listing.GapLine();

            listing.CheckboxLabeled(
                "R4_Settings_DebugLogging".Translate(),
                ref Settings.debugLogging,
                "R4_Settings_DebugLogging_Tip".Translate());

            listing.GapLine();
            if (listing.ButtonText("R4_Settings_Reset".Translate()))
                Settings.ResetToDefaults();

            listing.End();
        }
    }
}
