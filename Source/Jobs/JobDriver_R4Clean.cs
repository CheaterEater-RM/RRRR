using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Clean job using vanilla ingredient-gathering pattern.
    /// 
    /// Target layout (matches vanilla DoBill):
    ///   TargetA = workbench
    ///   TargetQueueA[0] = tainted apparel
    ///   TargetQueueB = ingredient stacks
    ///   TargetC = ingredient placement cell
    /// 
    /// Flow: gather ingredients → goto bench → haul apparel → work → consume → remove taint.
    /// </summary>
    public class JobDriver_R4Clean : JobDriver
    {
        private const TargetIndex BenchInd = TargetIndex.A;
        private const TargetIndex IngredientInd = TargetIndex.B;
        private const TargetIndex CellInd = TargetIndex.C;

        private float workLeft;
        private float totalWork;

        private Thing Bench => job.GetTarget(BenchInd).Thing;

        private Thing _cachedItem;
        private Thing CleanItem
        {
            get
            {
                if (_cachedItem == null || _cachedItem.Destroyed)
                {
                    var queue = job.GetTargetQueue(BenchInd); // targetQueueA
                    if (queue != null && queue.Count > 0)
                        _cachedItem = queue[0].Thing;
                }
                return _cachedItem;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.GetTarget(BenchInd), job, 1, -1, null, errorOnFailed))
                return false;

            var itemQueue = job.GetTargetQueue(BenchInd);
            if (itemQueue != null && itemQueue.Count > 0)
            {
                Thing item = itemQueue[0].Thing;
                if (item != null && !pawn.Reserve(item, job, 1, -1, null, errorOnFailed))
                    return false;
            }

            pawn.ReserveAsManyAsPossible(job.GetTargetQueue(IngredientInd), job);
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workLeft, "workLeft", 0f);
            Scribe_Values.Look(ref totalWork, "totalWork", 0f);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(BenchInd);
            this.FailOn(delegate
            {
                Thing item = CleanItem;
                if (item == null || item.Destroyed)
                    return true;
                if (item.Map != null && item.Map.designationManager.DesignationOn(item, R4DefOf.R4_Clean) == null)
                    return true;
                return false;
            });

            // ── Phase 1: Gather ingredients ──
            Toil gotoBillGiver = Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

            yield return Toils_Jump.JumpIf(gotoBillGiver,
                () => job.GetTargetQueue(IngredientInd).NullOrEmpty());

            foreach (Toil t in JobDriver_DoBill.CollectIngredientsToils(
                IngredientInd, BenchInd, CellInd,
                subtractNumTakenFromJobCount: false,
                failIfStackCountLessThanJobCount: false))
            {
                yield return t;
            }

            // ── Phase 2: Go to bench, then haul apparel ──
            yield return gotoBillGiver;

            Toil gotoItem = ToilMaker.MakeToil("R4_Clean_GotoItem");
            gotoItem.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            gotoItem.initAction = delegate
            {
                Thing item = CleanItem;
                if (item == null || item.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                pawn.pather.StartPath(item, PathEndMode.ClosestTouch);
            };
            yield return gotoItem;

            Toil carryItem = ToilMaker.MakeToil("R4_Clean_CarryItem");
            carryItem.defaultCompleteMode = ToilCompleteMode.Instant;
            carryItem.initAction = delegate
            {
                Thing item = CleanItem;
                if (item == null || item.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                if (pawn.carryTracker.CarriedThing == null)
                {
                    int count = Mathf.Min(item.stackCount, pawn.carryTracker.AvailableStackSpace(item.def));
                    if (count <= 0 || pawn.carryTracker.TryStartCarry(item, count) <= 0)
                    {
                        EndJobWith(JobCondition.Incompletable);
                    }
                }
            };
            yield return carryItem;

            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

            Toil dropItem = ToilMaker.MakeToil("R4_Clean_DropItem");
            dropItem.defaultCompleteMode = ToilCompleteMode.Instant;
            dropItem.initAction = delegate
            {
                if (pawn.carryTracker.CarriedThing != null)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            };
            yield return dropItem;

            // ── Phase 3: Work ──
            Toil workToil = ToilMaker.MakeToil("R4_Clean_Work");
            workToil.defaultCompleteMode = ToilCompleteMode.Never;
            workToil.handlingFacing = true;
            workToil.activeSkill = () => SkillDefOf.Crafting;
            workToil.FailOnCannotTouch(BenchInd, PathEndMode.InteractionCell);

            workToil.initAction = delegate
            {
                Thing item = CleanItem;
                if (item == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                float workToMake = item.def.GetStatValueAbstract(StatDefOf.WorkToMake, item.Stuff);
                if (workToMake <= 0f) workToMake = 1000f;
                totalWork = Mathf.Clamp(workToMake * 0.15f, 300f, 1500f);
                if (workLeft <= 0f) workLeft = totalWork;
            };

            workToil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Bench);
                float speed = pawn.GetStatValue(StatDefOf.GeneralLaborSpeed, true);
                float benchFactor = Bench.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor, true);
                int skillLevel = pawn?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
                float skillBonus = 1f + (skillLevel * 0.03f);
                workLeft -= speed * benchFactor * skillBonus;
                pawn.skills?.Learn(SkillDefOf.Crafting, 0.08f);
                if (workLeft <= 0f)
                    ReadyForNextToil();
            };

            workToil.WithProgressBar(BenchInd, delegate
            {
                if (totalWork <= 0f) return 0f;
                return 1f - (workLeft / totalWork);
            });

            yield return workToil;

            // ── Phase 4: Consume ingredients, remove taint ──
            Toil finishToil = ToilMaker.MakeToil("R4_Clean_Finish");
            finishToil.defaultCompleteMode = ToilCompleteMode.Instant;
            finishToil.initAction = delegate
            {
                Thing item = CleanItem;
                if (item == null || item.Destroyed)
                    return;

                MaterialUtility.ConsumeIngredientsOnBench(Bench, pawn.Map);

                if (item is Apparel apparel)
                {
                    apparel.WornByCorpse = false;
                    apparel.Notify_ColorChanged();
                    Log.Message($"[R4] Cleaned taint from: {apparel.LabelCap}");
                }

                var des = pawn.Map.designationManager.DesignationOn(item, R4DefOf.R4_Clean);
                if (des != null)
                    pawn.Map.designationManager.RemoveDesignation(des);
            };

            yield return finishToil;
        }
    }
}
