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
    /// </summary>
    public class WorkGiver_R4RepairBill : WorkGiver_Scanner
    {
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
            IBillGiver billGiver = workbench as IBillGiver;
            if (billGiver == null)
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

                SkillRequirement skillReq = bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn);
                if (skillReq != null)
                    continue;

                // Find all candidate damaged items matching the bill, sorted by distance
                var candidates = FindCandidateItems(pawn, workbench, bill, forced);

                // Try each candidate until we find one with available materials
                for (int c = 0; c < candidates.Count; c++)
                {
                    Thing item = candidates[c];

                    Job job = JobMaker.MakeJob(R4DefOf.RRRR_Repair, workbench);
                    job.count = 1;
                    job.bill = bill;
                    job.targetQueueA = new List<LocalTargetInfo> { item };
                    job.targetQueueB = new List<LocalTargetInfo>();
                    job.countQueue = new List<int>();

                    // Minor mending: no materials needed
                    if (WorkGiver_R4Repair.IsMinorMending(item))
                        return job;

                    var cycleCost = MaterialUtility.GetRepairCycleCost(item);
                    if (cycleCost.Count == 0)
                        return job; // No cost = free repair

                    if (MaterialUtility.TryFindIngredients(cycleCost, pawn, out var foundThings, out var foundCounts))
                    {
                        for (int i = 0; i < foundThings.Count; i++)
                        {
                            job.targetQueueB.Add(foundThings[i]);
                            job.countQueue.Add(foundCounts[i]);
                        }
                        return job;
                    }
                    // Materials not available for this item — try the next candidate
                }
            }

            return null;
        }

        /// <summary>
        /// Find all damaged items on the map matching the bill's filters,
        /// sorted by distance to pawn. Returns multiple candidates so we
        /// can try different items if materials aren't available for the closest.
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
            float radiusSq = searchRadius * searchRadius;
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

            // Sort by distance to pawn — try closest items first
            candidates.Sort((a, b2) =>
                (a.Position - pawn.Position).LengthHorizontalSquared
                .CompareTo((b2.Position - pawn.Position).LengthHorizontalSquared));

            return candidates;
        }
    }
}
