using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Skill-related calculations for repair: tech difficulty, success chance,
    /// and failure severity. Uses formulas from DESIGN.md.
    /// </summary>
    public static class SkillUtility
    {
        // Tech difficulty multipliers indexed by TechLevel ordinal
        // Undefined=1.0, Animal=0.8, Neolithic=0.8, Medieval=0.9, Industrial=1.0,
        // Spacer=1.3, Ultra=1.6, Archotech=2.0
        private static readonly float[] TechDifficultyMap =
        {
            1.00f, // Undefined
            0.80f, // Animal
            0.80f, // Neolithic
            0.90f, // Medieval
            1.00f, // Industrial
            1.30f, // Spacer
            1.60f, // Ultra
            2.00f  // Archotech
        };

        /// <summary>
        /// Get the tech difficulty multiplier for an item based on its research prerequisite.
        /// Higher = harder to repair. Falls back to Industrial (1.0) if unknown.
        /// </summary>
        public static float GetTechDifficulty(ThingDef def)
        {
            TechLevel level = TechLevel.Undefined;

            // Try the single research prerequisite first
            if (def.recipeMaker?.researchPrerequisite != null)
            {
                level = def.recipeMaker.researchPrerequisite.techLevel;
            }
            // Then try the list
            else if (def.recipeMaker?.researchPrerequisites != null)
            {
                // Use the highest tech level among prerequisites
                for (int i = 0; i < def.recipeMaker.researchPrerequisites.Count; i++)
                {
                    var rp = def.recipeMaker.researchPrerequisites[i];
                    if (rp != null && rp.techLevel > level)
                        level = rp.techLevel;
                }
            }

            // Fallback: Industrial for unknown items
            if (level == TechLevel.Undefined)
                level = TechLevel.Industrial;

            int idx = (int)level;
            if (idx >= 0 && idx < TechDifficultyMap.Length)
                return TechDifficultyMap[idx] * RRRR_Mod.Settings.repairTechDifficultyMult;

            return 1.0f * RRRR_Mod.Settings.repairTechDifficultyMult;
        }

        /// <summary>
        /// Success chance per repair cycle.
        /// Formula: (0.50 + skill × 0.025) / techDifficulty
        /// Clamped to [0.05, 1.0].
        /// </summary>
        public static float RepairSuccessChance(int skillLevel, float techDifficulty)
        {
            float baseChance = 0.50f + skillLevel * 0.025f;
            float chance = baseChance / Mathf.Max(techDifficulty, 0.1f);
            return Mathf.Clamp(chance, 0.05f, 1.0f);
        }

        /// <summary>
        /// Determine failure severity. Returns true for critical failure.
        /// Critical failures only happen when item HP is below 50%.
        /// Critical = 20% of failures when below 50% HP.
        /// </summary>
        public static bool IsCriticalFailure(Thing item)
        {
            if (!item.def.useHitPoints)
                return false;

            if (item.MaxHitPoints <= 0)
                return false;

            float hpRatio = (float)item.HitPoints / item.MaxHitPoints;
            if (hpRatio >= 0.50f)
                return false;

            // 20% chance of critical when below 50% HP
            return Rand.Chance(0.20f);
        }

        /// <summary>
        /// Apply minor failure: 5% HP loss.
        /// </summary>
        public static void ApplyMinorFailure(Thing item)
        {
            if (!item.def.useHitPoints)
                return;

            int hpLoss = Mathf.Max(1, Mathf.RoundToInt(item.MaxHitPoints * 0.05f));
            item.HitPoints = Mathf.Max(0, item.HitPoints - hpLoss);
        }

        /// <summary>
        /// Apply critical failure: 15% HP loss + quality degradation.
        /// </summary>
        public static void ApplyCriticalFailure(Thing item)
        {
            if (!item.def.useHitPoints)
                return;

            // 15% HP loss
            int hpLoss = Mathf.Max(1, Mathf.RoundToInt(item.MaxHitPoints * 0.15f));
            item.HitPoints = Mathf.Max(0, item.HitPoints - hpLoss);

            // Quality degradation — drop one level
            CompQuality compQuality = item.TryGetComp<CompQuality>();
            if (compQuality != null && compQuality.Quality > QualityCategory.Awful)
            {
                QualityCategory newQuality = compQuality.Quality - 1;
                compQuality.SetQuality(newQuality, null);
            }
        }

        /// <summary>
        /// Calculate material cost for one repair cycle.
        /// Formula: (cycleHP / maxHP) × baseCost × 1.2 × techDifficulty
        /// Returns the count for a single material entry.
        /// </summary>
        public static int RepairCycleMaterialCost(int baseCost, int maxHP, int cycleHP, float techDifficulty)
        {
            float ratio = (float)cycleHP / Mathf.Max(maxHP, 1);
            float cost = baseCost * ratio * 1.2f * techDifficulty;
            return Mathf.Max(1, GenMath.RoundRandom(cost));
        }
    }
}
