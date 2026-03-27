using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Clean job: haul tainted apparel to workbench → work → consume materials → remove taint.
    /// Always succeeds. Skill reduces work time. No failure mechanics.
    /// Material cost: ~20% of base materials (non-intricate only).
    /// 
    /// TargetA = apparel to clean, TargetB = workbench.
    /// </summary>
    public class JobDriver_R4Clean : JobDriver
    {
        private const TargetIndex ItemInd = TargetIndex.A;
        private const TargetIndex BenchInd = TargetIndex.B;

        private float workLeft;
        private float totalWork;

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
            Scribe_Values.Look(ref workLeft, "workLeft", 0f);
            Scribe_Values.Look(ref totalWork, "totalWork", 0f);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(ItemInd);
            this.FailOnDestroyedNullOrForbidden(BenchInd);
            this.FailOnThingMissingDesignation(ItemInd, R4DefOf.R4_Clean);

            yield return Toils_Goto.GotoThing(ItemInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(ItemInd);

            yield return Toils_Haul.StartCarryThing(ItemInd);

            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

            Toil dropToil = ToilMaker.MakeToil("R4_Clean_Drop");
            dropToil.defaultCompleteMode = ToilCompleteMode.Instant;
            dropToil.initAction = delegate
            {
                if (pawn.carryTracker.CarriedThing != null)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            };
            yield return dropToil;

            // Check and consume materials upfront before starting work
            Toil materialCheckToil = ToilMaker.MakeToil("R4_Clean_MaterialCheck");
            materialCheckToil.defaultCompleteMode = ToilCompleteMode.Instant;
            materialCheckToil.initAction = delegate
            {
                Thing item = Item;
                if (item == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                var cleanCost = MaterialUtility.GetCleanCost(item);
                if (cleanCost.Count > 0)
                {
                    if (!MaterialUtility.HasRepairMaterials(cleanCost, pawn.Map, pawn.Position))
                    {
                        Messages.Message(
                            "R4_CleanNoMaterials".Translate(item.LabelCap),
                            item, MessageTypeDefOf.RejectInput);
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    MaterialUtility.ConsumeRepairMaterials(cleanCost, pawn.Map, pawn.Position);
                }
            };
            yield return materialCheckToil;

            // Work toil
            Toil workToil = ToilMaker.MakeToil("R4_Clean_Work");
            workToil.defaultCompleteMode = ToilCompleteMode.Never;
            workToil.handlingFacing = true;
            workToil.activeSkill = () => SkillDefOf.Crafting;
            workToil.FailOnCannotTouch(BenchInd, PathEndMode.InteractionCell);

            workToil.initAction = delegate
            {
                Thing item = Item;
                if (item == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                float workToMake = item.def.GetStatValueAbstract(StatDefOf.WorkToMake, item.Stuff);
                if (workToMake <= 0f)
                    workToMake = 1000f;

                totalWork = Mathf.Clamp(workToMake * 0.15f, 300f, 1500f);

                if (workLeft <= 0f)
                    workLeft = totalWork;
            };

            workToil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Bench);

                float pawnSpeed = pawn.GetStatValue(StatDefOf.GeneralLaborSpeed, true);
                float benchFactor = Bench.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor, true);
                int skillLevel = pawn?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
                float skillBonus = 1f + (skillLevel * 0.03f);

                workLeft -= pawnSpeed * benchFactor * skillBonus;
                pawn.skills?.Learn(SkillDefOf.Crafting, 0.08f);

                if (workLeft <= 0f)
                    ReadyForNextToil();
            };

            workToil.WithProgressBar(ItemInd, delegate
            {
                if (totalWork <= 0f) return 0f;
                return 1f - (workLeft / totalWork);
            });

            yield return workToil;

            // Completion: remove taint
            Toil finishToil = ToilMaker.MakeToil("R4_Clean_Finish");
            finishToil.defaultCompleteMode = ToilCompleteMode.Instant;
            finishToil.initAction = delegate
            {
                Thing item = Item;
                if (item == null || item.Destroyed)
                    return;

                if (item is Apparel apparel)
                {
                    apparel.WornByCorpse = false;
                    apparel.Notify_ColorChanged();
                    Log.Message($"[R4] Cleaned taint from: {apparel.LabelCap}");
                }

                // Remove only the clean designation
                var des = pawn.Map.designationManager.DesignationOn(item, R4DefOf.R4_Clean);
                if (des != null)
                    pawn.Map.designationManager.RemoveDesignation(des);
            };

            yield return finishToil;
        }
    }
}
