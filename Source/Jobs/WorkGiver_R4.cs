using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// WorkGiver that scans for items with R⁴ designations and creates jobs.
    /// </summary>
    public class WorkGiver_R4 : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override ThingRequest PotentialWorkThingRequest =>
            ThingRequest.ForGroup(ThingRequestGroup.HaulableEver);

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var comp = t.TryGetComp<CompRecyclable>();
            if (comp == null || comp.Designation == R4Designation.None)
                return false;

            if (t.IsForbidden(pawn) || !pawn.CanReserve(t, ignoreOtherReservations: forced))
                return false;

            // Only handle recycle for now (M1); repair and clean in M2/M3
            if (comp.Designation != R4Designation.MarkedRecycle)
                return false;

            var bench = WorkbenchRouter.FindBestBench(t, pawn);
            return bench != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var comp = t.TryGetComp<CompRecyclable>();
            if (comp == null)
                return null;

            var bench = WorkbenchRouter.FindBestBench(t, pawn);
            if (bench == null)
                return null;

            switch (comp.Designation)
            {
                case R4Designation.MarkedRecycle:
                    return JobMaker.MakeJob(R4DefOf.RRRR_Recycle, bench, t);
                // M2: case R4Designation.MarkedRepair:
                // M3: case R4Designation.MarkedClean:
                default:
                    return null;
            }
        }
    }
}
