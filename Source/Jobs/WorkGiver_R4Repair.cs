using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Scans for items designated R4_Repair, finds a nearby bench,
    /// and creates a repair job. Uses Crafting work type.
    /// </summary>
    public class WorkGiver_R4Repair : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return !pawn.Map.designationManager.AnySpawnedDesignationOfDef(R4DefOf.R4_Repair);
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (var des in pawn.Map.designationManager.SpawnedDesignationsOfDef(R4DefOf.R4_Repair))
            {
                if (des.target.Thing != null)
                    yield return des.target.Thing;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn.Map.designationManager.DesignationOn(t, R4DefOf.R4_Repair) == null)
                return false;

            if (!pawn.CanReserve(t, 1, -1, null, forced))
                return false;

            if (t.IsForbidden(pawn))
                return false;

            Thing bench = FindBench(pawn, t, forced);
            if (bench == null)
                return false;

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn.Map.designationManager.DesignationOn(t, R4DefOf.R4_Repair) == null)
                return null;

            Thing bench = FindBench(pawn, t, forced);
            if (bench == null)
                return null;

            Job job = JobMaker.MakeJob(R4DefOf.RRRR_Repair, t, bench);
            job.count = 1;
            return job;
        }

        /// <summary>
        /// Reuses the same bench-finding logic as recycle.
        /// Repair uses the same workbenches.
        /// </summary>
        private Thing FindBench(Pawn pawn, Thing item, bool forced)
        {
            var validBenchDefs = WorkbenchRouter.GetValidBenches(item);
            if (validBenchDefs == null || validBenchDefs.Count == 0)
                return null;

            var candidates = new List<Thing>();
            for (int i = 0; i < validBenchDefs.Count; i++)
            {
                var benchesOfType = pawn.Map.listerThings.ThingsOfDef(validBenchDefs[i]);
                if (benchesOfType != null)
                    candidates.AddRange(benchesOfType);
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
                    if (bench.IsForbidden(pawn))
                        return false;
                    if (!pawn.CanReserve(bench, 1, -1, null, forced))
                        return false;
                    if (bench is IBillGiver billGiver && !billGiver.UsableForBillsAfterFueling())
                        return false;
                    return true;
                },
                candidates);
        }
    }
}
