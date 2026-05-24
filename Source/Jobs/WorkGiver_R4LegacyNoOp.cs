using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Save-compatibility shell for bill WorkGiverDefs removed from active use.
    /// It resolves old Job.workGiverDef references without adding a scanner.
    /// </summary>
    public class WorkGiver_R4LegacyNoOp : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest =>
            ThingRequest.ForGroup(ThingRequestGroup.Undefined);

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => true;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) => false;

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => null;
    }
}
