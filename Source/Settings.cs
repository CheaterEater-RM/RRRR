using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    public class RRRRSettings : ModSettings
    {
        // ── General ─────────────────────────────────────────────────────
        public float conditionExponent = 1.8f;
        public float globalEfficiencyMultiplier = 1.0f;

        // ── Recycle ─────────────────────────────────────────────────────
        public float recycleWorkFactor = 0.5f;
        public float baseSkillReturn = 0.10f;
        public float maxSkillReturn = 1.00f;
        public float skillExponent = 0.6f;
        public float taintedReturnFraction = 0.50f;
        public bool minimumReturnGuarantee = true;
        public float componentPenalty = 0.25f;
        public float advComponentPenalty = 0.15f;
        public float chemfuelPenalty = 0.30f;

        // Quality factors — indexed by QualityCategory (int 0-6)
        public float qualityFactor_Awful = 0.60f;
        public float qualityFactor_Poor = 0.70f;
        public float qualityFactor_Normal = 0.80f;
        public float qualityFactor_Good = 0.90f;
        public float qualityFactor_Excellent = 1.00f;
        public float qualityFactor_Masterwork = 1.10f;
        public float qualityFactor_Legendary = 1.20f;

        // ── Repair ──────────────────────────────────────────────────────
        public float repairCyclePercent = 0.10f;
        public float repairCostMultiplier = 1.2f;
        public float baseRepairSuccess = 0.50f;
        public float repairSkillBonus = 0.025f;
        public float failureHPLossPercent = 0.05f;
        public float criticalFailureHPLossPercent = 0.15f;
        public float criticalFailureBaseChance = 0.20f;
        public float qualityLossThreshold = 0.50f;

        // Tech difficulty multipliers
        public float techDifficulty_Neolithic = 0.80f;
        public float techDifficulty_Medieval = 0.90f;
        public float techDifficulty_Industrial = 1.00f;
        public float techDifficulty_Spacer = 1.30f;
        public float techDifficulty_Ultra = 1.60f;
        public float techDifficulty_Archotech = 2.00f;

        // ── Clean ───────────────────────────────────────────────────────
        public float cleanCostFraction = 0.15f;
        public float cleanWorkFactor = 0.4f;

        public float GetQualityFactor(QualityCategory qc)
        {
            switch (qc)
            {
                case QualityCategory.Awful: return qualityFactor_Awful;
                case QualityCategory.Poor: return qualityFactor_Poor;
                case QualityCategory.Normal: return qualityFactor_Normal;
                case QualityCategory.Good: return qualityFactor_Good;
                case QualityCategory.Excellent: return qualityFactor_Excellent;
                case QualityCategory.Masterwork: return qualityFactor_Masterwork;
                case QualityCategory.Legendary: return qualityFactor_Legendary;
                default: return qualityFactor_Normal;
            }
        }

        public float GetTechDifficulty(TechLevel tech)
        {
            switch (tech)
            {
                case TechLevel.Neolithic: return techDifficulty_Neolithic;
                case TechLevel.Medieval: return techDifficulty_Medieval;
                case TechLevel.Industrial: return techDifficulty_Industrial;
                case TechLevel.Spacer: return techDifficulty_Spacer;
                case TechLevel.Ultra: return techDifficulty_Ultra;
                case TechLevel.Archotech: return techDifficulty_Archotech;
                default: return techDifficulty_Industrial;
            }
        }

        public void ResetToDefaults()
        {
            conditionExponent = 1.8f;
            globalEfficiencyMultiplier = 1.0f;
            recycleWorkFactor = 0.5f;
            baseSkillReturn = 0.10f;
            maxSkillReturn = 1.00f;
            skillExponent = 0.6f;
            taintedReturnFraction = 0.50f;
            minimumReturnGuarantee = true;
            componentPenalty = 0.25f;
            advComponentPenalty = 0.15f;
            chemfuelPenalty = 0.30f;
            qualityFactor_Awful = 0.60f;
            qualityFactor_Poor = 0.70f;
            qualityFactor_Normal = 0.80f;
            qualityFactor_Good = 0.90f;
            qualityFactor_Excellent = 1.00f;
            qualityFactor_Masterwork = 1.10f;
            qualityFactor_Legendary = 1.20f;
            repairCyclePercent = 0.10f;
            repairCostMultiplier = 1.2f;
            baseRepairSuccess = 0.50f;
            repairSkillBonus = 0.025f;
            failureHPLossPercent = 0.05f;
            criticalFailureHPLossPercent = 0.15f;
            criticalFailureBaseChance = 0.20f;
            qualityLossThreshold = 0.50f;
            techDifficulty_Neolithic = 0.80f;
            techDifficulty_Medieval = 0.90f;
            techDifficulty_Industrial = 1.00f;
            techDifficulty_Spacer = 1.30f;
            techDifficulty_Ultra = 1.60f;
            techDifficulty_Archotech = 2.00f;
            cleanCostFraction = 0.15f;
            cleanWorkFactor = 0.4f;
        }

        public override void ExposeData()
        {
            base.ExposeData();

            // General
            Scribe_Values.Look(ref conditionExponent, "conditionExponent", 1.8f);
            Scribe_Values.Look(ref globalEfficiencyMultiplier, "globalEfficiencyMultiplier", 1.0f);

            // Recycle
            Scribe_Values.Look(ref recycleWorkFactor, "recycleWorkFactor", 0.5f);
            Scribe_Values.Look(ref baseSkillReturn, "baseSkillReturn", 0.10f);
            Scribe_Values.Look(ref maxSkillReturn, "maxSkillReturn", 1.00f);
            Scribe_Values.Look(ref skillExponent, "skillExponent", 0.6f);
            Scribe_Values.Look(ref taintedReturnFraction, "taintedReturnFraction", 0.50f);
            Scribe_Values.Look(ref minimumReturnGuarantee, "minimumReturnGuarantee", true);
            Scribe_Values.Look(ref componentPenalty, "componentPenalty", 0.25f);
            Scribe_Values.Look(ref advComponentPenalty, "advComponentPenalty", 0.15f);
            Scribe_Values.Look(ref chemfuelPenalty, "chemfuelPenalty", 0.30f);
            Scribe_Values.Look(ref qualityFactor_Awful, "qualityFactor_Awful", 0.60f);
            Scribe_Values.Look(ref qualityFactor_Poor, "qualityFactor_Poor", 0.70f);
            Scribe_Values.Look(ref qualityFactor_Normal, "qualityFactor_Normal", 0.80f);
            Scribe_Values.Look(ref qualityFactor_Good, "qualityFactor_Good", 0.90f);
            Scribe_Values.Look(ref qualityFactor_Excellent, "qualityFactor_Excellent", 1.00f);
            Scribe_Values.Look(ref qualityFactor_Masterwork, "qualityFactor_Masterwork", 1.10f);
            Scribe_Values.Look(ref qualityFactor_Legendary, "qualityFactor_Legendary", 1.20f);

            // Repair
            Scribe_Values.Look(ref repairCyclePercent, "repairCyclePercent", 0.10f);
            Scribe_Values.Look(ref repairCostMultiplier, "repairCostMultiplier", 1.2f);
            Scribe_Values.Look(ref baseRepairSuccess, "baseRepairSuccess", 0.50f);
            Scribe_Values.Look(ref repairSkillBonus, "repairSkillBonus", 0.025f);
            Scribe_Values.Look(ref failureHPLossPercent, "failureHPLossPercent", 0.05f);
            Scribe_Values.Look(ref criticalFailureHPLossPercent, "criticalFailureHPLossPercent", 0.15f);
            Scribe_Values.Look(ref criticalFailureBaseChance, "criticalFailureBaseChance", 0.20f);
            Scribe_Values.Look(ref qualityLossThreshold, "qualityLossThreshold", 0.50f);
            Scribe_Values.Look(ref techDifficulty_Neolithic, "techDifficulty_Neolithic", 0.80f);
            Scribe_Values.Look(ref techDifficulty_Medieval, "techDifficulty_Medieval", 0.90f);
            Scribe_Values.Look(ref techDifficulty_Industrial, "techDifficulty_Industrial", 1.00f);
            Scribe_Values.Look(ref techDifficulty_Spacer, "techDifficulty_Spacer", 1.30f);
            Scribe_Values.Look(ref techDifficulty_Ultra, "techDifficulty_Ultra", 1.60f);
            Scribe_Values.Look(ref techDifficulty_Archotech, "techDifficulty_Archotech", 2.00f);

            // Clean
            Scribe_Values.Look(ref cleanCostFraction, "cleanCostFraction", 0.15f);
            Scribe_Values.Look(ref cleanWorkFactor, "cleanWorkFactor", 0.4f);
        }
    }

    public class RRRRMod : Mod
    {
        public static RRRRSettings Settings { get; private set; }

        public RRRRMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RRRRSettings>();
        }

        public override string SettingsCategory() => "RRRR_SettingsTitle".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            // ── General ─────────────────────────────────────────────────
            listing.Label("RRRR_Settings_Header_General".Translate());
            listing.Label("RRRR_Settings_ConditionExponent".Translate() + ": " + Settings.conditionExponent.ToString("F2"),
                tooltip: "RRRR_Settings_ConditionExponent_Tip".Translate());
            Settings.conditionExponent = listing.Slider(Settings.conditionExponent, 1.0f, 3.0f);
            listing.Label("RRRR_Settings_GlobalEfficiency".Translate() + ": " + Settings.globalEfficiencyMultiplier.ToString("F2"),
                tooltip: "RRRR_Settings_GlobalEfficiency_Tip".Translate());
            Settings.globalEfficiencyMultiplier = listing.Slider(Settings.globalEfficiencyMultiplier, 0.1f, 3.0f);

            listing.GapLine();

            // ── Recycle ─────────────────────────────────────────────────
            listing.Label("RRRR_Settings_Header_Recycle".Translate());
            listing.Label("RRRR_Settings_BaseSkillReturn".Translate() + ": " + Settings.baseSkillReturn.ToStringPercent());
            Settings.baseSkillReturn = listing.Slider(Settings.baseSkillReturn, 0f, 0.5f);
            listing.Label("RRRR_Settings_MaxSkillReturn".Translate() + ": " + Settings.maxSkillReturn.ToStringPercent());
            Settings.maxSkillReturn = listing.Slider(Settings.maxSkillReturn, 0.5f, 1.0f);
            listing.Label("RRRR_Settings_SkillExponent".Translate() + ": " + Settings.skillExponent.ToString("F2"),
                tooltip: "RRRR_Settings_SkillExponent_Tip".Translate());
            Settings.skillExponent = listing.Slider(Settings.skillExponent, 0.2f, 2.0f);
            listing.Label("RRRR_Settings_TaintedReturnFraction".Translate() + ": " + Settings.taintedReturnFraction.ToStringPercent(),
                tooltip: "RRRR_Settings_TaintedReturnFraction_Tip".Translate());
            Settings.taintedReturnFraction = listing.Slider(Settings.taintedReturnFraction, 0.1f, 1.0f);
            listing.CheckboxLabeled("RRRR_Settings_MinimumReturnGuarantee".Translate(), ref Settings.minimumReturnGuarantee,
                "RRRR_Settings_MinimumReturnGuarantee_Tip".Translate());

            listing.GapLine();

            // ── Repair (brief for now — full UI in M5) ──────────────────
            listing.Label("RRRR_Settings_Header_Repair".Translate());
            listing.Label("RRRR_Settings_RepairCyclePercent".Translate() + ": " + Settings.repairCyclePercent.ToStringPercent());
            Settings.repairCyclePercent = listing.Slider(Settings.repairCyclePercent, 0.05f, 0.25f);
            listing.Label("RRRR_Settings_RepairCostMultiplier".Translate() + ": " + Settings.repairCostMultiplier.ToString("F2"),
                tooltip: "RRRR_Settings_RepairCostMultiplier_Tip".Translate());
            Settings.repairCostMultiplier = listing.Slider(Settings.repairCostMultiplier, 0.5f, 3.0f);

            listing.GapLine();

            // ── Clean ───────────────────────────────────────────────────
            listing.Label("RRRR_Settings_Header_Clean".Translate());
            listing.Label("RRRR_Settings_CleanCostFraction".Translate() + ": " + Settings.cleanCostFraction.ToStringPercent(),
                tooltip: "RRRR_Settings_CleanCostFraction_Tip".Translate());
            Settings.cleanCostFraction = listing.Slider(Settings.cleanCostFraction, 0.05f, 0.50f);

            listing.GapLine();

            if (listing.ButtonText("RRRR_Settings_ResetDefaults".Translate()))
            {
                Settings.ResetToDefaults();
            }

            listing.End();
        }
    }
}
