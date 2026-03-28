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
    /// Minor mending: items at ≥95% HP are repaired for free (no materials).
    /// 
    /// Job target layout (matches vanilla DoBill pattern):
    ///   TargetA = workbench
    ///   TargetQueueA[0] = item to repair (single-element queue)
    ///   TargetQueueB = ingredient stacks, countQueue = amounts (empty for minor mending)
    /// </summary>
    public class WorkGiver_R4Repair : WorkGiver_Scanner
    {
        /// <summary>
        /// Items at or above this HP fraction are considered minor damage
        /// and can be mended for free without materials.
        /// </summary>
        public const float MinorMendingThreshold = 0.95f;

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

        public static bool IsMinorMending(Thing item)
        {
            if (!item.def.useHitPoints || item.MaxHitPoints <= 0)
                return false;
            return (float)item.HitPoints / item.MaxHitPoints >= MinorMendingThreshold;
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

            // Minor mending: skip material check — it's free
            if (!IsMinorMending(t))
            {
                var cycleCost = MaterialUtility.GetRepairCycleCost(t);
                if (cycleCost.Count > 0)
                {
                    if (!MaterialUtility.TryFindIngredients(cycleCost, pawn, out _, out _))
                        return false;
                }
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

            // TargetA = bench (matches vanilla DoBill)
            Job job = JobMaker.MakeJob(R4DefOf.RRRR_Repair, bench);
            job.count = 1;
            job.targetQueueA = new List<LocalTargetInfo> { t };

            // Minor mending: no ingredients needed
            job.targetQueueB = new List<LocalTargetInfo>();
            job.countQueue = new List<int>();
            
            if (!IsMinorMending(t))
            {
                var cycleCost = MaterialUtility.GetRepairCycleCost(t);
                if (cycleCost.Count > 0)
                {
                    if (!MaterialUtility.TryFindIngredients(cycleCost, pawn, out var foundThings, out var foundCounts))
                        return null;

                    for (int i = 0; i < foundThings.Count; i++)
                    {
                        job.targetQueueB.Add(foundThings[i]);
                        job.countQueue.Add(foundCounts[i]);
                    }
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
