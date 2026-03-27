using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Repair job: haul item to workbench → work in cycles → each cycle restores
    /// 10% maxHP with a skill check. Failures cause HP loss or quality degradation.
    /// If the item reaches 0 HP, it is destroyed and partial materials are reclaimed.
    /// 
    /// TargetA = item to repair, TargetB = workbench.
    /// 
    /// Designation persistence: the R4_Repair designation stays on the item until
    /// it reaches full HP or is destroyed. If the pawn gets interrupted or all planned
    /// cycles finish but the item still has damage (from failures), the designation
    /// remains so another pawn (or the same pawn) will continue the repair.
    /// </summary>
    public class JobDriver_R4Repair : JobDriver
    {
        private const TargetIndex ItemInd = TargetIndex.A;
        private const TargetIndex BenchInd = TargetIndex.B;

        // Work tracking for the current cycle
        private float cycleWorkLeft;
        private float cycleWorkTotal;

        // Overall repair tracking
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

            // 1. Go to item
            yield return Toils_Goto.GotoThing(ItemInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(ItemInd);

            // 2. Pick up item
            yield return Toils_Haul.StartCarryThing(ItemInd);

            // 3. Carry to bench
            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

            // 4. Drop item near bench
            Toil dropToil = ToilMaker.MakeToil("R4_Repair_Drop");
            dropToil.defaultCompleteMode = ToilCompleteMode.Instant;
            dropToil.initAction = delegate
            {
                if (pawn.carryTracker.CarriedThing != null)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            };
            yield return dropToil;

            // 5. Initialize repair tracking
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
                    // Already at full HP — remove designation and finish
                    pawn.Map.designationManager.RemoveAllDesignationsOn(item);
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                int hpToRepair = item.MaxHitPoints - item.HitPoints;
                int cycleHP = Mathf.Max(1, Mathf.RoundToInt(item.MaxHitPoints * 0.10f));
                totalCyclesNeeded = Mathf.CeilToInt((float)hpToRepair / cycleHP);
            };
            yield return initToil;

            // 6. Repair work toil — cycles through work, applying skill checks per cycle
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
                    CompleteCycle(item);

                    // Check if item was destroyed by failure
                    if (item.Destroyed || item.HitPoints <= 0)
                    {
                        HandleItemDestroyed(item);
                        return;
                    }

                    // Check if fully repaired — only NOW remove designation
                    if (item.HitPoints >= item.MaxHitPoints)
                    {
                        Log.Message($"[R4] Repair complete: {item.LabelCap} fully repaired after {cyclesCompleted} cycles.");
                        pawn.Map.designationManager.RemoveAllDesignationsOn(item);
                        ReadyForNextToil();
                        return;
                    }

                    // All planned cycles done but item still damaged (failures reduced HP).
                    // End this job attempt but KEEP the designation — another pawn
                    // (or this pawn) will pick it up again via WorkGiver.
                    if (cyclesCompleted >= totalCyclesNeeded)
                    {
                        Log.Message($"[R4] Repair pass done: {item.LabelCap} at {item.HitPoints}/{item.MaxHitPoints} HP after {cyclesCompleted} cycles. Designation remains for continued repair.");
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

            // 7. Finish toil
            Toil finishToil = ToilMaker.MakeToil("R4_Repair_Finish");
            finishToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finishToil;
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

            // Reclaim partial materials scaled by cycle progress
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
