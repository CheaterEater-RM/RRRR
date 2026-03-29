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
    /// this WorkGiver's def.workType, ensuring e.g. a Smithing pawn only picks
    /// up smithy items and a Tailoring pawn only picks up tailor bench items.
    ///
    /// Items that route to multiple bench types (e.g. tribal apparel craftable
    /// at both CraftingSpot and HandTailoringBench) will be accepted by both the
    /// Crafting and Tailoring WorkGivers; the pawn with the highest-priority
    /// enabled column wins.
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
        /// Finds the closest reachable bench for the item whose WorkTypeDef
        /// matches this WorkGiver's def.workType.
        /// </summary>
        protected Thing FindBench(Pawn pawn, Thing item, bool forced)
        {
            List<ThingDef> validBenchDefs = WorkbenchRouter.GetValidBenches(item);
            if (validBenchDefs == null || validBenchDefs.Count == 0)
                return null;

            // Only consider benches serviced by this WorkGiver's workType
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
