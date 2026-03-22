using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Scans for items designated R4_Recycle, finds a nearby bench,
    /// and creates a recycle job. Uses Crafting work type.
    /// </summary>
    public class WorkGiver_R4Recycle : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            // Fast early-out: no designations of our type on the map at all
            return !pawn.Map.designationManager.AnySpawnedDesignationOfDef(R4DefOf.R4_Recycle);
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (var des in pawn.Map.designationManager.SpawnedDesignationsOfDef(R4DefOf.R4_Recycle))
            {
                if (des.target.Thing != null)
                    yield return des.target.Thing;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            // Verify designation still exists
            if (pawn.Map.designationManager.DesignationOn(t, R4DefOf.R4_Recycle) == null)
                return false;

            // Can the pawn reach and reserve the item?
            if (!pawn.CanReserve(t, 1, -1, null, forced))
                return false;

            // Is the item forbidden?
            if (t.IsForbidden(pawn))
                return false;

            // Find a valid bench
            Thing bench = FindBench(pawn, t, forced);
            if (bench == null)
                return false;

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            // Double-check designation
            if (pawn.Map.designationManager.DesignationOn(t, R4DefOf.R4_Recycle) == null)
                return null;

            Thing bench = FindBench(pawn, t, forced);
            if (bench == null)
                return null;

            // TargetA = item to recycle, TargetB = workbench
            Job job = JobMaker.MakeJob(R4DefOf.RRRR_Recycle, t, bench);
            job.count = 1;
            return job;
        }

        /// <summary>
        /// Find the closest reachable and usable bench for the given item.
        /// Routes by smeltable/non-smeltable per WorkbenchRouter.
        /// </summary>
        private Thing FindBench(Pawn pawn, Thing item, bool forced)
        {
            var validBenchDefs = WorkbenchRouter.GetValidBenches(item);
            if (validBenchDefs == null || validBenchDefs.Count == 0)
                return null;

            // Build a candidate list of all spawned benches of valid types
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

            Thing closest = GenClosest.ClosestThingReachable(
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

            return closest;
        }
    }
}
