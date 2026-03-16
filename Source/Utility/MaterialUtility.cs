using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// Central material calculation for all R⁴ actions.
    /// </summary>
    public static class MaterialUtility
    {
        /// <summary>
        /// Get the full base material cost of an item from its ThingDef.
        /// </summary>
        public static List<ThingDefCountClass> GetBaseMaterials(Thing item)
        {
            var result = new List<ThingDefCountClass>();
            var def = item.def;

            // Stuff-based cost (e.g., wool jacket uses costStuffCount of the actual stuff)
            if (def.MadeFromStuff && item.Stuff != null && def.costStuffCount > 0)
            {
                result.Add(new ThingDefCountClass(item.Stuff, def.costStuffCount));
            }

            // Fixed cost list (e.g., charge rifle costs steel + components)
            if (def.costList != null)
            {
                foreach (var cost in def.costList)
                {
                    result.Add(new ThingDefCountClass(cost.thingDef, cost.count));
                }
            }

            return result;
        }

        /// <summary>
        /// Calculate recycling returns for an item given a specific worker pawn.
        /// </summary>
        public static List<ThingDefCountClass> CalculateRecycleReturn(Thing item, Pawn worker)
        {
            var settings = RRRRMod.Settings;
            var baseMats = GetBaseMaterials(item);

            float condFactor = ConditionFactor(item);
            float qualFactor = QualityFactor(item);
            float skillFactor = SkillUtility.SkillFactor(worker);
            float taintPenalty = TaintPenalty(item);
            float globalMult = settings.globalEfficiencyMultiplier;

            var result = new List<ThingDefCountClass>();
            foreach (var mat in baseMats)
            {
                float rarePenalty = RareMaterialPenalty(mat.thingDef);
                float amount = mat.count * condFactor * qualFactor * skillFactor
                             * taintPenalty * rarePenalty * globalMult;

                // Clamp to base cost
                amount = Mathf.Min(amount, mat.count);

                int final = ProbabilisticRound(amount);
                if (final > 0)
                {
                    result.Add(new ThingDefCountClass(mat.thingDef, final));
                }
            }

            // Guarantee at least 1 of primary material if enabled and item has HP
            if (settings.minimumReturnGuarantee && result.Count == 0
                && baseMats.Count > 0 && item.HitPoints > 0)
            {
                result.Add(new ThingDefCountClass(baseMats[0].thingDef, 1));
            }

            return result;
        }

        /// <summary>
        /// Preview return for tooltip (assumes skill 10 pawn).
        /// </summary>
        public static List<ThingDefCountClass> GetRecycleReturnPreview(Thing item)
        {
            var settings = RRRRMod.Settings;
            var baseMats = GetBaseMaterials(item);

            float condFactor = ConditionFactor(item);
            float qualFactor = QualityFactor(item);
            // Simulate skill 10
            float t = 10f / 20f;
            float skillFactor = settings.baseSkillReturn
                + (settings.maxSkillReturn - settings.baseSkillReturn)
                * Mathf.Pow(t, settings.skillExponent);
            float taintPenalty = TaintPenalty(item);
            float globalMult = settings.globalEfficiencyMultiplier;

            var result = new List<ThingDefCountClass>();
            foreach (var mat in baseMats)
            {
                float rarePenalty = RareMaterialPenalty(mat.thingDef);
                float amount = mat.count * condFactor * qualFactor * skillFactor
                             * taintPenalty * rarePenalty * globalMult;
                amount = Mathf.Min(amount, mat.count);
                int final = Mathf.Max(Mathf.RoundToInt(amount), 0);
                if (final > 0)
                {
                    result.Add(new ThingDefCountClass(mat.thingDef, final));
                }
            }

            if (settings.minimumReturnGuarantee && result.Count == 0
                && baseMats.Count > 0 && item.HitPoints > 0)
            {
                result.Add(new ThingDefCountClass(baseMats[0].thingDef, 1));
            }

            return result;
        }

        /// <summary>
        /// Nonlinear condition factor: (HP / maxHP) ^ exponent.
        /// </summary>
        public static float ConditionFactor(Thing item)
        {
            float ratio = (float)item.HitPoints / item.MaxHitPoints;
            return Mathf.Pow(ratio, RRRRMod.Settings.conditionExponent);
        }

        /// <summary>
        /// Quality factor from settings lookup.
        /// </summary>
        public static float QualityFactor(Thing item)
        {
            if (item.TryGetQuality(out QualityCategory qc))
            {
                return RRRRMod.Settings.GetQualityFactor(qc);
            }
            return RRRRMod.Settings.GetQualityFactor(QualityCategory.Normal);
        }

        /// <summary>
        /// Taint penalty: halves return for tainted items.
        /// </summary>
        public static float TaintPenalty(Thing item)
        {
            if (item is Apparel apparel && apparel.WornByCorpse)
            {
                return RRRRMod.Settings.taintedReturnFraction;
            }
            return 1f;
        }

        /// <summary>
        /// Penalty for rare materials (components, adv components, chemfuel).
        /// </summary>
        public static float RareMaterialPenalty(ThingDef matDef)
        {
            var settings = RRRRMod.Settings;

            if (matDef == ThingDefOf.ComponentIndustrial)
                return settings.componentPenalty;
            if (matDef == ThingDefOf.ComponentSpacer)
                return settings.advComponentPenalty;
            if (matDef == ThingDefOf.Chemfuel)
                return settings.chemfuelPenalty;

            return 1f;
        }

        /// <summary>
        /// Calculate work ticks for recycling an item.
        /// </summary>
        public static float RecycleWorkAmount(Thing item)
        {
            float work = item.MarketValue * RRRRMod.Settings.recycleWorkFactor;
            return Mathf.Max(work, 100f);
        }

        /// <summary>
        /// Probabilistic rounding: floor + chance of +1 based on fractional part.
        /// </summary>
        public static int ProbabilisticRound(float amount)
        {
            if (amount <= 0f) return 0;
            int floor = Mathf.FloorToInt(amount);
            float frac = amount - floor;
            if (Rand.Value < frac)
                floor++;
            return floor;
        }
    }
}
