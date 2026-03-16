using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RRRR
{
    /// <summary>
    /// Gizmo-driven recycle job: haul item to workbench, do work, spawn materials, destroy item.
    /// TargetA = workbench, TargetB = item to recycle.
    /// </summary>
    public class JobDriver_R4Recycle : JobDriver
    {
        private const TargetIndex BenchInd = TargetIndex.A;
        private const TargetIndex ItemInd = TargetIndex.B;

        private Building_WorkTable Bench => (Building_WorkTable)job.GetTarget(BenchInd).Thing;
        private Thing Item => job.GetTarget(ItemInd).Thing;

        private float workLeft;
        private float totalWork;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(Bench, job, errorOnFailed: errorOnFailed))
                return false;
            if (!pawn.Reserve(Item, job, errorOnFailed: errorOnFailed))
                return false;
            return true;
        }

        public override string GetReport()
        {
            return "RRRR_JobString_Recycle".Translate(Item.LabelCap);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Fail conditions
            this.FailOnDespawnedNullOrForbidden(BenchInd);
            this.FailOnDespawnedNullOrForbidden(ItemInd);
            this.FailOnBurningImmobile(BenchInd);

            // 1. Go to the item
            yield return Toils_Goto.GotoThing(ItemInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(ItemInd);

            // 2. Pick up the item
            yield return Toils_Haul.StartCarryThing(ItemInd);

            // 3. Go to the workbench interaction cell
            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

            // 4. Place item near bench (on the interaction cell)
            Toil placeItem = ToilMaker.MakeToil("PlaceItemAtBench");
            placeItem.initAction = () =>
            {
                if (pawn.carryTracker.CarriedThing != null)
                {
                    pawn.carryTracker.TryDropCarriedThing(Bench.InteractionCell, ThingPlaceMode.Near, out _);
                }
            };
            yield return placeItem;

            // 5. Do the recycling work
            Toil doWork = ToilMaker.MakeToil("DoRecycleWork");
            doWork.initAction = () =>
            {
                totalWork = MaterialUtility.RecycleWorkAmount(Item);
                workLeft = totalWork;
            };
            doWork.tickAction = () =>
            {
                float speed = pawn.GetStatValue(StatDefOf.GeneralLaborSpeed);
                workLeft -= speed;

                pawn.skills?.Learn(SkillDefOf.Crafting, 0.05f);

                if (workLeft <= 0f)
                {
                    ReadyForNextToil();
                }
            };
            doWork.defaultCompleteMode = ToilCompleteMode.Never;
            doWork.WithProgressBar(BenchInd, () => 1f - workLeft / totalWork);
            doWork.FailOnCannotTouch(BenchInd, PathEndMode.InteractionCell);
            doWork.activeSkill = () => SkillDefOf.Crafting;
            yield return doWork;

            // 6. Finish: calculate returns, spawn materials, destroy item
            Toil finish = ToilMaker.MakeToil("FinishRecycle");
            finish.initAction = () =>
            {
                Thing item = Item;
                if (item == null || item.Destroyed)
                    return;

                // Calculate returns
                var returns = MaterialUtility.CalculateRecycleReturn(item, pawn);

                // Spawn material stacks
                foreach (var mat in returns)
                {
                    if (mat.count <= 0) continue;

                    Thing stack = ThingMaker.MakeThing(mat.thingDef);
                    stack.stackCount = mat.count;
                    GenPlace.TryPlaceThing(stack, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                }

                // Clear designation and destroy
                var comp = item.TryGetComp<CompRecyclable>();
                if (comp != null)
                    comp.Designation = R4Designation.None;

                item.Destroy();
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workLeft, "workLeft", 0f);
            Scribe_Values.Look(ref totalWork, "totalWork", 100f);
        }
    }
}
