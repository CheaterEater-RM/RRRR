using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Repair job using vanilla ingredient-gathering pattern.
    /// 
    /// Target layout (matches vanilla DoBill):
    ///   TargetA = workbench (stable, never overwritten by queue extraction)
    ///   TargetQueueA[0] = item to repair
    ///   TargetQueueB = ingredient stacks (extracted into TargetB during hauling)
    ///   TargetC = ingredient placement cell
    /// 
    /// Flow: gather ingredients via vanilla CollectIngredientsToils →
    ///       goto bench → haul item → work one cycle → consume → skill check.
    /// If not fully repaired, designation stays and WorkGiver queues another cycle.
    /// </summary>
    public class JobDriver_R4Repair : JobDriver
    {
        private const TargetIndex BenchInd = TargetIndex.A;
        private const TargetIndex IngredientInd = TargetIndex.B;
        private const TargetIndex CellInd = TargetIndex.C;

        private float cycleWorkLeft;
        private float cycleWorkTotal;

        private Thing Bench => job.GetTarget(BenchInd).Thing;

        private Thing _cachedItem;
        private Thing RepairItem
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
            Scribe_Values.Look(ref cycleWorkLeft, "cycleWorkLeft", 0f);
            Scribe_Values.Look(ref cycleWorkTotal, "cycleWorkTotal", 0f);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(BenchInd);
            // Fail if item is gone or designation removed (player cancelled)
            this.FailOn(delegate
            {
                Thing item = RepairItem;
                if (item == null || item.Destroyed)
                    return true;
                if (item.Map != null && item.Map.designationManager.DesignationOn(item, R4DefOf.R4_Repair) == null)
                    return true;
                return false;
            });

            // ── Phase 1: Gather ingredients using vanilla pattern ──
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

            // ── Phase 2: Go to bench, then haul item ──
            yield return gotoBillGiver;

            Toil gotoItem = ToilMaker.MakeToil("R4_Repair_GotoItem");
            gotoItem.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            gotoItem.initAction = delegate
            {
                Thing item = RepairItem;
                if (item == null || item.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                pawn.pather.StartPath(item, PathEndMode.ClosestTouch);
            };
            yield return gotoItem;

            Toil carryItem = ToilMaker.MakeToil("R4_Repair_CarryItem");
            carryItem.defaultCompleteMode = ToilCompleteMode.Instant;
            carryItem.initAction = delegate
            {
                Thing item = RepairItem;
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

            Toil dropItem = ToilMaker.MakeToil("R4_Repair_DropItem");
            dropItem.defaultCompleteMode = ToilCompleteMode.Instant;
            dropItem.initAction = delegate
            {
                if (pawn.carryTracker.CarriedThing != null)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            };
            yield return dropItem;

            // ── Phase 3: Work one repair cycle ──
            Toil workToil = ToilMaker.MakeToil("R4_Repair_Work");
            workToil.defaultCompleteMode = ToilCompleteMode.Never;
            workToil.handlingFacing = true;
            workToil.activeSkill = () => SkillDefOf.Crafting;
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
                if (cycleWorkLeft <= 0f)
                    cycleWorkLeft = cycleWorkTotal;
            };

            workToil.tickAction = delegate
            {
                Thing item = RepairItem;
                if (item == null || item.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                pawn.rotationTracker.FaceTarget(Bench);
                float speed = pawn.GetStatValue(StatDefOf.GeneralLaborSpeed, true);
                float benchFactor = Bench.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor, true);
                cycleWorkLeft -= speed * benchFactor;
                pawn.skills?.Learn(SkillDefOf.Crafting, 0.12f);

                if (cycleWorkLeft <= 0f)
                    ReadyForNextToil();
            };

            workToil.WithProgressBar(BenchInd, delegate
            {
                if (cycleWorkTotal <= 0f) return 0f;
                return 1f - (cycleWorkLeft / cycleWorkTotal);
            });

            yield return workToil;

            // ── Phase 4: Apply cycle result, consume ingredients ──
            Toil finishToil = ToilMaker.MakeToil("R4_Repair_Finish");
            finishToil.defaultCompleteMode = ToilCompleteMode.Instant;
            finishToil.initAction = delegate
            {
                Thing item = RepairItem;
                if (item == null || item.Destroyed)
                    return;

                MaterialUtility.ConsumeIngredientsOnBench(Bench, pawn.Map);

                int skillLevel = pawn?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
                float techDifficulty = SkillUtility.GetTechDifficulty(item.def);
                float successChance = SkillUtility.RepairSuccessChance(skillLevel, techDifficulty);

                if (Rand.Chance(successChance))
                {
                    int cycleHP = Mathf.Max(1, Mathf.RoundToInt(item.MaxHitPoints * 0.20f));
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
                {
                    Log.Message($"[R4] Repair complete: {item.LabelCap}");
                    RemoveRepairDesignation(item);
                }
            };

            yield return finishToil;
        }

        private void RemoveRepairDesignation(Thing item)
        {
            if (item == null || item.Map == null) return;
            var des = pawn.Map.designationManager.DesignationOn(item, R4DefOf.R4_Repair);
            if (des != null)
                pawn.Map.designationManager.RemoveDesignation(des);
        }

        private void HandleItemDestroyed(Thing item)
        {
            string itemLabel = item.LabelCap;
            Log.Message($"[R4] Repair failure destroyed: {itemLabel}");

            if (!item.Destroyed)
            {
                int skillLevel = pawn?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
                float returnPct = MaterialUtility.CalculateReturnPercent(item, skillLevel) * 0.25f;

                var costList = item.def.CostListAdjusted(item.Stuff, errorOnNullStuff: false);
                if (costList != null)
                {
                    for (int i = 0; i < costList.Count; i++)
                    {
                        var entry = costList[i];
                        if (entry.thingDef == null || entry.count <= 0 || entry.thingDef.intricate)
                            continue;
                        int count = GenMath.RoundRandom(entry.count * returnPct);
                        if (count > 0)
                        {
                            Thing product = ThingMaker.MakeThing(entry.thingDef);
                            product.stackCount = count;
                            GenPlace.TryPlaceThing(product, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                        }
                    }
                }

                pawn.Map.designationManager.RemoveAllDesignationsOn(item);
                item.Destroy(DestroyMode.Vanish);
            }

            Messages.Message("R4_RepairItemDestroyed".Translate(itemLabel),
                new TargetInfo(pawn.Position, pawn.Map), MessageTypeDefOf.NegativeEvent);
        }
    }
}
