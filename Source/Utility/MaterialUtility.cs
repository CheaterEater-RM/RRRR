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
        // Quality scores [0,1] fed into the sigmoid weighted sum.
        // Awful(0)=0.0 … Legendary(6)=1.0; Normal sits at 0.35 so that
        // skill-10 + Normal + full HP lands on the sigmoid midpoint (~50% return).
        private static readonly float[] QualityScores = { 0.00f, 0.15f, 0.35f, 0.55f, 0.70f, 0.85f, 1.00f };

        // ── Sigmoid recycle parameters ────────────────────────────────────────
        // Weighted score: x = WeightSkill*s + WeightHP*h + WeightQuality*q  (each factor ∈ [0,1])
        // sigmoid output renormalised so x=0 → MinReturn, x=1 → 1.0
        private const float WeightSkill   = 0.35f;
        private const float WeightHP      = 0.40f;
        private const float WeightQuality = 0.25f;
        private const float SigmoidK      = 7f;    // steepness
        private const float SigmoidX0     = 0.70f; // midpoint — maps to ~50% return
        private const float MinReturn     = 0.05f; // floor
        private const float TaintMult     = 0.60f; // flat post-sigmoid taint penalty

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

        /// <summary>
        /// Final per-material return fraction, incorporating base return, rare-material
        /// penalties, and the global multiplier.  Used by both DoRecycleProducts and
        /// the tooltip builder so that what the UI shows matches what spawns.
        /// </summary>
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
        /// Formula per material:
        ///   divisor      = Settings.RepairCostDivisor  (= RepairCyclesFull * 2)
        ///   costPerCycle = Ceiling(baseCost / divisor)
        ///   Include only if costPerCycle * RepairCyclesFull &lt; baseCost
        ///     (total repair cost strictly less than making the item new)
        ///
        /// At default settings (20% HP/cycle → 5 cycles, divisor = 10):
        ///   6 steel:       Ceiling(6/10)=1,  1*5=5  &lt; 6  → 1/cycle
        ///   5 steel:       Ceiling(5/10)=1,  1*5=5  &lt; 5? No → fallback
        ///   60 steel:      Ceiling(60/10)=6, 6*5=30 &lt; 60 → 6/cycle
        ///   3 components:  Ceiling(3/10)=1,  1*5=5  &lt; 3? No → fallback candidate
        ///   60 steel + 3 components: steel passes, components excluded — no fallback needed
        ///
        /// Fallback: if nothing passes, use 1 unit of the highest-count material.
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

            // Fallback: nothing qualified — use 1 of the highest-count material
            if (costs.Count == 0)
                AddFallback(costList, costs);

            return costs;
        }

        // ================================================================
        // CLEAN MATERIAL COSTS
        // ================================================================

        /// <summary>
        /// Cost of a single cleaning operation.
        /// Formula: Ceiling(baseCost / CleanCostDivisor) per material.
        /// At default 20% fraction → divisor 5 → 20% of make cost.
        /// No repair-guard: cleaning is a single operation, not a cycle series.
        /// </summary>
        public static List<ThingDefCountClass> GetCleanCost(Thing item)
        {
            var costs    = new List<ThingDefCountClass>();
            var costList = item.def.CostListAdjusted(item.Stuff, errorOnNullStuff: false);
            if (costList == null) return costs;

            int divisor = RRRR_Mod.Settings.CleanCostDivisor;

            for (int i = 0; i < costList.Count; i++)
            {
                var entry = costList[i];
                if (entry.thingDef == null || entry.count <= 0) continue;

                int costPerOp = Mathf.CeilToInt((float)entry.count / divisor);
                costs.Add(new ThingDefCountClass(entry.thingDef, costPerOp));
            }

            // Fallback: nothing qualified — use 1 of the highest-count material
            if (costs.Count == 0)
                AddFallback(costList, costs);

            return costs;
        }

        // ================================================================
        // SHARED COST HELPERS
        // ================================================================

        /// <summary>
        /// Appends 1 unit of the highest-count material in costList to costs.
        /// Used as a fallback when no material passes the cost threshold.
        /// </summary>
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

        /// <summary>
        /// Find and reserve ingredients for repair/clean within a search radius
        /// from the given origin (typically the bench position).
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

            Map   map      = pawn.Map;
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

        /// <summary>
        /// Overload with no radius restriction — uses pawn position as origin.
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

            // Build a mutable remaining-count table; sum duplicate entries, skip null/zero.
            var remaining = new Dictionary<ThingDef, int>(expectedCosts.Count);
            for (int i = 0; i < expectedCosts.Count; i++)
            {
                var entry = expectedCosts[i];
                if (entry.thingDef == null || entry.count <= 0) continue;
                if (remaining.TryGetValue(entry.thingDef, out int existing))
                    remaining[entry.thingDef] = existing + entry.count;
                else
                    remaining[entry.thingDef] = entry.count;
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

        /// <summary>
        /// Returns the fraction of make-cost materials to yield when recycling.
        ///
        /// Algorithm:
        ///   1. Normalise skill, HP, and quality each to [0,1].
        ///   2. Combine into a single score:  x = ws·s + wh·h + wq·q
        ///   3. Pass through a logistic sigmoid:  σ(x) = 1 / (1 + e^{−k(x−x₀)})
        ///   4. Renormalise so σ(0)→MinReturn and σ(1)→1.0, giving strict endpoints.
        ///   5. Apply taint as a flat post-sigmoid multiplier, floored at MinReturn.
        ///
        /// Key reference points (defaults):
        ///   Skill 20, Legendary, HP 100 % → 100 %
        ///   Skill 10, Normal,    HP 100 % → ~51 %
        ///   Skill  0, Awful,     HP   0 % → ~5 %
        ///   Taint (×0.60) at full-HP Normal ≈ clean cost, so cleaning before
        ///   recycling is roughly break-even; for damaged items it is never worth it.
        /// </summary>
        public static float CalculateReturnPercent(Thing thing, int skillLevel)
        {
            // 1 — normalised inputs
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

            // 2 — weighted score
            float x = WeightSkill * s + WeightHP * h + WeightQuality * q;

            // 3 — logistic sigmoid
            float sigmoid = 1f / (1f + Mathf.Exp(-SigmoidK * (x  - SigmoidX0)));
            float sigMin  = 1f / (1f + Mathf.Exp(-SigmoidK * (0f - SigmoidX0)));
            float sigMax  = 1f / (1f + Mathf.Exp(-SigmoidK * (1f - SigmoidX0)));

            // 4 — renormalise to [MinReturn, 1.0]
            float renorm = (sigmoid - sigMin) / (sigMax - sigMin);
            float result = MinReturn + (1f - MinReturn) * renorm;

            // 5 — taint: flat multiplier, never below MinReturn
            if (thing is Apparel apparel && apparel.WornByCorpse)
                result = Mathf.Max(MinReturn, result * TaintMult);

            return result;
        }
    }
}
