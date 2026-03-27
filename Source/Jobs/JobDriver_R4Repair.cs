using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Repair job: haul item to workbench → work in cycles → each cycle restores
    /// 10% maxHP with a skill check, consuming materials proportional to the repair.
    /// Failures cause HP loss or quality degradation. If item reaches 0 HP, it is
    /// destroyed with partial material reclaim.
    /// 
    /// TargetA = item to repair, TargetB = workbench.
    /// 
    /// Designation persistence: the R4_Repair designation stays until full HP or
    /// destruction. Only the R4_Repair designation is removed — other designations
    /// (like R4_Clean) are preserved.
    /// </summary>
    public class JobDriver_R4Repair : JobDriver
    {
        private const TargetIndex ItemInd = TargetIndex.A;
        private const TargetIndex BenchInd = TargetIndex.B;

        private float cycleWorkLeft;
        private float cycleWorkTotal;
        private int cyclesCompleted;
        private int totalCyclesNeeded;

        private Thing Item => job.GetTarget(ItemInd).Thing;
        private Thing Bench => job.GetTarget(BenchInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(Item, job, 1, -1, null, errorOnFailed))
                return false;
            if (!pawn.Reserve(Bench, job, 1, -1, null, errorOnFailed))
                return false;
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref cycleWorkLeft, "cycleWorkLeft", 0f);
            Scribe_Values.Look(ref cycleWorkTotal, "cycleWorkTotal", 0f);
            Scribe_Values.Look(ref cyclesCompleted, "cyclesCompleted", 0);
            Scribe_Values.Look(ref totalCyclesNeeded, "totalCyclesNeeded", 0);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(ItemInd);
            this.FailOnDestroyedNullOrForbidden(BenchInd);
            this.FailOnThingMissingDesignation(ItemInd, R4DefOf.R4_Repair);

            yield return Toils_Goto.GotoThing(ItemInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(ItemInd);

            yield return Toils_Haul.StartCarryThing(ItemInd);

            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

            Toil dropToil = ToilMaker.MakeToil("R4_Repair_Drop");
            dropToil.defaultCompleteMode = ToilCompleteMode.Instant;
            dropToil.initAction = delegate
            {
                if (pawn.carryTracker.CarriedThing != null)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            };
            yield return dropToil;

            // Initialize
            Toil initToil = ToilMaker.MakeToil("R4_Repair_Init");
            initToil.defaultCompleteMode = ToilCompleteMode.Instant;
            initToil.initAction = delegate
            {
                Thing item = Item;
                if (item == null || !item.def.useHitPoints)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (item.HitPoints >= item.MaxHitPoints)
                {
                    RemoveRepairDesignation(item);
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                int hpToRepair = item.MaxHitPoints - item.HitPoints;
                int cycleHP = Mathf.Max(1, Mathf.RoundToInt(item.MaxHitPoints * 0.10f));
                totalCyclesNeeded = Mathf.CeilToInt((float)hpToRepair / cycleHP);
            };
            yield return initToil;

            // Work toil
            Toil workToil = ToilMaker.MakeToil("R4_Repair_Work");
            workToil.defaultCompleteMode = ToilCompleteMode.Never;
            workToil.handlingFacing = true;
            workToil.activeSkill = () => SkillDefOf.Crafting;
            workToil.FailOnCannotTouch(BenchInd, PathEndMode.InteractionCell);

            workToil.initAction = delegate
            {
                if (cycleWorkTotal <= 0f)
                    StartNewCycle();
            };

            workToil.tickAction = delegate
            {
                Thing item = Item;
                if (item == null || item.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                pawn.rotationTracker.FaceTarget(Bench);

                float pawnSpeed = pawn.GetStatValue(StatDefOf.GeneralLaborSpeed, true);
                float benchFactor = Bench.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor, true);
                cycleWorkLeft -= pawnSpeed * benchFactor;

                pawn.skills?.Learn(SkillDefOf.Crafting, 0.12f);

                if (cycleWorkLeft <= 0f)
                {
                    // Consume materials for this cycle
                    var cycleCost = MaterialUtility.GetRepairCycleCost(item);
                    if (cycleCost.Count > 0)
                    {
                        if (!MaterialUtility.HasRepairMaterials(cycleCost, pawn.Map, pawn.Position))
                        {
                            Messages.Message(
                                "R4_RepairNoMaterials".Translate(item.LabelCap),
                                item, MessageTypeDefOf.RejectInput);
                            // End job but keep designation — materials may appear later
                            ReadyForNextToil();
                            return;
                        }
                        MaterialUtility.ConsumeRepairMaterials(cycleCost, pawn.Map, pawn.Position);
                    }

                    CompleteCycle(item);

                    if (item.Destroyed || item.HitPoints <= 0)
                    {
                        HandleItemDestroyed(item);
                        return;
                    }

                    if (item.HitPoints >= item.MaxHitPoints)
                    {
                        Log.Message($"[R4] Repair complete: {item.LabelCap} after {cyclesCompleted} cycles.");
                        RemoveRepairDesignation(item);
                        ReadyForNextToil();
                        return;
                    }

                    if (cyclesCompleted >= totalCyclesNeeded)
                    {
                        Log.Message($"[R4] Repair pass done: {item.LabelCap} at {item.HitPoints}/{item.MaxHitPoints} HP. Designation remains.");
                        ReadyForNextToil();
                        return;
                    }

                    StartNewCycle();
                }
            };

            workToil.WithProgressBar(ItemInd, delegate
            {
                if (totalCyclesNeeded <= 0) return 0f;
                float cycleProgress = (cycleWorkTotal > 0f) ? (1f - cycleWorkLeft / cycleWorkTotal) : 0f;
                return (cyclesCompleted + cycleProgress) / totalCyclesNeeded;
            });

            yield return workToil;

            Toil finishToil = ToilMaker.MakeToil("R4_Repair_Finish");
            finishToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finishToil;
        }

        /// <summary>
        /// Remove only the R4_Repair designation, preserving other designations
        /// (like R4_Clean) on the same item.
        /// </summary>
        private void RemoveRepairDesignation(Thing item)
        {
            var des = pawn.Map.designationManager.DesignationOn(item, R4DefOf.R4_Repair);
            if (des != null)
                pawn.Map.designationManager.RemoveDesignation(des);
        }

        private void StartNewCycle()
        {
            Thing item = Item;
            if (item == null) return;

            float baseWork = item.def.GetStatValueAbstract(StatDefOf.WorkToMake, item.Stuff);
            if (baseWork <= 0f) baseWork = 1000f;

            cycleWorkTotal = Mathf.Clamp(baseWork * 0.05f, 200f, 800f);
            cycleWorkLeft = cycleWorkTotal;
        }

        private void CompleteCycle(Thing item)
        {
            cyclesCompleted++;
            int skillLevel = pawn?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
            float techDifficulty = SkillUtility.GetTechDifficulty(item.def);
            float successChance = SkillUtility.RepairSuccessChance(skillLevel, techDifficulty);

            if (Rand.Chance(successChance))
            {
                int cycleHP = Mathf.Max(1, Mathf.RoundToInt(item.MaxHitPoints * 0.10f));
                item.HitPoints = Mathf.Min(item.MaxHitPoints, item.HitPoints + cycleHP);
            }
            else
            {
                if (SkillUtility.IsCriticalFailure(item))
                {
                    SkillUtility.ApplyCriticalFailure(item);
                    Messages.Message(
                        "R4_RepairCriticalFailure".Translate(pawn.LabelShort, item.LabelCap),
                        item, MessageTypeDefOf.NegativeEvent);
                }
                else
                {
                    SkillUtility.ApplyMinorFailure(item);
                    Messages.Message(
                        "R4_RepairMinorFailure".Translate(pawn.LabelShort, item.LabelCap),
                        item, MessageTypeDefOf.NeutralEvent);
                }
            }
        }

        private void HandleItemDestroyed(Thing item)
        {
            string itemLabel = item.LabelCap;
            Log.Message($"[R4] Repair failure destroyed: {itemLabel}");

            float progressRatio = (float)cyclesCompleted / Mathf.Max(totalCyclesNeeded, 1);

            if (!item.Destroyed)
            {
                int skillLevel = pawn?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
                float returnPct = MaterialUtility.CalculateReturnPercent(item, skillLevel) * 0.5f * progressRatio;

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

            Messages.Message(
                "R4_RepairItemDestroyed".Translate(itemLabel),
                new TargetInfo(pawn.Position, pawn.Map),
                MessageTypeDefOf.NegativeEvent);

            EndJobWith(JobCondition.Succeeded);
        }
    }
}
