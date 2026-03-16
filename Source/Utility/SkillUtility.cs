using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Skill check calculations for all R⁴ actions.
    /// </summary>
    public static class SkillUtility
    {
        /// <summary>
        /// Nonlinear skill factor for recycling returns.
        /// Skill 0 → baseReturn, ramps via (skill/20)^exponent, skill 20 → maxReturn.
        /// </summary>
        public static float SkillFactor(Pawn worker)
        {
            var settings = RRRRMod.Settings;
            int skill = worker?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
            float t = (float)skill / 20f;
            return settings.baseSkillReturn
                 + (settings.maxSkillReturn - settings.baseSkillReturn)
                 * Mathf.Pow(t, settings.skillExponent);
        }

        /// <summary>
        /// Repair success chance for a given pawn on a given item.
        /// </summary>
        public static float RepairSuccessChance(Pawn worker, Thing item)
        {
            var settings = RRRRMod.Settings;
            int skill = worker?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
            float baseChance = settings.baseRepairSuccess + skill * settings.repairSkillBonus;
            float techDifficulty = settings.GetTechDifficulty(item.def.techLevel);
            return Mathf.Clamp01(baseChance / techDifficulty);
        }

        /// <summary>
        /// Whether a critical failure occurs (within a failure roll).
        /// </summary>
        public static bool IsCriticalFailure(Pawn worker)
        {
            var settings = RRRRMod.Settings;
            int skill = worker?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
            float critChance = settings.criticalFailureBaseChance * (1f - (float)skill / 20f * 0.8f);
            return Rand.Value < critChance;
        }
    }
}
