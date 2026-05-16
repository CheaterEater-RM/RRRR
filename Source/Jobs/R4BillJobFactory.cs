using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Shared repair/clean bill dispatch for the WorkGiver_DoBill postfix and
    /// orphan-bench fallback WorkGiver. Authored against RimWorld 1.6.
    /// </summary>
    public static class R4BillJobFactory
    {
        private static readonly IntRange ReCheckFailedBillTicksRange = new IntRange(500, 600);

        private static int _cachedTick = -1;
        private static Pawn _cachedPawn;
        private static Thing _cachedWorkbench;
        private static bool _cachedForced;
        private static int _cachedUpperExclusiveIndex;
        private static Job _cachedJob;

        public static bool IsR4RepairOrClean(RecipeDef recipe)
        {
            return recipe != null &&
                   (recipe.workerClass == typeof(RecipeWorker_R4Repair) ||
                    recipe.workerClass == typeof(RecipeWorker_R4Clean));
        }

        public static bool HasAnyR4RepairOrCleanBill(IBillGiver billGiver)
        {
            if (billGiver?.BillStack == null)
                return false;

            for (int i = 0; i < billGiver.BillStack.Count; i++)
            {
                if (IsR4RepairOrClean(billGiver.BillStack[i].recipe))
                    return true;
            }

            return false;
        }

        public static bool PassesVanillaBenchPrechecks(
            WorkGiver_DoBill workGiver, Pawn pawn, Thing bench, bool forced)
        {
            if (workGiver == null || !workGiver.ThingIsUsableBillGiver(bench))
                return false;

            return PassesCommonBenchPrechecks(pawn, bench, forced, allowRefuelJob: false);
        }

        public static bool PassesFallbackBenchPrechecks(Pawn pawn, Thing bench, bool forced)
        {
            return PassesCommonBenchPrechecks(pawn, bench, forced, allowRefuelJob: true);
        }

        public static Job RefuelJobIfNeeded(Pawn pawn, Thing bench, bool forced)
        {
            CompRefuelable compRefuelable = bench?.TryGetComp<CompRefuelable>();
            if (compRefuelable == null || compRefuelable.HasFuel)
                return null;

            if (!RefuelWorkGiverUtility.CanRefuel(pawn, bench, forced))
                return null;

            return RefuelWorkGiverUtility.RefuelJob(pawn, bench, forced);
        }

        public static Job TryDispatchAboveIndex(
            Pawn pawn, Thing workbench, IBillGiver billGiver,
            int upperExclusiveIndex, bool forced)
        {
            int currentTick = Find.TickManager.TicksGame;
            if (_cachedTick == currentTick &&
                _cachedPawn == pawn &&
                _cachedWorkbench == workbench &&
                _cachedForced == forced)
            {
                if (_cachedJob != null)
                {
                    int cachedIndex = billGiver.BillStack.IndexOf(_cachedJob.bill);
                    if (cachedIndex >= 0 && cachedIndex < upperExclusiveIndex)
                    {
                        R4Log.Debug(
                            $"R4 bill dispatch cache hit: pawn={pawn.LabelShort} bench={workbench?.def?.defName ?? "null"} tick={currentTick} hasJob=true");
                        return _cachedJob;
                    }
                }

                if (_cachedJob == null && _cachedUpperExclusiveIndex >= upperExclusiveIndex)
                {
                    R4Log.Debug(
                        $"R4 bill dispatch cache hit: pawn={pawn.LabelShort} bench={workbench?.def?.defName ?? "null"} tick={currentTick} hasJob=false");
                    return null;
                }
            }

            Job job = TryDispatchAboveIndexUncached(pawn, workbench, billGiver, upperExclusiveIndex, forced);
            _cachedTick = currentTick;
            _cachedPawn = pawn;
            _cachedWorkbench = workbench;
            _cachedForced = forced;
            _cachedUpperExclusiveIndex = upperExclusiveIndex;
            _cachedJob = job;
            return job;
        }

        private static bool PassesCommonBenchPrechecks(
            Pawn pawn, Thing bench, bool forced, bool allowRefuelJob)
        {
            if (!(bench is IBillGiver billGiver))
                return false;
            if (!billGiver.BillStack.AnyShouldDoNow)
                return false;
            if (!billGiver.UsableForBillsAfterFueling())
                return false;
            if (!pawn.CanReserve(bench, 1, -1, null, forced))
                return false;
            if (bench.IsBurning())
                return false;
            if (bench.def.hasInteractionCell &&
                !pawn.CanReserveSittableOrSpot(bench.InteractionCell, bench, forced))
            {
                return false;
            }

            CompRefuelable compRefuelable = bench.TryGetComp<CompRefuelable>();
            if (!allowRefuelJob && compRefuelable != null && !compRefuelable.HasFuel)
                return false;

            return true;
        }

        private static Job TryDispatchAboveIndexUncached(
            Pawn pawn, Thing workbench, IBillGiver billGiver,
            int upperExclusiveIndex, bool forced)
        {
            if (billGiver?.BillStack == null)
                return null;

            billGiver.BillStack.RemoveIncompletableBills();

            int count = billGiver.BillStack.Count;
            int upper = upperExclusiveIndex < count ? upperExclusiveIndex : count;
            for (int i = 0; i < upper; i++)
            {
                Bill bill = billGiver.BillStack[i];
                if (!IsR4RepairOrClean(bill.recipe))
                    continue;
                if (Find.TickManager.TicksGame <= bill.nextTickToSearchForIngredients &&
                    FloatMenuMakerMap.makingFor != pawn)
                {
                    continue;
                }
                if (!bill.ShouldDoNow() || !bill.PawnAllowedToStartAnew(pawn))
                    continue;

                SkillRequirement skillReq = bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn);
                if (skillReq != null)
                {
                    JobFailReason.Is("UnderRequiredSkill".Translate(skillReq.minLevel), bill.Label);
                    continue;
                }

                Job job = null;
                if (bill.recipe.workerClass == typeof(RecipeWorker_R4Repair))
                    job = TryCreateRepairBillJob(pawn, workbench, bill, forced);
                else if (bill.recipe.workerClass == typeof(RecipeWorker_R4Clean))
                    job = TryCreateCleanBillJob(pawn, workbench, bill, forced);

                if (job != null)
                    return job;

                if (FloatMenuMakerMap.makingFor != pawn)
                {
                    bill.nextTickToSearchForIngredients =
                        Find.TickManager.TicksGame + ReCheckFailedBillTicksRange.RandomInRange;
                }
            }

            return null;
        }

        private static Job TryCreateRepairBillJob(Pawn pawn, Thing workbench, Bill bill, bool forced)
        {
            IBillGiver billGiver = (IBillGiver)workbench;
            List<Thing> candidates = FindRepairCandidateItems(pawn, workbench, bill, forced, out bool bisActive);
            R4Log.Debug(
                $"Repair bill scan: pawn={pawn.LabelShort} bench={workbench.def.defName} bill={bill.recipe.defName} candidates={candidates.Count}");

            for (int c = 0; c < candidates.Count; c++)
            {
                Thing item = candidates[c];

                if (WorkGiver_R4Repair.IsMinorMending(item))
                {
                    Job minorHaulOff = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, billGiver, null);
                    if (minorHaulOff != null) return minorHaulOff;

                    return MakeR4BillJob(R4DefOf.RRRR_Repair, workbench, bill, item, null);
                }

                List<ThingDefCountClass> cycleCost = MaterialUtility.GetRepairCycleCost(item);
                List<IngredientCount> ingredientCounts = MaterialUtility.BuildIngredientCounts(cycleCost);
                var chosenThings = new List<ThingCount>();
                float materialSearchRadius = bisActive ? 999f : bill.ingredientSearchRadius;

                bool ingredientsFound = ingredientCounts.Count == 0 ||
                    WorkGiver_DoBill.TryFindBestFixedIngredients(
                        ingredientCounts, pawn, workbench, chosenThings,
                        materialSearchRadius);

                if (!ingredientsFound)
                    continue;

                Job haulOff = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, billGiver, null);
                if (haulOff != null) return haulOff;

                return MakeR4BillJob(R4DefOf.RRRR_Repair, workbench, bill, item, chosenThings);
            }

            return null;
        }

        private static Job TryCreateCleanBillJob(Pawn pawn, Thing workbench, Bill bill, bool forced)
        {
            IBillGiver billGiver = (IBillGiver)workbench;
            List<Thing> candidates = FindCleanCandidateItems(pawn, workbench, bill, forced, out bool bisActive);
            R4Log.Debug(
                $"Clean bill scan: pawn={pawn.LabelShort} bench={workbench.def.defName} bill={bill.recipe.defName} candidates={candidates.Count}");

            for (int c = 0; c < candidates.Count; c++)
            {
                Thing item = candidates[c];
                List<ThingDefCountClass> cleanCost = MaterialUtility.GetCleanCost(item);
                List<IngredientCount> ingredientCounts = MaterialUtility.BuildIngredientCounts(cleanCost);
                var chosenThings = new List<ThingCount>();
                float materialSearchRadius = bisActive ? 999f : bill.ingredientSearchRadius;

                bool ingredientsFound = ingredientCounts.Count == 0 ||
                    WorkGiver_DoBill.TryFindBestFixedIngredients(
                        ingredientCounts, pawn, workbench, chosenThings,
                        materialSearchRadius);

                if (!ingredientsFound)
                    continue;

                Job haulOff = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, billGiver, null);
                if (haulOff != null) return haulOff;

                return MakeR4BillJob(R4DefOf.RRRR_Clean, workbench, bill, item, chosenThings);
            }

            return null;
        }

        private static Job MakeR4BillJob(
            JobDef jobDef, Thing workbench, Bill bill, Thing item, List<ThingCount> ingredients)
        {
            Job job = JobMaker.MakeJob(jobDef, workbench);
            job.count = 1;
            job.bill = bill;
            job.haulMode = HaulMode.ToCellNonStorage;
            job.placedThings = null;
            job.targetQueueA = new List<LocalTargetInfo> { item };
            job.targetQueueB = new List<LocalTargetInfo>();
            job.countQueue = new List<int>();

            if (ingredients != null)
            {
                for (int i = 0; i < ingredients.Count; i++)
                {
                    job.targetQueueB.Add(ingredients[i].Thing);
                    job.countQueue.Add(ingredients[i].Count);
                }
            }

            return job;
        }

        private static List<Thing> FindRepairCandidateItems(
            Pawn pawn, Thing workbench, Bill bill, bool forced, out bool bisActive)
        {
            return FindCandidateItems(
                pawn, workbench, bill, forced, out bisActive,
                delegate(Thing thing)
                {
                    return thing.def.useHitPoints && thing.HitPoints < thing.MaxHitPoints;
                });
        }

        private static List<Thing> FindCleanCandidateItems(
            Pawn pawn, Thing workbench, Bill bill, bool forced, out bool bisActive)
        {
            return FindCandidateItems(
                pawn, workbench, bill, forced, out bisActive,
                delegate(Thing thing)
                {
                    return thing is Apparel apparel && apparel.WornByCorpse;
                });
        }

        private static List<Thing> FindCandidateItems(
            Pawn pawn, Thing workbench, Bill bill, bool forced, out bool bisActive,
            System.Predicate<Thing> operationPredicate)
        {
            var candidates = new List<Thing>();
            bisActive = false;

            IntVec3 rootCell = (workbench is Building building && building.def.hasInteractionCell)
                ? building.InteractionCell
                : workbench.Position;
            Region rootReg = rootCell.GetRegion(pawn.Map);
            if (rootReg == null)
                return candidates;

            HashSet<int> bisIDs = BISCompat.GetStorageCandidateIDs(bill, pawn.Map);
            if (bisIDs != null)
            {
                bisActive = true;
                List<Thing> bisThings = BISCompat.GetStorageCandidateThings(bill, pawn.Map);
                if (bisThings != null)
                {
                    for (int i = 0; i < bisThings.Count; i++)
                    {
                        Thing thing = bisThings[i];
                        if (thing.IsForbidden(pawn) || !pawn.CanReserve(thing, 1, -1, null, forced))
                            continue;
                        if (!bill.IsFixedOrAllowedIngredient(thing))
                            continue;
                        if (!operationPredicate(thing))
                            continue;
                        if (!pawn.CanReach(thing, PathEndMode.ClosestTouch, pawn.NormalMaxDanger()))
                            continue;

                        candidates.Add(thing);
                    }
                }

                SortByDistanceToBench(candidates, workbench);
                R4Log.Debug($"BIS item filter: {candidates.Count} candidates from {bisThings?.Count ?? 0} storage items.");
                return candidates;
            }

            float searchRadius = bill.ingredientSearchRadius;
            float radiusSq = searchRadius * searchRadius;
            bool useRadius = searchRadius > 0f && searchRadius < 9999f;
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
                    if (!operationPredicate(thing))
                        continue;

                    candidates.Add(thing);
                }

                return false;
            };

            RegionTraverser.BreadthFirstTraverse(rootReg, entryCondition, regionProcessor, 99999);
            SortByDistanceToBench(candidates, workbench);
            return candidates;
        }

        private static void SortByDistanceToBench(List<Thing> candidates, Thing workbench)
        {
            IntVec3 benchPos = workbench.Position;
            candidates.Sort((a, b) =>
                (a.Position - benchPos).LengthHorizontalSquared
                    .CompareTo((b.Position - benchPos).LengthHorizontalSquared));
        }
    }
}
