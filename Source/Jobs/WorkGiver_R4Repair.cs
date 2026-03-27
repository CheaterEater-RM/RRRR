using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Scans for items designated R4_Repair, finds a nearby bench and
    /// the required materials, and creates a job with ingredients queued.
    /// 
    /// Job target layout (matches vanilla DoBill pattern):
    ///   TargetA = workbench
    ///   TargetQueueA[0] = item to repair (single-element queue)
    ///   TargetQueueB = ingredient stacks, countQueue = amounts
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

            var cycleCost = MaterialUtility.GetRepairCycleCost(t);
            if (cycleCost.Count > 0)
            {
                if (!MaterialUtility.TryFindIngredients(cycleCost, pawn, out _, out _))
                    return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn.Map.designationManager.DesignationOn(t, R4DefOf.R4_Repair) == null)
                return null;

            Thing bench = FindBench(pawn, t, forced);
            if (bench == null)
                return null;

            var cycleCost = MaterialUtility.GetRepairCycleCost(t);

            // TargetA = bench (matches vanilla DoBill)
            Job job = JobMaker.MakeJob(R4DefOf.RRRR_Repair, bench);
            job.count = 1;

            // Item stored in targetQueueA as single element
            job.targetQueueA = new List<LocalTargetInfo> { t };

            // Ingredients in targetQueueB
            if (cycleCost.Count > 0)
            {
                if (!MaterialUtility.TryFindIngredients(cycleCost, pawn, out var foundThings, out var foundCounts))
                    return null;

                job.targetQueueB = new List<LocalTargetInfo>();
                job.countQueue = new List<int>();
                for (int i = 0; i < foundThings.Count; i++)
                {
                    job.targetQueueB.Add(foundThings[i]);
                    job.countQueue.Add(foundCounts[i]);
                }
            }

            return job;
        }

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
                pawn.Position, pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.Undefined),
                PathEndMode.InteractionCell, traverseParms, 9999f,
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
