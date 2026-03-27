using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RRRR
{
    /// <summary>
    /// All material cost and return calculations for recycling, repair, and cleaning.
    /// Uses the formulas from DESIGN.md.
    /// </summary>
    public static class MaterialUtility
    {
        // Quality multipliers indexed by QualityCategory ordinal (0=Awful..6=Legendary)
        private static readonly float[] QualityMultipliers = { 0.60f, 0.70f, 0.80f, 0.90f, 1.00f, 1.10f, 1.20f };

        // Rare material return penalties for recycling
        private static readonly Dictionary<string, float> RareMaterialPenalties = new Dictionary<string, float>
        {
            { "ComponentIndustrial", 0.25f },
            { "ComponentSpacer", 0.15f },
            { "Chemfuel", 0.30f }
        };

        // ================================================================
        // RECYCLE
        // ================================================================

        public static float GetRecycleWorkAmount(Thing thing)
        {
            float workToMake = thing.def.GetStatValueAbstract(StatDefOf.WorkToMake, thing.Stuff);
            if (workToMake <= 0f)
                workToMake = 1000f;

            float work = workToMake * 0.15f;
            return Mathf.Clamp(work, 400f, 2000f);
        }

        public static List<Thing> DoRecycleProducts(Thing thing, Pawn worker, IntVec3 spawnPos, Map map)
        {
            var results = new List<Thing>();
            int skillLevel = worker?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
            float returnPct = CalculateReturnPercent(thing, skillLevel);

            var settings = RRRR_Mod.Settings;

            var costList = thing.def.CostListAdjusted(thing.Stuff, errorOnNullStuff: false);
            if (costList != null)
            {
                for (int i = 0; i < costList.Count; i++)
                {
                    var entry = costList[i];
                    if (entry.thingDef == null || entry.count <= 0)
                        continue;

                    if (settings.skipIntricateComponents && entry.thingDef.intricate)
                        continue;

                    float materialPct = returnPct;

                    if (RareMaterialPenalties.TryGetValue(entry.thingDef.defName, out float penalty))
                        materialPct *= penalty;

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

        // ================================================================
        // REPAIR MATERIAL COSTS
        // ================================================================

        /// <summary>
        /// Calculate what materials one repair cycle costs for the given item.
        /// Each cycle repairs 10% maxHP, so it costs ~10% of the item's base
        /// materials, scaled by tech difficulty. Components are included at
        /// reduced rates only when the original cost was high enough.
        /// Returns the list of (thingDef, count) pairs needed.
        /// </summary>
        public static List<ThingDefCountClass> GetRepairCycleCost(Thing item)
        {
            var costs = new List<ThingDefCountClass>();
            float techDifficulty = SkillUtility.GetTechDifficulty(item.def);

            var costList = item.def.CostListAdjusted(item.Stuff, errorOnNullStuff: false);
            if (costList == null)
                return costs;

            for (int i = 0; i < costList.Count; i++)
            {
                var entry = costList[i];
                if (entry.thingDef == null || entry.count <= 0)
                    continue;

                // For intricate/rare materials (components), only charge if the
                // original cost is high enough to matter. At 10% per cycle,
                // an item with 2 components would cost 0.24 per cycle — round
                // probabilistically so it sometimes costs 1, sometimes 0.
                float cycleFraction = 0.10f * 1.2f * techDifficulty;
                float rawCost = entry.count * cycleFraction;

                // For intricate materials, apply a reduced rate
                if (entry.thingDef.intricate)
                    rawCost *= 0.5f;

                int count = GenMath.RoundRandom(rawCost);
                if (count > 0)
                    costs.Add(new ThingDefCountClass(entry.thingDef, count));
            }

            return costs;
        }

        /// <summary>
        /// Check if the required materials for a repair cycle are available
        /// on the map near the given position.
        /// </summary>
        public static bool HasRepairMaterials(List<ThingDefCountClass> costs, Map map, IntVec3 pos)
        {
            for (int i = 0; i < costs.Count; i++)
            {
                int available = CountAvailableNear(costs[i].thingDef, map, pos);
                if (available < costs[i].count)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Consume the required materials from the map near the position.
        /// Call only after verifying with HasRepairMaterials.
        /// </summary>
        public static void ConsumeRepairMaterials(List<ThingDefCountClass> costs, Map map, IntVec3 pos)
        {
            for (int i = 0; i < costs.Count; i++)
            {
                ConsumeMaterialNear(costs[i].thingDef, costs[i].count, map, pos);
            }
        }

        // ================================================================
        // CLEAN MATERIAL COSTS
        // ================================================================

        /// <summary>
        /// Calculate the material cost for cleaning taint from an item.
        /// Costs ~20% of the item's primary (stuff) material.
        /// This ensures clean→recycle returns less than just recycling tainted.
        /// </summary>
        public static List<ThingDefCountClass> GetCleanCost(Thing item)
        {
            var costs = new List<ThingDefCountClass>();

            var costList = item.def.CostListAdjusted(item.Stuff, errorOnNullStuff: false);
            if (costList == null)
                return costs;

            for (int i = 0; i < costList.Count; i++)
            {
                var entry = costList[i];
                if (entry.thingDef == null || entry.count <= 0)
                    continue;

                // Skip intricate materials — cleaning doesn't use components
                if (entry.thingDef.intricate)
                    continue;

                int count = GenMath.RoundRandom(entry.count * 0.20f);
                if (count > 0)
                    costs.Add(new ThingDefCountClass(entry.thingDef, count));
            }

            // Guarantee minimum cost of 1 of the first non-intricate material
            if (costs.Count == 0 && costList.Count > 0)
            {
                for (int i = 0; i < costList.Count; i++)
                {
                    if (costList[i].thingDef != null && !costList[i].thingDef.intricate)
                    {
                        costs.Add(new ThingDefCountClass(costList[i].thingDef, 1));
                        break;
                    }
                }
            }

            return costs;
        }

        // ================================================================
        // SHARED MATERIAL HELPERS
        // ================================================================

        /// <summary>
        /// Count how many of a material are available on the map (not forbidden,
        /// reachable from the given position).
        /// </summary>
        private static int CountAvailableNear(ThingDef matDef, Map map, IntVec3 pos)
        {
            int total = 0;
            var things = map.listerThings.ThingsOfDef(matDef);
            if (things == null)
                return 0;

            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                if (t.IsForbidden(Faction.OfPlayer))
                    continue;
                total += t.stackCount;
            }
            return total;
        }

        /// <summary>
        /// Consume a specific amount of a material from the map, preferring
        /// stacks closest to the given position.
        /// </summary>
        private static void ConsumeMaterialNear(ThingDef matDef, int amount, Map map, IntVec3 pos)
        {
            int remaining = amount;
            var things = map.listerThings.ThingsOfDef(matDef);
            if (things == null)
                return;

            // Sort by distance to position for locality
            var sorted = new List<Thing>(things);
            sorted.Sort((a, b) => a.Position.DistanceToSquared(pos).CompareTo(b.Position.DistanceToSquared(pos)));

            for (int i = 0; i < sorted.Count && remaining > 0; i++)
            {
                var t = sorted[i];
                if (t.IsForbidden(Faction.OfPlayer))
                    continue;

                int take = Mathf.Min(remaining, t.stackCount);
                t.SplitOff(take).Destroy();
                remaining -= take;
            }
        }

        // ================================================================
        // RETURN PERCENT CALCULATION
        // ================================================================

        public static float CalculateReturnPercent(Thing thing, int skillLevel)
        {
            float condition = ConditionFactor(thing);
            float quality = QualityFactor(thing);
            float skill = SkillFactor(skillLevel);
            float taint = TaintFactor(thing);

            return condition * quality * skill * taint;
        }

        private static float ConditionFactor(Thing thing)
        {
            if (!thing.def.useHitPoints || thing.MaxHitPoints <= 0)
                return 1f;

            float ratio = (float)thing.HitPoints / thing.MaxHitPoints;
            return Mathf.Pow(ratio, 1.8f);
        }

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

        private static float SkillFactor(int skillLevel)
        {
            float normalized = Mathf.Clamp01(skillLevel / 20f);
            return 0.10f + 0.90f * Mathf.Pow(normalized, 0.6f);
        }

        private static float TaintFactor(Thing thing)
        {
            if (thing is Apparel apparel && apparel.WornByCorpse)
                return 0.50f;
            return 1f;
        }
    }
}
