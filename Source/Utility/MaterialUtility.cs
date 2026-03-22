using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RRRR
{
    /// <summary>
    /// All material cost and return calculations for recycling.
    /// Uses the formulas from DESIGN.md.
    /// </summary>
    public static class MaterialUtility
    {
        // Quality multipliers indexed by QualityCategory ordinal (0=Awful..6=Legendary)
        private static readonly float[] QualityMultipliers = { 0.60f, 0.70f, 0.80f, 0.90f, 1.00f, 1.10f, 1.20f };

        // Rare material return penalties
        private static readonly Dictionary<string, float> RareMaterialPenalties = new Dictionary<string, float>
        {
            { "ComponentIndustrial", 0.25f },
            { "ComponentSpacer", 0.15f },
            { "Chemfuel", 0.30f }
        };

        /// <summary>
        /// Calculate the total work ticks needed for recycling an item.
        /// Recycling is destructive work — faster than crafting.
        /// Vanilla smelting uses a flat 1600 work. We scale lightly with
        /// the item's crafting cost but cap it to keep recycling snappy.
        /// </summary>
        public static float GetRecycleWorkAmount(Thing thing)
        {
            float workToMake = thing.def.GetStatValueAbstract(StatDefOf.WorkToMake, thing.Stuff);
            if (workToMake <= 0f)
                workToMake = 1000f;

            // 15% of original crafting time, clamped between 400 and 2000
            // For reference: vanilla SmeltWeapon is a flat 1600
            float work = workToMake * 0.15f;
            return UnityEngine.Mathf.Clamp(work, 400f, 2000f);
        }

        /// <summary>
        /// Calculate and spawn the material returns from recycling.
        /// Returns the list of spawned things for logging/UI purposes.
        /// </summary>
        public static List<Thing> DoRecycleProducts(Thing thing, Pawn worker, IntVec3 spawnPos, Map map)
        {
            var results = new List<Thing>();
            int skillLevel = worker?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
            float returnPct = CalculateReturnPercent(thing, skillLevel);

            var settings = RRRR_Mod.Settings;

            // Base materials from CostListAdjusted
            var costList = thing.def.CostListAdjusted(thing.Stuff, errorOnNullStuff: false);
            if (costList != null)
            {
                for (int i = 0; i < costList.Count; i++)
                {
                    var entry = costList[i];
                    if (entry.thingDef == null || entry.count <= 0)
                        continue;

                    // Skip intricate components if setting is on
                    // ThingDef.intricate is the vanilla field used by SmeltProducts
                    if (settings.skipIntricateComponents && entry.thingDef.intricate)
                        continue;

                    float materialPct = returnPct;

                    // Apply rare material penalty
                    if (RareMaterialPenalties.TryGetValue(entry.thingDef.defName, out float penalty))
                    {
                        materialPct *= penalty;
                    }

                    // Apply global multiplier from settings
                    materialPct *= settings.recycleGlobalMult;

                    int count = GenMath.RoundRandom(entry.count * materialPct);
                    if (count > 0)
                    {
                        Thing product = ThingMaker.MakeThing(entry.thingDef);
                        product.stackCount = count;
                        if (GenPlace.TryPlaceThing(product, spawnPos, map, ThingPlaceMode.Near))
                            results.Add(product);
                        else
                            product.Destroy();
                    }
                }
            }

            // Additional smelt products (e.g. steel from guns)
            if (thing.def.smeltProducts != null)
            {
                for (int i = 0; i < thing.def.smeltProducts.Count; i++)
                {
                    var entry = thing.def.smeltProducts[i];
                    if (entry.thingDef == null || entry.count <= 0)
                        continue;

                    Thing product = ThingMaker.MakeThing(entry.thingDef);
                    product.stackCount = entry.count;
                    if (GenPlace.TryPlaceThing(product, spawnPos, map, ThingPlaceMode.Near))
                        results.Add(product);
                    else
                        product.Destroy();
                }
            }

            // Guarantee minimum 1 of something if we got nothing but the item had materials
            if (results.Count == 0 && costList != null && costList.Count > 0)
            {
                var fallback = costList[0];
                if (fallback.thingDef != null && !fallback.thingDef.intricate)
                {
                    Thing product = ThingMaker.MakeThing(fallback.thingDef);
                    product.stackCount = 1;
                    if (GenPlace.TryPlaceThing(product, spawnPos, map, ThingPlaceMode.Near))
                        results.Add(product);
                    else
                        product.Destroy();
                }
            }

            return results;
        }

        /// <summary>
        /// Calculate the combined return percentage from all multipliers.
        /// Formula: condition × quality × skill × taintPenalty
        /// </summary>
        public static float CalculateReturnPercent(Thing thing, int skillLevel)
        {
            float condition = ConditionFactor(thing);
            float quality = QualityFactor(thing);
            float skill = SkillFactor(skillLevel);
            float taint = TaintFactor(thing);

            return condition * quality * skill * taint;
        }

        /// <summary>
        /// (HP/maxHP) ^ 1.8 — nonlinear, punishes damage heavily
        /// </summary>
        private static float ConditionFactor(Thing thing)
        {
            if (!thing.def.useHitPoints || thing.MaxHitPoints <= 0)
                return 1f;

            float ratio = (float)thing.HitPoints / thing.MaxHitPoints;
            return UnityEngine.Mathf.Pow(ratio, 1.8f);
        }

        /// <summary>
        /// Quality multiplier: Awful 0.60 → Legendary 1.20
        /// </summary>
        private static float QualityFactor(Thing thing)
        {
            if (thing.TryGetQuality(out QualityCategory qc))
            {
                int idx = (int)qc;
                if (idx >= 0 && idx < QualityMultipliers.Length)
                    return QualityMultipliers[idx];
            }
            return 0.80f;
        }

        /// <summary>
        /// 0.10 + 0.90 × (skill/20)^0.6
        /// skill 0 = 10%, skill 10 ≈ 74%, skill 20 = 100%
        /// </summary>
        private static float SkillFactor(int skillLevel)
        {
            float normalized = UnityEngine.Mathf.Clamp01(skillLevel / 20f);
            return 0.10f + 0.90f * UnityEngine.Mathf.Pow(normalized, 0.6f);
        }

        /// <summary>
        /// 0.50 for tainted items, 1.0 otherwise
        /// </summary>
        private static float TaintFactor(Thing thing)
        {
            if (thing is Apparel apparel && apparel.WornByCorpse)
                return 0.50f;
            return 1f;
        }
    }
}
