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
            return JobOnThing(pawn, t, forced) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn.Map.designationManager.DesignationOn(t, R4DefOf.R4_Clean) == null)
                return null;
            if (t.IsForbidden(pawn) || !pawn.CanReserve(t, 1, -1, null, forced))
                return null;
            if (!(t is Apparel apparel) || !apparel.WornByCorpse)
                return null;
            if (!ItemHasMatchingBench(pawn, t))
                return null;

            Thing bench = FindBench(pawn, t, forced);
            if (bench == null)
                return null;

            // Clear stale ingredients before starting a new job
            if (bench is IBillGiver bg)
            {
                Job haulOff = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, bg, null);
                if (haulOff != null) return haulOff;
            }

            Job job = JobMaker.MakeJob(R4DefOf.RRRR_Clean, bench);
            job.count        = 1;
            job.haulMode     = HaulMode.ToCellNonStorage;
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
