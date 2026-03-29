using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RRRR
{
    public class JobDriver_R4Repair : JobDriver
    {
        private const TargetIndex BenchInd      = TargetIndex.A;
        private const TargetIndex IngredientInd = TargetIndex.B;
        private const TargetIndex CellInd       = TargetIndex.C;

        private float cycleWorkLeft;
        private float cycleWorkTotal;

        private Thing Bench        => job.GetTarget(BenchInd).Thing;
        private bool  IsBillDriven => job.bill != null;

        private Thing _cachedItem;
        private Thing RepairItem
        {
            get
            {
                if (_cachedItem == null || _cachedItem.Destroyed)
                {
                    var queue = job.GetTargetQueue(BenchInd);
                    if (queue != null && queue.Count > 0)
                        _cachedItem = queue[0].Thing;
                }
                return _cachedItem;
            }
        }

        public override string GetReport()
        {
            Thing item  = RepairItem;
            Thing bench = Bench;
            string itemLabel  = item  != null ? item.LabelShort  : "unknown".Translate().ToString();
            string benchLabel = bench != null ? bench.LabelShort : "unknown".Translate().ToString();
            return "R4_JobReport_Repair".Translate(itemLabel, benchLabel);
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
            Scribe_Values.Look(ref cycleWorkLeft,  "cycleWorkLeft",  0f);
            Scribe_Values.Look(ref cycleWorkTotal, "cycleWorkTotal", 0f);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(BenchInd);

            this.FailOn(delegate
            {
                Thing item = RepairItem;
                if (item == null || item.Destroyed) return true;
                if (IsBillDriven) return job.bill.DeletedOrDereferenced || job.bill.suspended;
                if (item.Map != null && item.Map.designationManager.DesignationOn(item, R4DefOf.R4_Repair) == null)
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

            // ── Phase 2: Go to bench, haul item ──
            yield return gotoBillGiver;

            Toil gotoItem = ToilMaker.MakeToil("R4_Repair_GotoItem");
            gotoItem.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            gotoItem.initAction = delegate
            {
                Thing item = RepairItem;
                if (item == null || item.Destroyed) { EndJobWith(JobCondition.Incompletable); return; }
                pawn.pather.StartPath(item, PathEndMode.ClosestTouch);
            };
            yield return gotoItem;

            Toil carryItem = ToilMaker.MakeToil("R4_Repair_CarryItem");
            carryItem.defaultCompleteMode = ToilCompleteMode.Instant;
            carryItem.initAction = delegate
            {
                Thing item = RepairItem;
                if (item == null || item.Destroyed) { EndJobWith(JobCondition.Incompletable); return; }
                if (pawn.carryTracker.CarriedThing == null)
                {
                    int count = Mathf.Min(item.stackCount, pawn.carryTracker.AvailableStackSpace(item.def));
                    if (count <= 0 || pawn.carryTracker.TryStartCarry(item, count) <= 0)
                        EndJobWith(JobCondition.Incompletable);
                }
            };
            yield return carryItem;

            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

            Toil dropItem = ToilMaker.MakeToil("R4_Repair_DropItem");
            dropItem.defaultCompleteMode = ToilCompleteMode.Instant;
            dropItem.initAction = delegate
            {
                if (pawn.carryTracker.CarriedThing != null)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            };
            yield return dropItem;

            // ── Phase 3: Work one cycle ──
            Toil workToil = ToilMaker.MakeToil("R4_Repair_Work");
            workToil.defaultCompleteMode = ToilCompleteMode.Never;
            workToil.handlingFacing      = true;
            workToil.activeSkill         = () => SkillDefOf.Crafting;
            workToil.FailOnCannotTouch(BenchInd, PathEndMode.InteractionCell);

            workToil.initAction = delegate
            {
                Thing item = RepairItem;
                if (item == null || !item.def.useHitPoints || item.HitPoints >= item.MaxHitPoints)
                {
                    RemoveRepairDesignation(item);
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }
                float baseWork = item.def.GetStatValueAbstract(StatDefOf.WorkToMake, item.Stuff);
                if (baseWork <= 0f) baseWork = 1000f;
                cycleWorkTotal = Mathf.Clamp(baseWork * 0.05f, 200f, 800f);
                if (cycleWorkLeft <= 0f) cycleWorkLeft = cycleWorkTotal;
            };

            workToil.tickAction = delegate
            {
                Thing item = RepairItem;
                if (item == null || item.Destroyed) { EndJobWith(JobCondition.Incompletable); return; }
                pawn.rotationTracker.FaceTarget(Bench);
                float speed       = pawn.GetStatValue(StatDefOf.GeneralLaborSpeed,        true);
                float benchFactor = Bench.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor, true);
                cycleWorkLeft -= speed * benchFactor;
                pawn.skills?.Learn(SkillDefOf.Crafting, 0.12f);
                if (cycleWorkLeft <= 0f) ReadyForNextToil();
            };

            workToil.WithProgressBar(BenchInd,
                () => cycleWorkTotal <= 0f ? 0f : 1f - (cycleWorkLeft / cycleWorkTotal));

            yield return workToil;

            // ── Phase 4: Apply result, consume ingredients ──
            Toil finishToil = ToilMaker.MakeToil("R4_Repair_Finish");
            finishToil.defaultCompleteMode = ToilCompleteMode.Instant;
            finishToil.initAction = delegate
            {
                Thing item = RepairItem;
                if (item == null || item.Destroyed)
                    return;

                // Consume only the expected cycle materials, not everything on bench cells
                var cycleCost = MaterialUtility.GetRepairCycleCost(item);
                MaterialUtility.ConsumeIngredientsOnBench(Bench, pawn.Map, cycleCost);

                int skillLevel       = pawn?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
                float techDifficulty = SkillUtility.GetTechDifficulty(item.def);
                float successChance  = SkillUtility.RepairSuccessChance(skillLevel, techDifficulty);

                if (Rand.Chance(successChance))
                {
                    float hpFraction = RRRR_Mod.Settings.repairHpPerCycle;
                    int cycleHP = Mathf.Max(1, Mathf.RoundToInt(item.MaxHitPoints * hpFraction));
                    item.HitPoints = Mathf.Min(item.MaxHitPoints, item.HitPoints + cycleHP);
                }
                else
                {
                    if (SkillUtility.IsCriticalFailure(item))
                    {
                        SkillUtility.ApplyCriticalFailure(item);
                        Messages.Message("R4_RepairCriticalFailure".Translate(pawn.LabelShort, item.LabelCap),
                            item, MessageTypeDefOf.NegativeEvent);
                    }
                    else
                    {
                        SkillUtility.ApplyMinorFailure(item);
                        Messages.Message("R4_RepairMinorFailure".Translate(pawn.LabelShort, item.LabelCap),
                            item, MessageTypeDefOf.NeutralEvent);
                    }
                }

                if (item.Destroyed || item.HitPoints <= 0)
                {
                    HandleItemDestroyed(item);
                    return;
                }

                if (item.HitPoints >= item.MaxHitPoints)
                    RemoveRepairDesignation(item);

                if (IsBillDriven)
                    job.bill.Notify_IterationCompleted(pawn, new List<Thing> { item });
            };

            yield return finishToil;
        }

        private void RemoveRepairDesignation(Thing item)
        {
            if (item == null || item.Map == null) return;
            var des = pawn.Map.designationManager.DesignationOn(item, R4DefOf.R4_Repair);
            if (des != null) pawn.Map.designationManager.RemoveDesignation(des);
        }

        private void HandleItemDestroyed(Thing item)
        {
            string itemLabel = item.LabelCap;
            if (!item.Destroyed)
            {
                MaterialUtility.SpawnPartialReclaim(item, pawn, 0.25f, pawn.Position, pawn.Map);
                pawn.Map.designationManager.RemoveAllDesignationsOn(item);
                item.Destroy(DestroyMode.Vanish);
            }
            Messages.Message("R4_RepairItemDestroyed".Translate(itemLabel),
                new TargetInfo(pawn.Position, pawn.Map), MessageTypeDefOf.NegativeEvent);
        }
    }
}
