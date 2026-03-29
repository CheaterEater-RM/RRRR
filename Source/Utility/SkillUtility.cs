using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Skill-related calculations for repair: tech difficulty, success chance,
    /// and failure severity.
    /// </summary>
    public static class SkillUtility
    {
        // Tech difficulty multipliers indexed by TechLevel ordinal.
        // Higher = harder to repair (lower success chance).
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
        /// Get the tech difficulty multiplier for an item based on its tech level.
        /// Higher = harder to repair. Falls back to Industrial (1.0) if unknown.
        /// </summary>
        public static float GetTechDifficulty(ThingDef def)
        {
            TechLevel level = TechLevel.Undefined;

            if (def.recipeMaker?.researchPrerequisite != null)
            {
                level = def.recipeMaker.researchPrerequisite.techLevel;
            }
            else if (def.recipeMaker?.researchPrerequisites != null)
            {
                for (int i = 0; i < def.recipeMaker.researchPrerequisites.Count; i++)
                {
                    var rp = def.recipeMaker.researchPrerequisites[i];
                    if (rp != null && rp.techLevel > level)
                        level = rp.techLevel;
                }
            }

            if (level == TechLevel.Undefined)
                level = TechLevel.Industrial;

            int idx = (int)level;
            if (idx >= 0 && idx < TechDifficultyMap.Length)
                return TechDifficultyMap[idx];

            return 1.0f;
        }

        /// <summary>
        /// Success chance per repair cycle.
        /// Formula: (0.50 + skill × 0.025) / techDifficulty, clamped to [0.05, 1.0].
        /// </summary>
        public static float RepairSuccessChance(int skillLevel, float techDifficulty)
        {
            float baseChance = 0.50f + skillLevel * 0.025f;
            float chance = baseChance / Mathf.Max(techDifficulty, 0.1f);
            return Mathf.Clamp(chance, 0.05f, 1.0f);
        }

        /// <summary>
        /// Returns true if this failure should be a critical failure.
        /// Critical failures only occur when item HP is below 50%, at a 20% rate.
        /// </summary>
        public static bool IsCriticalFailure(Thing item)
        {
            if (!item.def.useHitPoints || item.MaxHitPoints <= 0)
                return false;
            if ((float)item.HitPoints / item.MaxHitPoints >= 0.50f)
                return false;
            return Rand.Chance(0.20f);
        }

        /// <summary>Apply minor failure: 5% HP loss.</summary>
        public static void ApplyMinorFailure(Thing item)
        {
            if (!item.def.useHitPoints) return;
            int hpLoss = Mathf.Max(1, Mathf.RoundToInt(item.MaxHitPoints * 0.05f));
            item.HitPoints = Mathf.Max(0, item.HitPoints - hpLoss);
        }

        /// <summary>Apply critical failure: 15% HP loss + one quality level drop.</summary>
        public static void ApplyCriticalFailure(Thing item)
        {
            if (!item.def.useHitPoints) return;
            int hpLoss = Mathf.Max(1, Mathf.RoundToInt(item.MaxHitPoints * 0.15f));
            item.HitPoints = Mathf.Max(0, item.HitPoints - hpLoss);

            CompQuality compQuality = item.TryGetComp<CompQuality>();
            if (compQuality != null && compQuality.Quality > QualityCategory.Awful)
                compQuality.SetQuality(compQuality.Quality - 1, null);
        }

        /// <summary>
        /// Calculate material cost for one repair cycle.
        /// Used as a legacy reference; main path now uses MaterialUtility.GetRepairCycleCost.
        /// </summary>
        public static int RepairCycleMaterialCost(int baseCost, int maxHP, int cycleHP, float techDifficulty)
        {
            float ratio = (float)cycleHP / Mathf.Max(maxHP, 1);
            float cost = baseCost * ratio * 1.2f * techDifficulty;
            return Mathf.Max(1, GenMath.RoundRandom(cost));
        }
    }
}
