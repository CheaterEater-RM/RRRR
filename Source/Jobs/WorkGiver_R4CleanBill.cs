using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Custom WorkGiver for clean bills. Scans the bench's bill stack for
    /// our clean RecipeDefs, finds tainted apparel matching the bill filters,
    /// gathers clean materials, and creates our custom RRRR_Clean jobs
    /// (which haul both the apparel AND materials to the bench).
    ///
    /// Mirrors WorkGiver_R4RepairBill in structure. Vanilla WorkGiver_DoBill
    /// is blocked from R4 clean recipes by Patch_WorkGiver_DoBill_JobOnThing.
    /// </summary>
    public class WorkGiver_R4CleanBill : WorkGiver_Scanner
    {
        // Matches vanilla WorkGiver_DoBill.ReCheckFailedBillTicksRange
        private static readonly IntRange ReCheckFailedBillTicksRange = new IntRange(500, 600);

        private static bool IsCleanRecipe(RecipeDef recipe) =>
            recipe.workerClass == typeof(RecipeWorker_R4Clean);

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
                if (!IsCleanRecipe(bill.recipe))
                    continue;
                if (!bill.ShouldDoNow() || !bill.PawnAllowedToStartAnew(pawn))
                    continue;

                // Vanilla throttle: skip if a recent search already failed and
                // the cooldown hasn't expired yet.
                if (!forced && Find.TickManager.TicksGame < bill.nextTickToSearchForIngredients)
                    continue;

                SkillRequirement skillReq = bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn);
                if (skillReq != null)
                    continue;

                List<Thing> candidates = FindCandidateItems(pawn, workbench, bill, forced);

                for (int c = 0; c < candidates.Count; c++)
                {
                    Thing item = candidates[c];
                    List<ThingDefCountClass> cleanCost = MaterialUtility.GetCleanCost(item);

                    List<Thing> foundThings = null;
                    List<int> foundCounts = null;

                    if (cleanCost.Count > 0 && !MaterialUtility.TryFindIngredients(
                            cleanCost,
                            pawn,
                            workbench.Position,
                            bill.ingredientSearchRadius,
                            out foundThings,
                            out foundCounts))
                    {
                        continue;
                    }

                    Job job = JobMaker.MakeJob(R4DefOf.RRRR_Clean, workbench);
                    job.count = 1;
                    job.bill = bill;
                    job.targetQueueA = new List<LocalTargetInfo> { item };
                    job.targetQueueB = new List<LocalTargetInfo>();
                    job.countQueue = new List<int>();

                    if (foundThings != null)
                    {
                        for (int i = 0; i < foundThings.Count; i++)
                        {
                            job.targetQueueB.Add(foundThings[i]);
                            job.countQueue.Add(foundCounts[i]);
                        }
                    }

                    return job;
                }

                // No candidate produced a valid job — set vanilla-style cooldown.
                if (!forced)
                    bill.nextTickToSearchForIngredients =
                        Find.TickManager.TicksGame + ReCheckFailedBillTicksRange.RandomInRange;
            }

            return null;
        }

        /// <summary>
        /// Find all tainted apparel within the bill's ingredient search radius,
        /// sorted by distance to the workbench (matching vanilla bill behaviour).
        /// Uses region traversal for efficiency rather than a full map scan.
        /// </summary>
        private List<Thing> FindCandidateItems(Pawn pawn, Thing workbench, Bill bill, bool forced)
        {
            var candidates = new List<Thing>();

            IntVec3 rootCell = (workbench is Building building && building.def.hasInteractionCell)
                ? building.InteractionCell : workbench.Position;
            Region rootReg = rootCell.GetRegion(pawn.Map);
            if (rootReg == null)
                return candidates;

            float searchRadius = bill.ingredientSearchRadius;
            float radiusSq = searchRadius * searchRadius;
            TraverseParms traverseParms = TraverseParms.For(pawn);

            RegionEntryPredicate entryCondition = (from, region) =>
                region.Allows(traverseParms, isDestination: false);

            RegionProcessor regionProcessor = delegate(Region region)
            {
                List<Thing> things = region.ListerThings.ThingsMatching(
                    ThingRequest.ForGroup(ThingRequestGroup.HaulableEver));

                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing.IsForbidden(pawn) || !pawn.CanReserve(thing, 1, -1, null, forced))
                        continue;
                    if ((thing.Position - workbench.Position).LengthHorizontalSquared > radiusSq)
                        continue;
                    if (!bill.IsFixedOrAllowedIngredient(thing))
                        continue;
                    if (!(thing is Apparel apparel) || !apparel.WornByCorpse)
                        continue;

                    candidates.Add(thing);
                }

                return false;
            };

            RegionTraverser.BreadthFirstTraverse(rootReg, entryCondition, regionProcessor, 99999);

            IntVec3 benchPos = workbench.Position;
            candidates.Sort((a, b) =>
                (a.Position - benchPos).LengthHorizontalSquared
                    .CompareTo((b.Position - benchPos).LengthHorizontalSquared));

            return candidates;
        }
    }
}