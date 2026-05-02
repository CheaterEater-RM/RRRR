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

        private int _cachedTick = -1;
        private Pawn _cachedPawn;
        private Thing _cachedWorkbench;
        private bool _cachedForced;
        private Job _cachedJob;

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
            return GetOrCreateCachedJob(pawn, thing, forced) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing workbench, bool forced = false)
        {
            return GetOrCreateCachedJob(pawn, workbench, forced);
        }

        private Job GetOrCreateCachedJob(Pawn pawn, Thing workbench, bool forced)
        {
            int currentTick = Find.TickManager.TicksGame;
            if (_cachedTick == currentTick && _cachedPawn == pawn && _cachedWorkbench == workbench && _cachedForced == forced)
            {
                R4Log.Debug(
                    $"Clean bill cache hit: pawn={pawn.LabelShort} bench={workbench?.def?.defName ?? "null"} tick={currentTick} hasJob={_cachedJob != null}");
                return _cachedJob;
            }

            Job job = CreateJobOnThing(pawn, workbench, forced);
            _cachedTick = currentTick;
            _cachedPawn = pawn;
            _cachedWorkbench = workbench;
            _cachedForced = forced;
            _cachedJob = job;
            return job;
        }

        private Job CreateJobOnThing(Pawn pawn, Thing workbench, bool forced)
        {
            if (!(workbench is IBillGiver billGiver))
                return null;
            if (def.fixedBillGiverDefs == null || !def.fixedBillGiverDefs.Contains(workbench.def))
                return null;
            if (!billGiver.CurrentlyUsableForBills())
                return null;
            billGiver.BillStack.RemoveIncompletableBills();
            if (!billGiver.BillStack.AnyShouldDoNow)
                return null;
            if (workbench.IsBurning() || workbench.IsForbidden(pawn))
                return null;
            if (!pawn.CanReserve(workbench, 1, -1, null, forced))
                return null;
            if (workbench.def.hasInteractionCell && !pawn.CanReserveSittableOrSpot(workbench.InteractionCell, workbench, forced))
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

                List<Thing> candidates = FindCandidateItems(pawn, workbench, bill, forced, out bool bisActive);
                R4Log.Debug(
                    $"Clean bill scan: pawn={pawn.LabelShort} bench={workbench.def.defName} bill={bill.recipe.defName} candidates={candidates.Count}");

                for (int c = 0; c < candidates.Count; c++)
                {
                    Thing item = candidates[c];
                    List<ThingDefCountClass> cleanCost = MaterialUtility.GetCleanCost(item);
                    var ingredientCounts = MaterialUtility.BuildIngredientCounts(cleanCost);
                    var chosenThings = new List<ThingCount>();

                    // When BIS controls item selection, it sets ingredientSearchRadius to 0.
                    // Materials still need a normal search radius — use vanilla default (999f).
                    float materialSearchRadius = bisActive ? 999f : bill.ingredientSearchRadius;

                    bool ingredientsFound = ingredientCounts.Count == 0 ||
                        WorkGiver_DoBill.TryFindBestFixedIngredients(
                            ingredientCounts, pawn, workbench, chosenThings,
                            materialSearchRadius);

                    if (!ingredientsFound)
                    {
                        continue;
                    }

                    // Clear stale ingredients before starting a new job
                    Job haulOff = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, billGiver, null);
                    if (haulOff != null) return haulOff;

                    Job job = JobMaker.MakeJob(R4DefOf.RRRR_Clean, workbench);
                    job.count    = 1;
                    job.bill     = bill;
                    job.haulMode = HaulMode.ToCellNonStorage;
                    job.placedThings = null;
                    job.targetQueueA = new List<LocalTargetInfo> { item };
                    job.targetQueueB = new List<LocalTargetInfo>();
                    job.countQueue = new List<int>();

                    for (int i = 0; i < chosenThings.Count; i++)
                    {
                        job.targetQueueB.Add(chosenThings[i].Thing);
                        job.countQueue.Add(chosenThings[i].Count);
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
        private List<Thing> FindCandidateItems(Pawn pawn, Thing workbench, Bill bill, bool forced, out bool bisActive)
        {
            var candidates = new List<Thing>();
            bisActive = false;

            IntVec3 rootCell = (workbench is Building building && building.def.hasInteractionCell)
                ? building.InteractionCell : workbench.Position;
            Region rootReg = rootCell.GetRegion(pawn.Map);
            if (rootReg == null)
                return candidates;

            // BIS fast path: when a storage source is configured, iterate the BIS
            // candidate list directly instead of traversing every region on the map.
            HashSet<int> bisIDs = BISCompat.GetStorageCandidateIDs(bill, pawn.Map);
            if (bisIDs != null)
            {
                bisActive = true;
                var bisThings = BISCompat.GetStorageCandidateThings(bill, pawn.Map);
                if (bisThings != null)
                {
                    for (int i = 0; i < bisThings.Count; i++)
                    {
                        Thing thing = bisThings[i];
                        if (thing.IsForbidden(pawn) || !pawn.CanReserve(thing, 1, -1, null, forced))
                            continue;
                        if (!bill.IsFixedOrAllowedIngredient(thing))
                            continue;
                        if (!(thing is Apparel apparel) || !apparel.WornByCorpse)
                            continue;
                        if (!pawn.CanReach(thing, PathEndMode.ClosestTouch, pawn.NormalMaxDanger()))
                            continue;

                        candidates.Add(thing);
                    }
                }

                IntVec3 benchPos = workbench.Position;
                candidates.Sort((a, b) =>
                    (a.Position - benchPos).LengthHorizontalSquared
                        .CompareTo((b.Position - benchPos).LengthHorizontalSquared));

                return candidates;
            }

            float searchRadius = bill.ingredientSearchRadius;
            float radiusSq = searchRadius * searchRadius;
            bool  useRadius = searchRadius > 0f && searchRadius < 9999f;
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
                    if (useRadius && (thing.Position - workbench.Position).LengthHorizontalSquared > radiusSq)
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

            IntVec3 benchPos2 = workbench.Position;
            candidates.Sort((a, b) =>
                (a.Position - benchPos2).LengthHorizontalSquared
                    .CompareTo((b.Position - benchPos2).LengthHorizontalSquared));

            return candidates;
        }
    }
}