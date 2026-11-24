using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Skill-related calculations for repair: the odds curve, critical share,
    /// and failure severity.
    /// </summary>
    public static class SkillUtility
    {
        // Indexed by TechLevel ordinal: Undefined, Animal, Neolithic, Medieval,
        // Industrial, Spacer, Ultra, Archotech. P0 is success at effective skill 0;
        // S is the effective skill at which success is certain.
        private static readonly float[] RepairP0 = { 0.60f, 0.70f, 0.70f, 0.65f, 0.60f, 0.45f, 0.35f, 0.25f };
        private static readonly float[] RepairS  = { 10f,   6f,    6f,    8f,    10f,   14f,   18f,   20f   };

        /// <summary>
        /// Per-cycle repair odds for an effective skill (Crafting level × repair aptitude)
        /// on a tech level. Pure, so the simulation tool can call it. Undefined reads as
        /// Industrial and an out-of-range value reads as Industrial.
        /// </summary>
        public static void RepairOdds(float effectiveSkill, TechLevel tier, out float success, out float criticalShare)
        {
            int idx = (int)tier;
            if (idx >= RepairP0.Length)
                idx = (int)TechLevel.Industrial;

            float p0 = RepairP0[idx];
            float f = Mathf.Clamp01(effectiveSkill / RepairS[idx]);
            success = f >= 1f ? 1f : p0 + (1f - p0) * f;
            criticalShare = 0.20f * (1f - f);
        }

        /// <summary>
        /// Returns true if a failed repair is critical: item HP below 50% and a roll at
        /// criticalShare succeeds.
        /// </summary>
        public static bool IsCriticalFailure(Thing item, float criticalShare)
        {
            if (!item.def.useHitPoints || item.MaxHitPoints <= 0)
                return false;
            if ((float)item.HitPoints / item.MaxHitPoints >= 0.50f)
                return false;
            return Rand.Chance(criticalShare);
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
