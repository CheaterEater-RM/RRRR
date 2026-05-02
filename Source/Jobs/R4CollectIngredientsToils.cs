using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Vanilla-aligned ingredient collection with a stricter placement-cell rule:
    /// R4 jobs may only stage into empty cells or onto stacks they already track
    /// in job.placedThings. This prevents concurrent jobs from merging into each
    /// other's staged stacks and later invalidating the tracked references.
    /// </summary>
    public static class R4CollectIngredientsToils
    {
        public static IEnumerable<Toil> CollectIngredientsToils(
            TargetIndex ingredientInd,
            TargetIndex billGiverInd,
            TargetIndex ingredientPlaceCellInd,
            bool subtractNumTakenFromJobCount = false,
            bool failIfStackCountLessThanJobCount = true,
            bool placeInBillGiver = false)
        {
            Toil extract = Toils_JobTransforms.ExtractNextTargetFromQueue(ingredientInd);
            yield return extract;

            Toil jumpIfHaveTargetInQueue = Toils_Jump.JumpIfHaveTargetInQueue(ingredientInd, extract);
            yield return JumpIfTargetInsideBillGiver(jumpIfHaveTargetInQueue, ingredientInd, billGiverInd);

            Toil getToHaulTarget = Toils_Goto.GotoThing(ingredientInd, PathEndMode.ClosestTouch, canGotoSpawnedParent: true)
                .FailOnForbidden(ingredientInd)
                .FailOnSomeonePhysicallyInteracting(ingredientInd);
            yield return getToHaulTarget;

            yield return Toils_Haul.StartCarryThing(
                ingredientInd,
                putRemainderInQueue: true,
                subtractNumTakenFromJobCount,
                failIfStackCountLessThanJobCount,
                reserve: false,
                canTakeFromInventory: true);

            yield return JobDriver_DoBill.JumpToCollectNextIntoHandsForBill(getToHaulTarget, ingredientInd);
            yield return Toils_Goto.GotoThing(billGiverInd, PathEndMode.InteractionCell).FailOnDestroyedOrNull(ingredientInd);

            if (!placeInBillGiver)
            {
                Toil findPlaceTarget = SetTargetToOwnedIngredientPlaceCell(billGiverInd, ingredientInd, ingredientPlaceCellInd);
                yield return findPlaceTarget;
                yield return Toils_Haul.PlaceHauledThingInCell(ingredientPlaceCellInd, findPlaceTarget, storageMode: false);

                Toil physReserveToil = ToilMaker.MakeToil("R4_CollectIngredientsReserve");
                physReserveToil.initAction = delegate
                {
                    Pawn actor = physReserveToil.actor;
                    actor.Map.physicalInteractionReservationManager.Reserve(actor, actor.CurJob, actor.CurJob.GetTarget(ingredientInd));
                };
                yield return physReserveToil;
            }
            else
            {
                yield return Toils_Haul.DepositHauledThingInContainer(billGiverInd, ingredientInd);
            }

            yield return jumpIfHaveTargetInQueue;
        }

        private static Toil JumpIfTargetInsideBillGiver(Toil jumpToil, TargetIndex ingredientInd, TargetIndex billGiverInd)
        {
            Toil toil = ToilMaker.MakeToil("R4_JumpIfTargetInsideBillGiver");
            toil.initAction = delegate
            {
                Thing billGiver = toil.actor.CurJob.GetTarget(billGiverInd).Thing;
                if (billGiver == null || !billGiver.Spawned)
                    return;

                Thing ingredient = toil.actor.jobs.curJob.GetTarget(ingredientInd).Thing;
                if (ingredient == null)
                    return;

                ThingOwner thingOwner = billGiver.TryGetInnerInteractableThingOwner();
                if (thingOwner == null || !thingOwner.Contains(ingredient))
                    return;

                HaulAIUtility.UpdateJobWithPlacedThings(toil.actor.jobs.curJob, ingredient, ingredient.stackCount);
                toil.actor.jobs.curDriver.JumpToToil(jumpToil);
            };
            return toil;
        }

        private static Toil SetTargetToOwnedIngredientPlaceCell(TargetIndex billGiverInd, TargetIndex carriedThingInd, TargetIndex cellInd)
        {
            Toil toil = ToilMaker.MakeToil("R4_SetTargetToOwnedIngredientPlaceCell");
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Job curJob = actor.jobs.curJob;
                Thing carriedThing = curJob.GetTarget(carriedThingInd).Thing;
                Thing billGiver = curJob.GetTarget(billGiverInd).Thing;

                if (carriedThing == null || billGiver == null)
                {
                    R4Log.Warn(
                        $"SetTargetToOwnedIngredientPlaceCell: missing target for {curJob.def.defName}#{curJob.loadID}. " +
                        $"carried={MaterialUtility.DescribeThingReference(carriedThing)} billGiver={billGiver?.def?.defName ?? "null"}.");
                    actor.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
                    return;
                }

                IntVec3 chosenCell = IntVec3.Invalid;
                string rejectionReason = null;
                Thing workItem = GetWorkItem(curJob, billGiverInd);

                foreach (IntVec3 cell in R4WorkbenchPlacement.IngredientPlaceCellsInOrder(curJob, billGiver, carriedThing, workItem))
                {
                    if (!GenSpawn.CanSpawnAt(carriedThing.def, cell, actor.Map))
                        continue;

                    if (R4WorkbenchPlacement.CellAcceptsTrackedIngredientPlacement(curJob, carriedThing, actor.Map.thingGrid.ThingsListAt(cell), out rejectionReason))
                    {
                        chosenCell = cell;
                        break;
                    }

                    R4Log.Debug(
                        $"Ingredient place cell rejected for {curJob.def.defName}#{curJob.loadID}: pawn={actor.LabelShort} " +
                        $"carried={MaterialUtility.DescribeThingReference(carriedThing)} cell={cell} reason={rejectionReason} " +
                        $"tracked={MaterialUtility.DescribePlacedThings(curJob)}");
                }

                if (!chosenCell.IsValid)
                {
                    R4Log.Warn(
                        $"SetTargetToOwnedIngredientPlaceCell: no owned/empty ingredient cell found for {curJob.def.defName}#{curJob.loadID}. " +
                        $"pawn={actor.LabelShort} carried={MaterialUtility.DescribeThingReference(carriedThing)} lastReject={rejectionReason ?? "none"} " +
                        $"tracked={MaterialUtility.DescribePlacedThings(curJob)}");
                    actor.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
                    return;
                }

                curJob.SetTarget(cellInd, chosenCell);
                R4Log.Debug(
                    $"Ingredient place cell selected for {curJob.def.defName}#{curJob.loadID}: pawn={actor.LabelShort} " +
                    $"carried={MaterialUtility.DescribeThingReference(carriedThing)} cell={chosenCell} tracked={MaterialUtility.DescribePlacedThings(curJob)}");
            };
            return toil;
        }

        private static Thing GetWorkItem(Job job, TargetIndex billGiverInd)
        {
            List<LocalTargetInfo> queue = job?.GetTargetQueue(billGiverInd);
            if (queue == null || queue.Count == 0)
                return null;

            return queue[0].Thing;
        }
    }
}