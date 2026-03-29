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
    ///
    /// Cost formulas use RRRR_Mod.Settings-derived divisors rather than
    /// hardcoded numbers, so player configuration flows through correctly.
    /// See RRRR_Settings for the derived property definitions.
    /// </summary>
    public static class MaterialUtility
    {
        private static readonly float[] QualityScores = { 0.00f, 0.15f, 0.35f, 0.55f, 0.70f, 0.85f, 1.00f };

        private const float WeightSkill   = 0.35f;
        private const float WeightHP      = 0.40f;
        private const float WeightQuality = 0.25f;
        private const float SigmoidK      = 7f;
        private const float SigmoidX0     = 0.70f;
        private const float MinReturn     = 0.05f;
        private const float TaintMult     = 0.60f;

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

        public static float GetMaterialReturnPct(ThingDef materialDef, float baseReturnPct)
        {
            float pct = baseReturnPct;
            if (RareMaterialPenalties.TryGetValue(materialDef.defName, out float penalty))
                pct *= penalty;
            pct *= RRRR_Mod.Settings.recycleGlobalMult;
            return pct;
        }

        public static List<Thing> DoRecycleProducts(Thing thing, Pawn worker, IntVec3 spawnPos, Map map)
        {
            var results     = new List<Thing>();
            int skillLevel  = worker?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
            float returnPct = CalculateReturnPercent(thing, skillLevel);
            var settings    = RRRR_Mod.Settings;

            var costList = thing.def.CostListAdjusted(thing.Stuff, errorOnNullStuff: false);
            if (costList != null)
            {
                for (int i = 0; i < costList.Count; i++)
                {
                    var entry = costList[i];
                    if (entry.thingDef == null || entry.count <= 0) continue;
                    if (settings.skipIntricateComponents && entry.thingDef.intricate) continue;

                    float materialPct = GetMaterialReturnPct(entry.thingDef, returnPct);
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

        /// <summary>
        /// Cost of one repair cycle per material.
        ///
        /// Formula: costPerCycle = Ceiling(baseCost / RepairCostDivisor)
        /// Include only if costPerCycle * RepairCyclesFull &lt; baseCost
        /// (total repair cost strictly less than making the item new).
        ///
        /// Fallback: if nothing qualifies, use 1 unit of the highest-count material.
        ///
        /// At default settings (20% HP/cycle → 5 cycles, divisor = 10):
        ///   60 steel:      6/cycle, 30 total  (~50% of make cost) ✓
        ///   6 components:  1/cycle, 5 total   (&lt; 6 to make)      ✓
        ///   3 components:  excluded → fallback: 1 component
        /// </summary>
        public static List<ThingDefCountClass> GetRepairCycleCost(Thing item)
        {
            var costs    = new List<ThingDefCountClass>();
            var costList = item.def.CostListAdjusted(item.Stuff, errorOnNullStuff: false);
            if (costList == null) return costs;

            var settings   = RRRR_Mod.Settings;
            int divisor    = settings.RepairCostDivisor;
            int cyclesFull = settings.RepairCyclesFull;

            for (int i = 0; i < costList.Count; i++)
            {
                var entry = costList[i];
                if (entry.thingDef == null || entry.count <= 0) continue;

                int costPerCycle = Mathf.CeilToInt((float)entry.count / divisor);
                if (costPerCycle * cyclesFull < entry.count)
                    costs.Add(new ThingDefCountClass(entry.thingDef, costPerCycle));
            }

            if (costs.Count == 0)
                AddFallback(costList, costs);

            return costs;
        }

        // ================================================================
        // CLEAN MATERIAL COSTS
        // ================================================================

        /// <summary>
        /// Cost of a single cleaning operation.
        /// Same structure as repair: Ceiling(baseCost / CleanCostDivisor),
        /// include only if total &lt; baseCost. Fallback: 1 of highest-count material.
        /// </summary>
        public static List<ThingDefCountClass> GetCleanCost(Thing item)
        {
            var costs    = new List<ThingDefCountClass>();
            var costList = item.def.CostListAdjusted(item.Stuff, errorOnNullStuff: false);
            if (costList == null) return costs;

            var settings   = RRRR_Mod.Settings;
            int divisor    = settings.CleanCostDivisor;
            int cyclesFull = settings.RepairCyclesFull;

            for (int i = 0; i < costList.Count; i++)
            {
                var entry = costList[i];
                if (entry.thingDef == null || entry.count <= 0) continue;

                int costPerOp = Mathf.CeilToInt((float)entry.count / divisor);
                if (costPerOp * cyclesFull < entry.count)
                    costs.Add(new ThingDefCountClass(entry.thingDef, costPerOp));
            }

            if (costs.Count == 0)
                AddFallback(costList, costs);

            return costs;
        }

        // ================================================================
        // SHARED COST HELPERS
        // ================================================================

        private static void AddFallback(List<ThingDefCountClass> costList, List<ThingDefCountClass> costs)
        {
            ThingDefCountClass best = null;
            for (int i = 0; i < costList.Count; i++)
            {
                var entry = costList[i];
                if (entry.thingDef == null || entry.count <= 0) continue;
                if (best == null || entry.count > best.count)
                    best = entry;
            }
            if (best != null)
                costs.Add(new ThingDefCountClass(best.thingDef, 1));
        }

        // ================================================================
        // INGREDIENT FINDING
        // ================================================================

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

            Map   map       = pawn.Map;
            bool  useRadius = searchRadius > 0f && searchRadius < 9999f;
            float radiusSq  = searchRadius * searchRadius;

            for (int i = 0; i < costs.Count; i++)
            {
                ThingDef needed    = costs[i].thingDef;
                int      remaining = costs[i].count;

                var available = map.listerThings.ThingsOfDef(needed);
                if (available == null || available.Count == 0)
                    return false;

                var sorted = new List<Thing>(available.Count);
                for (int j = 0; j < available.Count; j++)
                {
                    Thing t = available[j];
                    if (useRadius && (t.Position - searchOrigin).LengthHorizontalSquared > radiusSq)
                        continue;
                    sorted.Add(t);
                }

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

        /// <summary>
        /// Consumes ingredients from the bench's ingredient stack cells.
        /// Only destroys items whose ThingDef is in expectedCosts, up to the
        /// expected count per def. Anything else on those cells is left alone.
        /// </summary>
        public static void ConsumeIngredientsOnBench(
            Thing bench,
            Map map,
            List<ThingDefCountClass> expectedCosts)
        {
            if (!(bench is IBillGiver billGiver)) return;
            if (expectedCosts == null || expectedCosts.Count == 0) return;

            // Build a mutable remaining-count table
            var remaining = new Dictionary<ThingDef, int>(expectedCosts.Count);
            for (int i = 0; i < expectedCosts.Count; i++)
            {
                var entry = expectedCosts[i];
                if (entry.thingDef != null && entry.count > 0)
                {
                    if (remaining.TryGetValue(entry.thingDef, out int existing))
                        remaining[entry.thingDef] = existing + entry.count;
                    else
                        remaining[entry.thingDef] = entry.count;
                }
            }

            foreach (IntVec3 cell in billGiver.IngredientStackCells)
            {
                if (remaining.Count == 0) break;

                var things = map.thingGrid.ThingsListAt(cell);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing t = things[i];
                    if (t == bench) continue;
                    if (t.def.category != ThingCategory.Item) continue;
                    if (!remaining.TryGetValue(t.def, out int need)) continue;

                    int toConsume = Mathf.Min(need, t.stackCount);
                    need -= toConsume;

                    if (need <= 0)
                        remaining.Remove(t.def);
                    else
                        remaining[t.def] = need;

                    if (toConsume >= t.stackCount)
                        t.Destroy();
                    else
                        t.stackCount -= toConsume;
                }
            }
        }

        // ================================================================
        // RETURN PERCENT CALCULATION
        // ================================================================

        public static float CalculateReturnPercent(Thing thing, int skillLevel)
        {
            float s = Mathf.Clamp01(skillLevel / 20f);

            float h = 1f;
            if (thing.def.useHitPoints && thing.MaxHitPoints > 0)
                h = Mathf.Clamp01((float)thing.HitPoints / thing.MaxHitPoints);

            float q = QualityScores[2]; // default: Normal
            if (thing.TryGetQuality(out QualityCategory qc))
            {
                int qi = (int)qc;
                if (qi >= 0 && qi < QualityScores.Length)
                    q = QualityScores[qi];
            }

            float x       = WeightSkill * s + WeightHP * h + WeightQuality * q;
            float sigmoid = 1f / (1f + Mathf.Exp(-SigmoidK * (x     - SigmoidX0)));
            float sigMin  = 1f / (1f + Mathf.Exp(-SigmoidK * (0f    - SigmoidX0)));
            float sigMax  = 1f / (1f + Mathf.Exp(-SigmoidK * (1f    - SigmoidX0)));
            float renorm  = (sigmoid - sigMin) / (sigMax - sigMin);
            float result  = MinReturn + (1f - MinReturn) * renorm;

            if (thing is Apparel apparel && apparel.WornByCorpse)
                result = Mathf.Max(MinReturn, result * TaintMult);

            return result;
        }
    }
}
