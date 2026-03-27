using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Clean job: haul tainted apparel to workbench → work → remove taint.
    /// Always succeeds. Skill reduces work time. No failure mechanics.
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

            // 1. Go to item
            yield return Toils_Goto.GotoThing(ItemInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(ItemInd);

            // 2. Pick up
            yield return Toils_Haul.StartCarryThing(ItemInd);

            // 3. Carry to bench
            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

            // 4. Drop near bench
            Toil dropToil = ToilMaker.MakeToil("R4_Clean_Drop");
            dropToil.defaultCompleteMode = ToilCompleteMode.Instant;
            dropToil.initAction = delegate
            {
                if (pawn.carryTracker.CarriedThing != null)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            };
            yield return dropToil;

            // 5. Work toil — flat work amount, skill reduces time
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

                // Flat work: 15% of original crafting time, clamped
                float workToMake = item.def.GetStatValueAbstract(StatDefOf.WorkToMake, item.Stuff);
                if (workToMake <= 0f)
                    workToMake = 1000f;

                totalWork = Mathf.Clamp(workToMake * 0.15f, 300f, 1500f);

                // On fresh start, set workLeft. On save-load, already restored.
                if (workLeft <= 0f)
                    workLeft = totalWork;
            };

            workToil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Bench);

                float pawnSpeed = pawn.GetStatValue(StatDefOf.GeneralLaborSpeed, true);
                float benchFactor = Bench.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor, true);

                // Skill bonus: higher crafting skill = faster cleaning
                int skillLevel = pawn?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
                float skillBonus = 1f + (skillLevel * 0.03f); // skill 10 = 1.3x, skill 20 = 1.6x

                float workDone = pawnSpeed * benchFactor * skillBonus;
                workLeft -= workDone;

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

            // 6. Completion: remove taint, remove designation
            Toil finishToil = ToilMaker.MakeToil("R4_Clean_Finish");
            finishToil.defaultCompleteMode = ToilCompleteMode.Instant;
            finishToil.initAction = delegate
            {
                Thing item = Item;
                if (item == null || item.Destroyed)
                    return;

                if (item is Apparel apparel)
                {
                    // Remove taint — public setter in RimWorld 1.6
                    apparel.WornByCorpse = false;

                    // Force render update so the corpse tint is removed visually
                    apparel.Notify_ColorChanged();

                    Log.Message($"[R4] Cleaned taint from: {apparel.LabelCap}");
                }

                // Remove designation
                pawn.Map.designationManager.RemoveAllDesignationsOn(item);
            };

            yield return finishToil;
        }
    }
}
