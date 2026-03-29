using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Designation-based repair WorkGiver. Registered under three WorkGiverDefs
    /// (Crafting / Smithing / Tailoring) so the work tab column matches the bench.
    /// </summary>
    public class WorkGiver_R4Repair : WorkGiver_R4DesignationBase
    {
        /// <summary>
        /// Items at or above this HP fraction are minor damage and can be
        /// mended for free without consuming any materials.
        /// </summary>
        public const float MinorMendingThreshold = 0.95f;

        protected override DesignationDef DesignationDef => R4DefOf.R4_Repair;

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
            if (FindBench(pawn, t, forced) == null)
                return false;

            if (!IsMinorMending(t))
            {
                var cycleCost = MaterialUtility.GetRepairCycleCost(t);
                if (cycleCost.Count > 0 && !MaterialUtility.TryFindIngredients(cycleCost, pawn, out _, out _))
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

            Job job = JobMaker.MakeJob(R4DefOf.RRRR_Repair, bench);
            job.count = 1;
            job.targetQueueA = new List<LocalTargetInfo> { t };
            job.targetQueueB = new List<LocalTargetInfo>();
            job.countQueue   = new List<int>();

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
    }
}
