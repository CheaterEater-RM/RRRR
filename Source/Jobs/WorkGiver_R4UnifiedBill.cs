using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Fallback bill scanner for modded benches that expose R4 bills but are
    /// not claimed by any vanilla WorkGiver_DoBill. Normal benches use the
    /// WorkGiver_DoBill postfix instead.
    /// </summary>
    public class WorkGiver_R4UnifiedBill : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override ThingRequest PotentialWorkThingRequest
        {
            get
            {
                if (def.fixedBillGiverDefs != null && def.fixedBillGiverDefs.Count == 1)
                    return ThingRequest.ForDef(def.fixedBillGiverDefs[0]);
                return ThingRequest.ForGroup(ThingRequestGroup.PotentialBillGiver);
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            return JobOnThing(pawn, thing, forced) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing workbench, bool forced = false)
        {
            if (!(workbench is IBillGiver billGiver))
                return null;
            if (billGiver.BillStack == null)
                return null;
            if (def.fixedBillGiverDefs != null && !def.fixedBillGiverDefs.Contains(workbench.def))
                return null;

            billGiver.BillStack.RemoveIncompletableBills();
            if (!R4BillJobFactory.HasAnyR4RepairOrCleanBill(billGiver))
                return null;
            if (!R4BillJobFactory.PassesFallbackBenchPrechecks(pawn, workbench, forced))
                return null;

            Job refuelJob = R4BillJobFactory.RefuelJobIfNeeded(pawn, workbench, forced);
            if (refuelJob != null)
                return refuelJob;

            return R4BillJobFactory.TryDispatchAboveIndex(
                pawn, workbench, billGiver, billGiver.BillStack.Count, forced);
        }
    }
}
