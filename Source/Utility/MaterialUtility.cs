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
            { "ComponentSpacer",     0.15f },
            { "Chemfuel",            0.30f }
        };

        // ================================================================
        // SAFE THING SPAWNING
        // ================================================================

        private static Thing TryMakeProduct(ThingDef def, int count)
        {
            if (def == null || count <= 0) return null;
            if (def.MadeFromStuff) return null;
            Thing product = ThingMaker.MakeThing(def);
            product.stackCount = count;
            return product;
        }

        private static bool TryPlaceOrDestroy(Thing product, IntVec3 pos, Map map)
        {
            if (product == null) return false;
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
            if (workToMake <= 0f) workToMake = 1000f;
            return Mathf.Clamp(workToMake * 0.15f, 400f, 2000f);
        }

        public static List<Thing> DoRecycleProducts(Thing thing, Pawn worker, IntVec3 spawnPos, Map map)
        {
            var results    = new List<Thing>();
            int skillLevel = worker?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
            float returnPct = CalculateReturnPercent(thing, skillLevel);
            var settings   = RRRR_Mod.Settings;

            var costList = thing.def.CostListAdjusted(thing.Stuff, errorOnNullStuff: false);
            if (costList != null)
            {
                for (int i = 0; i < costList.Count; i++)
                {
                    var entry = costList[i];
                    if (entry.thingDef == null || entry.count <= 0) continue;
                    if (settings.skipIntricateComponents && entry.thingDef.intricate) continue;

                    float materialPct = returnPct;
                    if (RareMaterialPenalties.TryGetValue(entry.thingDef.defName, out float penalty))
                        materialPct *= penalty;
                    materialPct *= settings.recycleGlobalMult;

                    int count = GenMath.RoundRandom(entry.count * materialPct);
                    Thing product = TryMakeProduct(entry.thingDef, count);
                    if (product != null && TryPlaceOrDestroy(product, spawnPos, map))
                        results.Add(product);
                }
            }

            if (thing.def.smeltProducts != null)
            {
                for (int i = 0; i < thing.def.smeltProducts.Count; i++)
                {
                    var entry = thing.def.smeltProducts[i];
                    Thing product = TryMakeProduct(entry.thingDef, entry.count);
                    if (product != null && TryPlaceOrDestroy(product, spawnPos, map))
                        results.Add(product);
                }
            }

            // Guarantee at least 1 non-intricate material on zero-yield results
            if (results.Count == 0 && costList != null)
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
        // PARTIAL MATERIAL RECLAIM
        // ================================================================

        public static void SpawnPartialReclaim(Thing item, Pawn worker, float reclaimFactor, IntVec3 spawnPos, Map map)
        {
            int skillLevel  = worker?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
            float returnPct = CalculateReturnPercent(item, skillLevel) * reclaimFactor;

            var costList = item.def.CostListAdjusted(item.Stuff, errorOnNullStuff: false);
            if (costList == null) return;

            for (int i = 0; i < costList.Count; i++)
            {
                var entry = costList[i];
                if (entry.thingDef == null || entry.count <= 0 || entry.thingDef.intricate) continue;

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
            float cycleFraction  = 0.10f * techDifficulty;
            const int cyclesNeeded = 5;

            var costList = item.def.CostListAdjusted(item.Stuff, errorOnNullStuff: false);
            if (costList == null) return costs;

            for (int i = 0; i < costList.Count; i++)
            {
                var entry = costList[i];
                if (entry.thingDef == null || entry.count <= 0) continue;

                int count;
                if (entry.thingDef.intricate)
                {
                    if (entry.count <= 2) continue;
                    count = Mathf.Max(1, Mathf.FloorToInt(entry.count * cycleFraction));
                }
                else
                {
                    int ceilCost = Mathf.CeilToInt(entry.count * cycleFraction);
                    if (ceilCost * cyclesNeeded >= entry.count) continue;
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
            var costs    = new List<ThingDefCountClass>();
            var costList = item.def.CostListAdjusted(item.Stuff, errorOnNullStuff: false);
            if (costList == null) return costs;

            for (int i = 0; i < costList.Count; i++)
            {
                var entry = costList[i];
                if (entry.thingDef == null || entry.count <= 0 || entry.thingDef.intricate) continue;

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
        // INGREDIENT FINDING
        // ================================================================

        /// <summary>
        /// Find and reserve ingredients for repair/clean within a search radius
        /// from the given origin (typically the bench position).
        ///
        /// Respects ingredientSearchRadius to match vanilla bill behaviour and
        /// avoid full-map scans. Sorts candidates by distance to the origin so
        /// the closest materials to the bench are consumed first.
        /// </summary>
        public static bool TryFindIngredients(
            List<ThingDefCountClass> costs,
            Pawn pawn,
            IntVec3 searchOrigin,
            float searchRadius,
            out List<Thing> foundThings,
            out List<int>   foundCounts)
        {
            foundThings = new List<Thing>();
            foundCounts = new List<int>();

            if (costs == null || costs.Count == 0)
                return true;

            Map  map      = pawn.Map;
            bool useRadius = searchRadius > 0f && searchRadius < 9999f;
            float radiusSq = searchRadius * searchRadius;

            for (int i = 0; i < costs.Count; i++)
            {
                ThingDef needed    = costs[i].thingDef;
                int      remaining = costs[i].count;

                var available = map.listerThings.ThingsOfDef(needed);
                if (available == null || available.Count == 0)
                    return false;

                // Build radius-filtered, sorted candidate list for this material.
                // Sorting allocates a list but only runs when materials are actually
                // present on the map, and only for the small set matching this def.
                var sorted = new List<Thing>(available.Count);
                for (int j = 0; j < available.Count; j++)
                {
                    Thing t = available[j];
                    if (useRadius && (t.Position - searchOrigin).LengthHorizontalSquared > radiusSq)
                        continue;
                    sorted.Add(t);
                }

                // Sort by distance to origin (bench position), matching vanilla logic
                sorted.Sort((a, b) =>
                    (a.Position - searchOrigin).LengthHorizontalSquared
                    .CompareTo((b.Position - searchOrigin).LengthHorizontalSquared));

                for (int j = 0; j < sorted.Count && remaining > 0; j++)
                {
                    Thing t = sorted[j];
                    if (t.IsForbidden(pawn))  continue;
                    if (!pawn.CanReserve(t))   continue;
                    if (!pawn.CanReach(t, PathEndMode.ClosestTouch, pawn.NormalMaxDanger())) continue;

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

        /// <summary>
        /// Overload with no radius restriction — used by designation-based WorkGivers
        /// which call this only after a bench has already been confirmed reachable.
        /// The bench's proximity naturally limits how far materials are expected.
        /// Uses pawn position as origin for closest-first ordering.
        /// </summary>
        public static bool TryFindIngredients(
            List<ThingDefCountClass> costs,
            Pawn pawn,
            out List<Thing> foundThings,
            out List<int>   foundCounts)
        {
            return TryFindIngredients(costs, pawn, pawn.Position, 9999f,
                out foundThings, out foundCounts);
        }

        // ================================================================
        // BENCH INGREDIENT CONSUMPTION
        // ================================================================

        public static void ConsumeIngredientsOnBench(Thing bench, Map map)
        {
            if (!(bench is IBillGiver billGiver)) return;

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
            if (!thing.def.useHitPoints || thing.MaxHitPoints <= 0) return 1f;
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
