using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Custom WorkGiver for repair bills. Scans the bench's bill stack for
    /// our repair RecipeDefs, finds damaged items matching the bill filters,
    /// gathers repair materials, and creates our custom RRRR_Repair jobs
    /// (which haul both the item AND materials to the bench).
    ///
    /// Performance notes:
    /// - nextTickToSearchForIngredients is checked and set to match vanilla
    ///   WorkGiver_DoBill throttling (~500-600 tick cooldown after a failed
    ///   ingredient search).
    /// - FindCandidateItems uses region traversal bounded by ingredientSearchRadius
    ///   and sorts candidates by distance to bench (not pawn), which matches
    ///   how vanilla bill ingredient searches prioritise work location.
    /// </summary>
    public class WorkGiver_R4RepairBill : WorkGiver_Scanner
    {
        // Matches vanilla WorkGiver_DoBill.ReCheckFailedBillTicksRange
        private static readonly IntRange ReCheckFailedBillTicksRange = new IntRange(500, 600);

        private static bool IsRepairRecipe(RecipeDef recipe) =>
            recipe.workerClass == typeof(RecipeWorker_R4Repair);

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
            if (def.fixedBillGiverDefs == null || !def.fixedBillGiverDefs.Contains(workbench.def))
                return null;
            if (!billGiver.CurrentlyUsableForBills())
                return null;
            if (!billGiver.BillStack.AnyShouldDoNow)
                return null;
            if (workbench.IsBurning() || workbench.IsForbidden(pawn))
                return null;
            if (!pawn.CanReserve(workbench, 1, -1, null, forced))
                return null;

            foreach (Bill bill in billGiver.BillStack)
            {
                if (!IsRepairRecipe(bill.recipe))
                    continue;
                if (!bill.ShouldDoNow() || !bill.PawnAllowedToStartAnew(pawn))
                    continue;

                // Vanilla throttle: skip if a recent search already failed and
                // the cooldown hasn't expired yet
                if (!forced && Find.TickManager.TicksGame < bill.nextTickToSearchForIngredients)
                    continue;

                SkillRequirement skillReq = bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn);
                if (skillReq != null)
                    continue;

                var candidates = FindCandidateItems(pawn, workbench, bill, forced);

                for (int c = 0; c < candidates.Count; c++)
                {
                    Thing item = candidates[c];

                    Job job = JobMaker.MakeJob(R4DefOf.RRRR_Repair, workbench);
                    job.count        = 1;
                    job.bill         = bill;
                    job.targetQueueA = new List<LocalTargetInfo> { item };
                    job.targetQueueB = new List<LocalTargetInfo>();
                    job.countQueue   = new List<int>();

                    // Minor mending: free, no materials needed
                    if (WorkGiver_R4Repair.IsMinorMending(item))
                        return job;

                    var cycleCost = MaterialUtility.GetRepairCycleCost(item);
                    if (cycleCost.Count == 0)
                        return job; // costless item

                    if (MaterialUtility.TryFindIngredients(cycleCost, pawn, workbench.Position,
                            bill.ingredientSearchRadius, out var foundThings, out var foundCounts))
                    {
                        for (int i = 0; i < foundThings.Count; i++)
                        {
                            job.targetQueueB.Add(foundThings[i]);
                            job.countQueue.Add(foundCounts[i]);
                        }
                        return job;
                    }
                    // Materials not found for this candidate — try the next one
                }

                // No candidate produced a valid job — set vanilla-style cooldown
                if (!forced)
                    bill.nextTickToSearchForIngredients =
                        Find.TickManager.TicksGame + ReCheckFailedBillTicksRange.RandomInRange;
            }

            return null;
        }

        /// <summary>
        /// Find all damaged items within the bill's ingredient search radius,
        /// sorted by distance to the workbench (matching vanilla bill behaviour).
        /// Uses region traversal for efficiency rather than a full map scan.
        /// </summary>
        private List<Thing> FindCandidateItems(Pawn pawn, Thing workbench, Bill bill, bool forced)
        {
            var candidates = new List<Thing>();

            IntVec3 rootCell = (workbench is Building b && b.def.hasInteractionCell)
                ? b.InteractionCell : workbench.Position;
            Region rootReg = rootCell.GetRegion(pawn.Map);
            if (rootReg == null)
                return candidates;

            float searchRadius = bill.ingredientSearchRadius;
            float radiusSq     = searchRadius * searchRadius;
            TraverseParms traverseParms = TraverseParms.For(pawn);

            RegionEntryPredicate entryCondition = (from, r) => r.Allows(traverseParms, isDestination: false);

            RegionProcessor regionProcessor = delegate (Region r)
            {
                var things = r.ListerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.HaulableEver));
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t.IsForbidden(pawn) || !pawn.CanReserve(t, 1, -1, null, forced))
                        continue;
                    // Radius measured from bench, matching vanilla ingredient search
                    if ((t.Position - workbench.Position).LengthHorizontalSquared > radiusSq)
                        continue;
                    if (!bill.IsFixedOrAllowedIngredient(t))
                        continue;
                    if (!t.def.useHitPoints || t.HitPoints >= t.MaxHitPoints)
                        continue;

                    candidates.Add(t);
                }
                return false;
            };

            RegionTraverser.BreadthFirstTraverse(rootReg, entryCondition, regionProcessor, 99999);

            // Sort by distance to bench — closest to the work location first,
            // matching vanilla's ingredient prioritisation logic
            IntVec3 benchPos = workbench.Position;
            candidates.Sort((a, b2) =>
                (a.Position - benchPos).LengthHorizontalSquared
                .CompareTo((b2.Position - benchPos).LengthHorizontalSquared));

            return candidates;
        }
    }
}
