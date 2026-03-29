using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Designation-based recycle WorkGiver. Registered under three WorkGiverDefs
    /// (Crafting / Smithing / Tailoring) so the work tab column matches the bench.
    /// </summary>
    public class WorkGiver_R4Recycle : WorkGiver_R4DesignationBase
    {
        protected override DesignationDef DesignationDef => R4DefOf.R4_Recycle;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn.Map.designationManager.DesignationOn(t, R4DefOf.R4_Recycle) == null)
                return false;
            if (!pawn.CanReserve(t, 1, -1, null, forced))
                return false;
            if (t.IsForbidden(pawn))
                return false;
            return FindBench(pawn, t, forced) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn.Map.designationManager.DesignationOn(t, R4DefOf.R4_Recycle) == null)
                return null;

            Thing bench = FindBench(pawn, t, forced);
            if (bench == null)
                return null;

            Job job = JobMaker.MakeJob(R4DefOf.RRRR_Recycle, t, bench);
            job.count = 1;
            return job;
        }
    }
}
