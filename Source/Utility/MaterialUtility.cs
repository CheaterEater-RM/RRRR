using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// All material cost and return calculations for recycling, repair, and cleaning.
    /// Also provides ingredient-finding helpers for the WorkGivers.
    /// </summary>
    public static class MaterialUtility
    {
        private static readonly float[] QualityMultipliers = { 0.60f, 0.70f, 0.80f, 0.90f, 1.00f, 1.10f, 1.20f };

        private static readonly Dictionary<string, float> RareMaterialPenalties = new Dictionary<string, float>
        {
            { "ComponentIndustrial", 0.25f },
            { "ComponentSpacer", 0.15f },
            { "Chemfuel", 0.30f }
        };

        // ================================================================
        // SAFE THING SPAWNING
        // ================================================================

        /// <summary>
        /// Safely create a Thing from a ThingDef, guarding against MadeFromStuff
        /// defs that require a stuff parameter. Returns null if the def can't be
        /// safely instantiated as a raw material.
        /// </summary>
        private static Thing TryMakeProduct(ThingDef def, int count)
        {
            if (def == null || count <= 0)
                return null;
            if (def.MadeFromStuff)
                return null;

            Thing product = ThingMaker.MakeThing(def);
            product.stackCount = count;
            return product;
        }

        /// <summary>
        /// Attempt to place a product thing on the map. Destroys the thing if
        /// placement fails to prevent orphaned objects.
        /// Returns true if placed successfully.
        /// </summary>
        private static bool TryPlaceOrDestroy(Thing product, IntVec3 pos, Map map)
        {
            if (product == null)
                return false;
            if (GenPlace.TryPlaceThing(product, pos, map, ThingPlaceMode.Near))
                return true;
            product.Destroy();
            return false;
        }

        // ================================================================
        // RECYCLE
        // ================================================================

        public static float GetRecycleWorkAmount(Thing thing)
        {
            float workToMake = thing.def.GetStatValueAbstract(StatDefOf.WorkToMake, thing.Stuff);
            if (workToMake <= 0f)
                workToMake = 1000f;
            return Mathf.Clamp(workToMake * 0.15f, 400f, 2000f);
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
                    Thing product = TryMakeProduct(entry.thingDef, count);
                    if (product != null)
                    {
                        if (TryPlaceOrDestroy(product, spawnPos, map))
                            results.Add(product);
                    }
                }
            }

            if (thing.def.smeltProducts != null)
            {
                for (int i = 0; i < thing.def.smeltProducts.Count; i++)
                {
                    var entry = thing.def.smeltProducts[i];
                    Thing product = TryMakeProduct(entry.thingDef, entry.count);
                    if (product != null)
                    {
                        if (TryPlaceOrDestroy(product, spawnPos, map))
                            results.Add(product);
                    }
                }
            }

            if (results.Count == 0 && costList != null && costList.Count > 0)
            {
                for (int i = 0; i < costList.Count; i++)
                {
                    var fallback = costList[i];
                    if (fallback.thingDef != null && !fallback.thingDef.intricate && !fallback.thingDef.MadeFromStuff)
                    {
                        Thing product = ThingMaker.MakeThing(fallback.thingDef);
                        product.stackCount = 1;
                        if (TryPlaceOrDestroy(product, spawnPos, map))
                            results.Add(product);
                        break;
                    }
                }
            }

            return results;
        }

        // ================================================================
        // PARTIAL MATERIAL RECLAIM (used on repair destruction)
        // ================================================================

        /// <summary>
        /// Spawn partial materials from a destroyed item, scaled by return percent.
        /// Used when repair failure destroys the item.
        /// </summary>
        public static void SpawnPartialReclaim(Thing item, Pawn worker, float reclaimFactor, IntVec3 spawnPos, Map map)
        {
            int skillLevel = worker?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
            float returnPct = CalculateReturnPercent(item, skillLevel) * reclaimFactor;

            var costList = item.def.CostListAdjusted(item.Stuff, errorOnNullStuff: false);
            if (costList == null)
                return;

            for (int i = 0; i < costList.Count; i++)
            {
                var entry = costList[i];
                if (entry.thingDef == null || entry.count <= 0 || entry.thingDef.intricate)
                    continue;

                int count = GenMath.RoundRandom(entry.count * returnPct);
                Thing product = TryMakeProduct(entry.thingDef, count);
                if (product != null)
                    TryPlaceOrDestroy(product, spawnPos, map);
            }
        }

        // ================================================================
        // REPAIR MATERIAL COSTS
        // ================================================================

        public static List<ThingDefCountClass> GetRepairCycleCost(Thing item)
        {
            var costs = new List<ThingDefCountClass>();
            float techDifficulty = SkillUtility.GetTechDifficulty(item.def);
            float cycleFraction = 0.10f * techDifficulty;
            const int cyclesNeeded = 5;

            var costList = item.def.CostListAdjusted(item.Stuff, errorOnNullStuff: false);
            if (costList == null)
                return costs;

            for (int i = 0; i < costList.Count; i++)
            {
                var entry = costList[i];
                if (entry.thingDef == null || entry.count <= 0)
                    continue;

                int count;
                if (entry.thingDef.intricate)
                {
                    if (entry.count <= 2)
                        continue;
                    count = Mathf.Max(1, Mathf.FloorToInt(entry.count * cycleFraction));
                }
                else
                {
                    int ceilCost = Mathf.CeilToInt(entry.count * cycleFraction);
                    if (ceilCost * cyclesNeeded >= entry.count)
                        continue;
                    count = ceilCost;
                }

                costs.Add(new ThingDefCountClass(entry.thingDef, count));
            }

            return costs;
        }

        // ================================================================
        // CLEAN MATERIAL COSTS
        // ================================================================

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
                if (entry.thingDef.intricate)
                    continue;

                int count = Mathf.CeilToInt(entry.count * 0.25f);
                if (count > 0)
                    costs.Add(new ThingDefCountClass(entry.thingDef, count));
            }

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
        // INGREDIENT FINDING (used by WorkGivers)
        // ================================================================

        public static bool TryFindIngredients(
            List<ThingDefCountClass> costs,
            Pawn pawn,
            out List<Thing> foundThings,
            out List<int> foundCounts)
        {
            foundThings = new List<Thing>();
            foundCounts = new List<int>();

            if (costs == null || costs.Count == 0)
                return true;

            Map map = pawn.Map;

            for (int i = 0; i < costs.Count; i++)
            {
                ThingDef needed = costs[i].thingDef;
                int remaining = costs[i].count;

                var available = map.listerThings.ThingsOfDef(needed);
                if (available == null)
                    return false;

                var sorted = new List<Thing>(available);
                sorted.Sort((a, b) =>
                    a.Position.DistanceToSquared(pawn.Position)
                    .CompareTo(b.Position.DistanceToSquared(pawn.Position)));

                for (int j = 0; j < sorted.Count && remaining > 0; j++)
                {
                    Thing t = sorted[j];
                    if (t.IsForbidden(pawn))
                        continue;
                    if (!pawn.CanReserve(t))
                        continue;
                    if (!pawn.CanReach(t, PathEndMode.ClosestTouch, pawn.NormalMaxDanger()))
                        continue;

                    int take = Mathf.Min(remaining, t.stackCount);
                    foundThings.Add(t);
                    foundCounts.Add(take);
                    remaining -= take;
                }

                if (remaining > 0)
                    return false;
            }

            return true;
        }

        public static void ConsumeIngredientsOnBench(Thing bench, Map map)
        {
            if (!(bench is IBillGiver billGiver))
                return;

            foreach (IntVec3 cell in billGiver.IngredientStackCells)
            {
                var things = map.thingGrid.ThingsListAt(cell);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing t = things[i];
                    if (t.def.category == ThingCategory.Item && t != bench)
                        t.Destroy();
                }
            }
        }

        // ================================================================
        // RETURN PERCENT CALCULATION
        // ================================================================

        public static float CalculateReturnPercent(Thing thing, int skillLevel)
        {
            return ConditionFactor(thing) * QualityFactor(thing) * SkillFactor(skillLevel) * TaintFactor(thing);
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
