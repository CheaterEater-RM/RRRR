using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Designation-based clean WorkGiver. Registered under three WorkGiverDefs
    /// (Crafting / Smithing / Tailoring) so the work tab column matches the bench.
    /// In practice only Crafting and Tailoring will ever have tainted apparel
    /// routed to them, but all three are registered for consistency.
    /// </summary>
    public class WorkGiver_R4Clean : WorkGiver_R4DesignationBase
    {
        protected override DesignationDef DesignationDef => R4DefOf.R4_Clean;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn.Map.designationManager.DesignationOn(t, R4DefOf.R4_Clean) == null)
                return false;
            if (t.IsForbidden(pawn) || !pawn.CanReserve(t, 1, -1, null, forced))
                return false;
            if (!ItemHasMatchingBench(pawn, t))
                return false;

            Thing bench = FindBench(pawn, t, forced);
            if (bench == null)
                return false;

            var cleanCost = MaterialUtility.GetCleanCost(t);
            // Search from bench position — materials should be near the work location
            if (cleanCost.Count > 0 &&
                !MaterialUtility.TryFindIngredients(cleanCost, pawn, bench.Position, 999f, out _, out _))
                return false;

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn.Map.designationManager.DesignationOn(t, R4DefOf.R4_Clean) == null)
                return null;

            Thing bench = FindBench(pawn, t, forced);
            if (bench == null)
                return null;

            Job job = JobMaker.MakeJob(R4DefOf.RRRR_Clean, bench);
            job.count        = 1;
            job.targetQueueA = new List<LocalTargetInfo> { t };
            job.targetQueueB = new List<LocalTargetInfo>();
            job.countQueue   = new List<int>();

            var cleanCost = MaterialUtility.GetCleanCost(t);
            if (cleanCost.Count > 0)
            {
                if (!MaterialUtility.TryFindIngredients(cleanCost, pawn, bench.Position, 999f,
                        out var foundThings, out var foundCounts))
                    return null;
                for (int i = 0; i < foundThings.Count; i++)
                {
                    job.targetQueueB.Add(foundThings[i]);
                    job.countQueue.Add(foundCounts[i]);
                }
            }

            return job;
        }
    }
}
