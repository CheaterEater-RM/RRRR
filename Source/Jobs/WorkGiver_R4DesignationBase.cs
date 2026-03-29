using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Shared base for designation-based WorkGivers (recycle, repair, clean).
    ///
    /// Each concrete subclass is registered under three WorkGiverDefs — one for
    /// Crafting, one for Smithing, one for Tailoring — so that the work tab
    /// column reflects which bench the item will actually go to.
    ///
    /// FindBench filters candidates to only benches whose WorkTypeDef matches
    /// this WorkGiver's def.workType, ensuring a Smithing pawn only picks up
    /// smithy items and a Tailoring pawn only picks up tailor bench items.
    ///
    /// Items that route to multiple bench types (e.g. tribal apparel craftable
    /// at both CraftingSpot and HandTailoringBench) will be accepted by both the
    /// Crafting and Tailoring WorkGivers; the pawn with the highest-priority
    /// enabled column wins.
    ///
    /// Performance notes:
    /// - ShouldSkip fast-exits if no designations of our type exist (standard).
    /// - HasJobOnThing calls ItemHasMatchingBench before the full FindBench so
    ///   that items routed to a different work type are rejected in O(n_benchDefs)
    ///   rather than paying the full GenClosest.ClosestThingReachable cost.
    /// </summary>
    public abstract class WorkGiver_R4DesignationBase : WorkGiver_Scanner
    {
        protected abstract DesignationDef DesignationDef { get; }

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDef);
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (Designation des in pawn.Map.designationManager.SpawnedDesignationsOfDef(DesignationDef))
            {
                if (des.target.Thing != null)
                    yield return des.target.Thing;
            }
        }

        /// <summary>
        /// Returns true if at least one of this item's valid bench defs is
        /// serviced by this WorkGiver's workType AND is actually present on the
        /// map. This is the cheap early-out used before paying for FindBench.
        /// </summary>
        protected bool ItemHasMatchingBench(Pawn pawn, Thing item)
        {
            List<ThingDef> validBenchDefs = WorkbenchRouter.GetValidBenches(item);
            if (validBenchDefs == null || validBenchDefs.Count == 0)
                return false;

            WorkTypeDef requiredWorkType = def.workType;
            for (int i = 0; i < validBenchDefs.Count; i++)
            {
                ThingDef benchDef = validBenchDefs[i];
                if (!R4WorkbenchFilterCache.BenchWorkTypes.TryGetValue(benchDef, out WorkTypeDef wt))
                    continue;
                if (wt != requiredWorkType)
                    continue;
                // Check at least one instance of this bench exists on the map
                if (pawn.Map.listerThings.ThingsOfDef(benchDef).Count > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Finds the closest reachable, usable bench for the item whose
        /// WorkTypeDef matches this WorkGiver's def.workType.
        /// Call ItemHasMatchingBench before this to avoid the GenClosest cost
        /// when no bench of the right type exists.
        /// </summary>
        protected Thing FindBench(Pawn pawn, Thing item, bool forced)
        {
            List<ThingDef> validBenchDefs = WorkbenchRouter.GetValidBenches(item);
            if (validBenchDefs == null || validBenchDefs.Count == 0)
                return null;

            WorkTypeDef requiredWorkType = def.workType;
            var candidates = new List<Thing>();
            for (int i = 0; i < validBenchDefs.Count; i++)
            {
                ThingDef benchDef = validBenchDefs[i];
                if (!R4WorkbenchFilterCache.BenchWorkTypes.TryGetValue(benchDef, out WorkTypeDef wt))
                    continue;
                if (wt != requiredWorkType)
                    continue;

                List<Thing> spawned = pawn.Map.listerThings.ThingsOfDef(benchDef);
                if (spawned != null)
                    candidates.AddRange(spawned);
            }

            if (candidates.Count == 0)
                return null;

            TraverseParms traverseParms = TraverseParms.For(pawn, pawn.NormalMaxDanger(), TraverseMode.ByPawn);

            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.Undefined),
                PathEndMode.InteractionCell,
                traverseParms,
                9999f,
                delegate (Thing bench)
                {
                    if (bench.IsForbidden(pawn)) return false;
                    if (!pawn.CanReserve(bench, 1, -1, null, forced)) return false;
                    if (bench is IBillGiver bg && !bg.UsableForBillsAfterFueling()) return false;
                    return true;
                },
                candidates);
        }
    }
}
